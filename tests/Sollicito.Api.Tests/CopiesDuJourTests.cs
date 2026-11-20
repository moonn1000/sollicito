using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// La copie du jour (#26, ADR-0008), sur une vraie base SQLite : faite au lancement si elle manque, refaite à la
/// fermeture, faite le lendemain par la minuterie, et gardée parmi les sept dernières de l'installation. Chaque test
/// garde son dossier de données jusqu'à la fin, pour relire les copies après l'arrêt de l'API. Données fictives.
/// </summary>
public sealed class CopiesDuJourTests : IAsyncLifetime
{
    // Le 18/11/2026 à 14:30 à Bruxelles.
    private static readonly DateOnly Le18 = new(2026, 11, 18);

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private string Base => new DossierDeLaBase(_dossier).Fichier;

    private string ParDefaut => new DossierDeLaBase(_dossier).DossierDesSauvegardes;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_dossier);

    [Fact]
    public async Task Au_lancement_la_copie_du_jour_se_fait_dans_le_dossier_par_defaut_et_se_relit_saine()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        var etat = await api.AttendreLaPremiereVerificationAsync();

        var copie = api.CopieDuJour(Le18);
        Assert.Equal([copie], Fichiers(ParDefaut));
        Assert.True(EstSaine(Path.Combine(ParDefaut, copie)));
        Assert.Equal((ParDefaut, true, (EchecDUneCopie?)null), (etat.Dossier, etat.ParDefaut, etat.Echec));
        Assert.Equal([copie], etat.Copies.Select(gardee => gardee.Nom));
        Assert.Equal(SorteDeCopie.DuJour, Assert.Single(etat.Copies).Sorte);
        Assert.NotNull(etat.DerniereCopie);
    }

    [Fact]
    public async Task Une_copie_emporte_ce_qui_est_valide_meme_dans_le_journal_WAL_et_rien_d_une_ecriture_en_cours()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        Assert.Equal("wal", ModeDuJournal(Base));

        // Une lecture tenue ouverte empêche le point de reprise : ce qui suit reste dans le journal.
        using var temoin = Ouvrir(Base);
        temoin.Executer("PRAGMA wal_autocheckpoint = 0");
        temoin.Executer("BEGIN");
        temoin.Executer("SELECT COUNT(*) FROM Candidatures");
        await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
        Assert.True(new FileInfo(Base + "-wal").Length > 0);

        // Une écriture commencée, jamais validée pendant la copie.
        using var enCours = Ouvrir(Base);
        enCours.Executer("BEGIN IMMEDIATE");
        enCours.Executer("INSERT INTO Entreprises (Nom, Cle) VALUES ('Écriture En Cours', 'ecriture en cours')");

        var etat = await api.SauvegarderMaintenantAsync();
        enCours.Executer("ROLLBACK");

        Assert.Null(etat["echec"]);
        var copie = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
        Assert.Equal(["Comptable"], PostesDe(copie));
        Assert.Equal(["Lumière Verte SA"], EntreprisesDe(copie));
        Assert.True(EstSaine(copie));
    }

    [Fact]
    public async Task A_la_fermeture_la_copie_du_jour_se_refait_et_garde_la_fin_de_la_seance()
    {
        string copie;
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
            copie = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
            Assert.Empty(PostesDe(copie));

            await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
        }

        // La même copie, refaite : elle porte la candidature enregistrée après le lancement.
        Assert.Equal([Path.GetFileName(copie)], Fichiers(ParDefaut));
        Assert.Equal(["Comptable"], PostesDe(copie));
        Assert.True(EstSaine(copie));
    }

    [Fact]
    public async Task Un_second_lancement_le_meme_jour_ne_refait_pas_la_copie_avant_sa_fermeture()
    {
        string copie;
        await using (var premier = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await premier.AttendreLaPremiereVerificationAsync();
            copie = Path.Combine(ParDefaut, premier.CopieDuJour(Le18));
        }

        var apresLePremier = File.GetLastWriteTimeUtc(copie);
        await using (var second = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            var etat = await second.AttendreLaPremiereVerificationAsync();

            Assert.Equal(apresLePremier, File.GetLastWriteTimeUtc(copie));
            Assert.Equal([Path.GetFileName(copie)], etat.Copies.Select(gardee => gardee.Nom));
        }

        Assert.True(File.GetLastWriteTimeUtc(copie) > apresLePremier);
    }

    [Fact]
    public async Task Une_fermeture_ne_fait_qu_une_copie_finie_avant_la_fin_de_l_arret()
    {
        // Sous l'hôte des tests, l'arrêt des services s'appelle deux fois pour un seul hôte, et la fermeture n'attend que
        // l'un des deux appels : une seconde copie finissait après l'arrêt (relevé du 2026-11-20).
        var capture = new CaptureDesJournaux();
        int CopiesFaites() => capture.Entrees.Count(entree => entree.Contains("Copie du jour faite", StringComparison.Ordinal));
        for (var lancement = 1; lancement <= 3; lancement++)
        {
            await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest(), builder => builder.ConfigureLogging(journaux => journaux.AddProvider(capture))))
            {
                await api.AttendreLaPremiereVerificationAsync();
            }

            var apresLArret = CopiesFaites();
            await Task.Delay(300, TestContext.Current.CancellationToken);

            // Le premier lancement copie au départ et à la fermeture ; les suivants, le même jour, à la fermeture seulement.
            Assert.Equal(lancement + 1, apresLArret);
            Assert.Equal(apresLArret, CopiesFaites());
        }
    }

    [Fact]
    public async Task Reste_ouvert_apres_minuit_Sollicito_fait_la_copie_du_nouveau_jour_dans_l_heure()
    {
        // Le 18/11/2026 à 23:30 à Bruxelles.
        var horloge = new HorlogeAMinuterie(new DateTimeOffset(2026, 11, 18, 22, 30, 0, TimeSpan.Zero));
        await using var api = await LancerSurAsync(_dossier, horloge);
        await api.AttendreLaPremiereVerificationAsync();
        Assert.Equal([api.CopieDuJour(Le18)], Fichiers(ParDefaut));
        Assert.Equal(TimeSpan.FromHours(1), SauvegardesDeLaBase.PeriodeDesVerifications);

        // Une heure plus tard, la minuterie de l'API part : il est 00:30, le 19.
        await AttendreAsync(() => horloge.MinuteriesActives > 0);
        horloge.Avancer(SauvegardesDeLaBase.PeriodeDesVerifications);

        await AttendreAsync(() => Fichiers(ParDefaut).Length == 2);
        Assert.Equal([api.CopieDuJour(Le18), api.CopieDuJour(new DateOnly(2026, 11, 19))], Fichiers(ParDefaut));
    }

    [Fact]
    public async Task La_rotation_garde_les_sept_dernieres_copies_du_jour_de_l_installation_et_rien_d_autre()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        var installation = api.Sauvegardes().Installation;
        // Huit jours d'avant, un autre poste, une copie d'avant une mise à jour, et des fichiers de l'utilisateur.
        string[] etrangers =
        [
            "sollicito-99aa88bb-2026-11-01.db",
            "sollicito-avant-20261119133602_CandidaturesEtEntreprises.db",
            "notes-personnelles.txt",
            "sollicito.db",
        ];
        foreach (var nom in Enumerable.Range(1, 8).Select(jour => NomsDesCopies.DuJour(installation, new DateOnly(2026, 11, jour))).Concat(etrangers))
        {
            await File.WriteAllTextAsync(Path.Combine(ParDefaut, nom), "fictif", TestContext.Current.CancellationToken);
        }

        await api.SauvegarderMaintenantAsync();

        string[] attendues = [.. new[] { 18, 8, 7, 6, 5, 4, 3 }.Select(jour => NomsDesCopies.DuJour(installation, new DateOnly(2026, 11, jour)))];
        Assert.Equal([.. attendues.Concat(etrangers).Order(StringComparer.Ordinal)], Fichiers(ParDefaut));
    }

    [Fact]
    public async Task Une_copie_manquee_n_efface_aucune_copie_et_ne_laisse_aucun_fichier_provisoire()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        var installation = api.Sauvegardes().Installation;
        var anciennes = Enumerable.Range(1, 8).Select(jour => NomsDesCopies.DuJour(installation, new DateOnly(2026, 11, jour))).ToArray();
        foreach (var nom in anciennes)
        {
            await File.WriteAllTextAsync(Path.Combine(ParDefaut, nom), "fictif", TestContext.Current.CancellationToken);
        }

        // Un dossier à la place de la copie du jour : la copie s'écrit, se vérifie, mais ne peut pas prendre son nom.
        var copie = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
        await EffacerAsync(copie);
        Directory.CreateDirectory(copie);

        var etat = await api.SauvegarderMaintenantAsync();

        Assert.Equal(nameof(MotifDEchec.CopieImpossible), etat["echec"]!["motif"]!.GetValue<string>());
        Assert.Equal([.. anciennes.Order(StringComparer.Ordinal)], Fichiers(ParDefaut));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(ParDefaut, "*.partielle"), _ => true);
    }

    [Fact]
    public async Task Une_copie_de_fermeture_manquee_n_empeche_pas_la_fermeture_et_la_copie_d_avant_reste()
    {
        string hier;
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
            hier = Path.Combine(ParDefaut, NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 17)));
            File.Copy(Path.Combine(ParDefaut, api.CopieDuJour(Le18)), hier);
            var copie = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
            await EffacerAsync(copie);
            Directory.CreateDirectory(copie);
        }

        // L'API s'est arrêtée ; la copie d'hier est intacte, et rien de provisoire ne reste.
        Assert.True(EstSaine(hier));
        Assert.Equal([Path.GetFileName(hier)], Fichiers(ParDefaut));
    }

    [Fact]
    public async Task Une_copie_de_fermeture_trop_longue_est_abandonnee_et_la_copie_d_avant_reste()
    {
        var horloge = new HorlogeAMinuterie(new DateTimeOffset(2026, 11, 18, 13, 30, 0, TimeSpan.Zero));
        var api = await LancerSurAsync(_dossier, horloge);
        string copie;
        DateTime avant;
        try
        {
            await api.AttendreLaPremiereVerificationAsync();
            copie = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
            avant = File.GetLastWriteTimeUtc(copie);
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }

        // Une connexion en verrouillage exclusif : la copie de la fermeture attend la base, sans fin pour elle.
        var verrou = Ouvrir(Base);
        try
        {
            verrou.Executer("PRAGMA locking_mode = EXCLUSIVE");
            verrou.Executer("BEGIN EXCLUSIVE");
            verrou.Executer("COMMIT");

            var fermeture = api.DisposeAsync().AsTask();
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.False(fermeture.IsCompleted);
            horloge.Avancer(SauvegardesDeLaBase.DelaiDeLaCopieDeFermeture);

            await fermeture.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(TimeSpan.FromSeconds(10), SauvegardesDeLaBase.DelaiDeLaCopieDeFermeture);
            Assert.Equal(avant, File.GetLastWriteTimeUtc(copie));
        }
        finally
        {
            verrou.Dispose();
        }

        // La copie abandonnée finit seule, une fois la base rendue : on l'attend avant d'effacer le dossier.
        await AttendreAsync(() => !Directory.EnumerateFiles(ParDefaut, "*.partielle").Any() && File.GetLastWriteTimeUtc(copie) > avant);
    }

    [Fact]
    public async Task Une_base_neuve_se_cree_en_mode_WAL_et_une_base_restee_en_mode_delete_y_passe_au_lancement_sans_rien_perdre()
    {
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
            await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
        }

        Assert.Equal("wal", ModeDuJournal(Base));
        // Une base créée avant #26, restée dans le mode par défaut : #79 ouvrait la base avant les migrations, qui ne la
        // créaient donc plus elles-mêmes.
        using (var connexion = Ouvrir(Base))
        {
            connexion.Executer("PRAGMA journal_mode = DELETE");
        }

        Assert.Equal("delete", ModeDuJournal(Base));
        await using (var relancee = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await relancee.AttendreLaPremiereVerificationAsync();

            var liste = await relancee.ListerAsync();
            var candidature = Assert.Single(liste)!;
            Assert.Equal(("Lumière Verte SA", "Comptable", "2026-11-02T09:00:00Z"), (candidature["entreprise"]!["nom"]!.GetValue<string>(), candidature["poste"]!.GetValue<string>(), candidature["envoyeeLe"]!.GetValue<string>()));
        }

        Assert.Equal("wal", ModeDuJournal(Base));
        Assert.True(EstSaine(Base));
    }

    private static async Task AttendreAsync(Func<bool> condition)
    {
        var debut = DateTime.UtcNow;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow - debut < TimeSpan.FromSeconds(40), "La condition attendue n'est pas venue.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
