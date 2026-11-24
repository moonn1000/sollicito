using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les journaux des sauvegardes (#26) : un chemin de sauvegarde porte en général le nom du compte Windows (#80, point 1).
/// Ni le dossier de données, ni le dossier des sauvegardes, ni une copie, ni une donnée de la base n'entrent dans un
/// journal, sur aucun chemin : copies réussies et manquées, dossier choisi, refusé ou manquant, réglage illisible, copie
/// d'avant une mise à jour, migration ratée, et l'échec d'un lancement que l'hôte écrit en entier. Chaque dossier porte un
/// nom qui imite un compte Windows. Toutes les données sont fictives.
/// </summary>
public sealed class JournauxDesSauvegardesTests : IAsyncLifetime
{
    private const string Compte = "Compte.Fictif-Dupont";
    private const string Donnee = "Donnée-Fictive-Sauvegarde";

    // Chaque catégorie au niveau le plus bavard, y compris par un réglage propre au fournisseur de test.
    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    private readonly string _racine = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
    private readonly CaptureDesJournaux _capture = new();
    private readonly List<string> _installations = [];

    private string Profil => Path.Combine(_racine, "Users", Compte);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_racine);

    [Fact]
    public async Task Aucun_chemin_ni_aucune_donnee_n_entre_dans_un_journal_des_sauvegardes()
    {
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        var cle = Path.Combine(Profil, "Cle USB", "Sauvegardes");
        Directory.CreateDirectory(donnees);
        Directory.CreateDirectory(cle);
        // Une base d'avant #25 : son lancement fait la copie d'avant une mise à jour, puis la migration sur une copie.
        await BaseDe12.PeuplerAsync(new DossierDeLaBase(donnees));

        await using (var api = await LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser))
        {
            await api.AttendreLaPremiereVerificationAsync();
            _installations.Add(api.Sauvegardes().Installation);
            await api.EnregistrerAvecSuccesAsync(Donnee, Donnee, "2026-11-02T10:00");
            await api.SauvegarderMaintenantAsync();
            await api.LireLesSauvegardesAsync();

            // Un dossier choisi, puis un dossier absent, refusé.
            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(cle, TestContext.Current.CancellationToken));
            Assert.Equal(IssueDuChoix.Refuse, await api.Sauvegardes().ChoisirLeDossierAsync(Path.Combine(Profil, "Absent"), TestContext.Current.CancellationToken));

            // La clé retirée, puis rebranchée.
            await SupprimerAsync(cle);
            await api.SauvegarderMaintenantAsync();
            await api.Sauvegardes().FaireLaCopieDuJourSiBesoinAsync(TestContext.Current.CancellationToken);
            Directory.CreateDirectory(cle);
            await api.Sauvegardes().FaireLaCopieDuJourSiBesoinAsync(TestContext.Current.CancellationToken);

            // Une copie qui s'écrit, mais ne peut pas prendre son nom.
            var copie = Path.Combine(cle, api.CopieDuJour(new DateOnly(2026, 11, 18)));
            await EffacerAsync(copie);
            Directory.CreateDirectory(copie);
            await api.SauvegarderMaintenantAsync();
            Directory.Delete(copie);

            // Le retour au dossier par défaut, réussi puis impossible.
            using (await api.RevenirAuDossierParDefautAsync())
            {
            }

            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(cle, TestContext.Current.CancellationToken));
            var parDefaut = new DossierDeLaBase(donnees).DossierDesSauvegardes;
            await SupprimerAsync(parDefaut);
            await File.WriteAllTextAsync(parDefaut, "fictif", TestContext.Current.CancellationToken);
            using (await api.RevenirAuDossierParDefautAsync())
            {
            }
        }

        // Un réglage illisible, sur un autre dossier de données.
        var illisible = Path.Combine(Profil, "AppData", "Local", "Sollicito-Illisible");
        Directory.CreateDirectory(illisible);
        await File.WriteAllTextAsync(Path.Combine(illisible, ReglagesDeLApplication.NomDuFichier), $$"""{ "dossierDesSauvegardes": "{{Compte}}" """, TestContext.Current.CancellationToken);
        await using (var api = await LancerSurAsync(illisible, new HorlogeDeTest(), ToutJournaliser))
        {
            Assert.True((await api.AttendreLaPremiereVerificationAsync()).ReglageIllisible);
            _installations.Add(api.Sauvegardes().Installation);
        }

        var entrees = _capture.Entrees.ToArray();
        // Témoins : chaque chemin a bien été parcouru.
        Assert.Contains(entrees, entree => entree.Contains("Base mise à jour sur une copie", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie du jour faite", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie du jour manquée (DossierIntrouvable)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie du jour manquée (UnauthorizedAccessException)", StringComparison.Ordinal) || entree.Contains("Copie du jour manquée (IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Dossier des sauvegardes choisi", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Dossier des sauvegardes refusé", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Réglages illisibles", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    [Fact]
    public async Task Une_mise_a_jour_ratee_et_son_echec_de_lancement_n_ecrivent_aucun_chemin()
    {
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        Directory.CreateDirectory(donnees);
        await BaseDe12.PeuplerAsync(new DossierDeLaBase(donnees));
        using (var connexion = Ouvrir(new DossierDeLaBase(donnees).Fichier))
        {
            connexion.Executer("""CREATE INDEX "IX_EvenementsDeCandidature_EtapeCorrigeeId" ON "EvenementsDeCandidature" ("Type")""");
        }

        await Assert.ThrowsAnyAsync<Exception>(() => LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser));

        // Puis une copie d'avant impossible : un fichier à la place du dossier des sauvegardes.
        var parDefaut = new DossierDeLaBase(donnees).DossierDesSauvegardes;
        await SupprimerAsync(parDefaut);
        await File.WriteAllTextAsync(parDefaut, "fictif", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser));

        var entrees = _capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Mise à jour de la base échouée (Migration, SqliteException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Mise à jour de la base échouée (CopieDAvant", StringComparison.Ordinal));
        // L'hôte écrit l'échec du lancement avec toute la chaîne de l'exception.
        Assert.Contains(entrees, entree => entree.Contains("Hosting failed to start", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    [Fact]
    public async Task Une_copie_ancienne_un_reglage_ou_un_instant_qui_ne_s_ecrivent_pas_n_ecrivent_que_le_type_de_l_erreur()
    {
        // Trois chemins des sauvegardes ont une erreur d'écriture en main, et n'en écrivent que le type (#88, point 14) :
        // la copie ancienne qu'on ne peut effacer, le réglage qu'on ne peut écrire, l'instant de la dernière copie qu'on
        // ne peut garder. L'erreur de la première cite son fichier, donc le nom du compte, et `SansCheminNiDonnee` la garde ;
        // celles des deux autres, un accès refusé, n'en citent aucun : seul le libellé exact des témoins, qui porte le
        // type, les garde.
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        var cle = Path.Combine(Profil, "Cle USB", "Sauvegardes");
        Directory.CreateDirectory(donnees);
        Directory.CreateDirectory(cle);

        await using (var api = await LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser))
        {
            await api.AttendreLaPremiereVerificationAsync();
            _installations.Add(api.Sauvegardes().Installation);
            var parDefaut = new DossierDeLaBase(donnees).DossierDesSauvegardes;

            // Huit copies plus anciennes que celle du jour ; la plus ancienne est tenue : la rotation ne peut pas l'effacer.
            for (var jour = 1; jour <= 8; jour++)
            {
                await File.WriteAllTextAsync(Path.Combine(parDefaut, api.CopieDuJour(new DateOnly(2026, 10, jour))), "fictif", TestContext.Current.CancellationToken);
            }

            using (new FileStream(Path.Combine(parDefaut, api.CopieDuJour(new DateOnly(2026, 10, 1))), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await api.SauvegarderMaintenantAsync();
            }

            // L'instant de la dernière copie : son fichier est tenu pendant une copie.
            var instant = Path.Combine(donnees, "sauvegardes.json");
            Assert.True(File.Exists(instant));
            using (new FileStream(instant, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await api.SauvegarderMaintenantAsync();
            }

            // Le réglage du dossier : son fichier est tenu pendant le choix.
            var reglages = Path.Combine(donnees, ReglagesDeLApplication.NomDuFichier);
            await File.WriteAllTextAsync(reglages, "{}", TestContext.Current.CancellationToken);
            using (new FileStream(reglages, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Equal(IssueDuChoix.Refuse, await api.Sauvegardes().ChoisirLeDossierAsync(cle, TestContext.Current.CancellationToken));
            }
        }

        var entrees = _capture.Entrees.ToArray();
        // Témoins : chaque chemin a bien été parcouru, et n'a écrit que le type de l'erreur.
        Assert.Contains(entrees, entree => entree.Contains("Une copie ancienne n'a pas pu être effacée (IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Instant de la dernière copie non gardé (UnauthorizedAccessException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Réglages non écrits (UnauthorizedAccessException)", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    [Fact]
    public async Task Un_dossier_de_donnees_qui_ne_peut_pas_se_creer_n_ecrit_pas_son_chemin_dans_le_journal_de_l_hote()
    {
        // La sonde de #80, point 1 : un fichier qui porte le nom du dossier de données.
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        Directory.CreateDirectory(Path.GetDirectoryName(donnees)!);
        await File.WriteAllTextAsync(donnees, "fictif", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser));

        var entrees = _capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("La base de données n'a pas pu être créée ni mise à jour (IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Hosting failed to start", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    [Fact]
    public async Task Aucun_chemin_ni_aucune_donnee_n_entre_dans_un_journal_de_la_restauration()
    {
        // La restauration (#28) sur chacun de ses chemins : réussie, refusée pour chaque motif, impossible, porte fermée,
        // et depuis une base abîmée.
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        Directory.CreateDirectory(donnees);
        var parDefaut = new DossierDeLaBase(donnees).DossierDesSauvegardes;
        var etapes = new EtapesDeTest { Arret = Application.Sauvegardes.EtapeDeLaRestauration.PorteFermee };
        await using (var api = await LancerSurAsync(donnees, new HorlogeDeTest(), builder =>
        {
            ToutJournaliser(builder);
            AppelsDeLaRestauration.AvecLesEtapes(etapes)(builder);
        }))
        {
            await api.AttendreLaPremiereVerificationAsync();
            _installations.Add(api.Sauvegardes().Installation);
            await api.EnregistrerAvecSuccesAsync(Donnee, Donnee, "2026-11-02T10:00");
            await api.SauvegarderMaintenantAsync();
            var duJour = api.Copie(api.CopieDuJour(new DateOnly(2026, 11, 18)));

            // Réussie, porte fermée un moment : une requête refusée.
            var restauration = Task.Run(() => api.RestaurerAsync(duJour));
            await etapes.Atteinte.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            using (await api.EnregistrerAsync(Donnee, Donnee, "2026-11-03T10:00"))
            {
            }

            etapes.Relacher();
            Assert.Equal(Application.Sauvegardes.IssueDeLaRestauration.Restauree, await restauration);
            etapes.Arret = null;

            // Refusée : abîmée, étrangère, plus récente, puis changée et introuvable.
            var saine = Path.Combine(parDefaut, api.CopieDuJour(new DateOnly(2026, 11, 18)));
            var abimee = Application.Sauvegardes.NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 10));
            var etrangere = Application.Sauvegardes.NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 11));
            var plusRecente = Application.Sauvegardes.NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 12));
            var octets = Octets(saine);
            Array.Fill(octets, (byte)0x5A, octets.Length / 2 / 4096 * 4096, 4096);
            await File.WriteAllBytesAsync(Path.Combine(parDefaut, abimee), octets, TestContext.Current.CancellationToken);
            using (var connexion = Ouvrir(Path.Combine(parDefaut, etrangere), Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate))
            {
                connexion.Executer($"CREATE TABLE Notes (Texte TEXT); INSERT INTO Notes VALUES ('{Donnee} {Compte}')");
            }
            File.Copy(saine, Path.Combine(parDefaut, plusRecente));
            using (var connexion = Ouvrir(Path.Combine(parDefaut, plusRecente)))
            {
                connexion.Executer("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29991231000000_UneVersionAVenir', '10.0.15')");
            }

            await api.SauvegarderMaintenantAsync();
            await api.RestaurerAsync(api.Copie(abimee));
            await api.RestaurerAsync(api.Copie(etrangere));
            await api.RestaurerAsync(api.Copie(plusRecente));
            var demande = (await api.Restauration().PreparerAsync(api.Copie(api.CopieDuJour(new DateOnly(2026, 11, 18))).Id, TestContext.Current.CancellationToken)).Demande!;
            await api.SauvegarderMaintenantAsync();
            await api.Restauration().RestaurerAsync(demande, TestContext.Current.CancellationToken);
            await EffacerAsync(Path.Combine(parDefaut, plusRecente));
            await api.Restauration().RestaurerAsync(demande with { Nom = plusRecente }, TestContext.Current.CancellationToken);

            // Impossible : la place qui manque, à la copie de travail.
            etapes.Arret = Application.Sauvegardes.EtapeDeLaRestauration.CopieDeTravailEcrite;
            etapes.Erreur = new IOException($"Disque plein (essai) : {Compte}", unchecked((int)0x80070070));
            await api.RestaurerAsync(api.Copie(api.CopieDuJour(new DateOnly(2026, 11, 18))));
            etapes.Arret = null;

            // Depuis une base abîmée : sa copie brute.
            var fichier = new DossierDeLaBase(donnees).Fichier;
            var base_ = Octets(fichier);
            Array.Fill(base_, (byte)0x5A, Math.Max(1, base_.Length / 4096 / 2) * 4096, 4096);
            await File.WriteAllBytesAsync(fichier, base_, TestContext.Current.CancellationToken);
            await api.RestaurerAsync(api.Copie(api.CopieDuJour(new DateOnly(2026, 11, 18))));
        }

        var entrees = _capture.Entrees.ToArray();
        // Témoins : chaque chemin a bien été parcouru.
        Assert.Contains(entrees, entree => entree.Contains("Base restaurée depuis une copie (DuJour, 0 migration(s)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Requête refusée : une restauration de la base est en cours.", StringComparison.Ordinal));
        foreach (var motif in new[] { "Abimee", "Etrangere", "PlusRecente", "Changee", "Introuvable" })
        {
            Assert.Contains(entrees, entree => entree.Contains($"Restauration refusée ({motif})", StringComparison.Ordinal));
        }

        Assert.Contains(entrees, entree => entree.Contains("Restauration impossible (CopieDeTravail, IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie d'avant la restauration faite (verifiee)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie d'avant la restauration faite (brute)", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
        Assert.DoesNotContain(entrees, entree => entree.Contains("sollicito-avant-restauration", StringComparison.OrdinalIgnoreCase));
    }

    private void ToutJournaliser(IWebHostBuilder builder) =>
        builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LesPlusBavards))
            .ConfigureLogging(journaux => journaux.AddProvider(_capture));

    // Ni le compte, ni un dossier ou une copie, ni la donnée, ni le nom d'une copie (son identifiant d'installation).
    private void SansCheminNiDonnee(string[] entrees)
    {
        Assert.NotEmpty(entrees);
        string[] interdits = [Compte, _racine, Donnee, "Cle USB", .. _installations];
        foreach (var interdit in interdits)
        {
            Assert.DoesNotContain(entrees, entree => entree.Contains(interdit, StringComparison.OrdinalIgnoreCase));
        }
    }
}
