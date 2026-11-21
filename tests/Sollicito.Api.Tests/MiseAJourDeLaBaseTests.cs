using System.Security.Cryptography;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// La copie d'avant une mise à jour, et la migration sur une copie (#26, qui absorbe #27 ; ADR-0008) : une base peuplée à
/// une migration antérieure se copie, puis se met à jour sans rien perdre ; une migration qui échoue laisse la base telle
/// qu'avant, et le lancement s'arrête sur son message. Toutes les données sont fictives.
/// </summary>
public sealed class MiseAJourDeLaBaseTests : IAsyncLifetime
{
    private static readonly TimeSpan DelaiDuLancement = TimeSpan.FromSeconds(60);

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private DossierDeLaBase DossierDeLaBase => new(_dossier);

    private string CopieDAvant => Path.Combine(DossierDeLaBase.DossierDesSauvegardes, NomsDesCopies.AvantMiseAJour(BaseDe12.DerniereMigration));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_dossier);

    [Fact]
    public async Task Une_base_peuplee_a_une_migration_anterieure_se_copie_puis_se_met_a_jour_sans_rien_perdre()
    {
        await BaseDe12.PeuplerAsync(DossierDeLaBase);
        Assert.Equal("wal", ModeDuJournal(DossierDeLaBase.Fichier));

        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            var etat = await api.AttendreLaPremiereVerificationAsync();

            // La base est à jour, en mode WAL, et ses dates n'ont pas bougé.
            Assert.Equal(BaseDe12.DerniereMigration, MigrationsDe(DossierDeLaBase.Fichier)[^1]);
            Assert.Equal("wal", ModeDuJournal(DossierDeLaBase.Fichier));
            var fiche = await api.FicheAsync(1);
            Assert.Equal("2026-11-02T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
            Assert.Equal(
                ["2026-11-10T09:00:00Z", "2026-11-18T13:30:00Z", "2026-11-18T14:30:00Z"],
                fiche["historique"]!.AsArray().Select(evenement => evenement!["survenuLe"]!.GetValue<string>()));

            // La copie d'avant garde la base telle qu'elle était, à la migration de #12, et l'état la montre à part.
            Assert.True(EstSaine(CopieDAvant));
            Assert.Equal(BaseDe12.Migration, MigrationsDe(CopieDAvant)[^1]);
            Assert.Equal(["Comptable", "Brasseuse"], PostesDe(CopieDAvant));
            var avant = Assert.Single(etat.Copies, copie => copie.Sorte == SorteDeCopie.AvantMiseAJour);
            Assert.Equal(Path.GetFileName(CopieDAvant), avant.Nom);
        }

        // Aucune copie de travail ne reste dans le dossier de données.
        Assert.DoesNotContain(Fichiers(_dossier), nom => nom.EndsWith(".migration", StringComparison.Ordinal) || nom.Contains(".migration-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_migration_qui_echoue_laisse_la_base_telle_qu_avant_et_arrete_le_lancement_sur_son_message()
    {
        await BaseDe12.PeuplerAsync(DossierDeLaBase);
        // Un index au nom de celui que crée la migration de #25 : elle échouera après avoir ajouté sa colonne.
        using (var connexion = Ouvrir(DossierDeLaBase.Fichier))
        {
            connexion.Executer("""CREATE INDEX "IX_EvenementsDeCandidature_EtapeCorrigeeId" ON "EvenementsDeCandidature" ("Type")""");
        }

        var empreinte = Empreinte(DossierDeLaBase.Fichier);

        var echec = await Assert.ThrowsAnyAsync<Exception>(LancerAsync);

        var miseAJour = Assert.Single(Chaine(echec).OfType<MiseAJourDeLaBaseEchouee>());
        Assert.Equal(RaisonDeLEchec.Migration, miseAJour.Raison);
        Assert.Equal(MiseAJourDeLaBaseEchouee.MessageDeLaMigration, miseAJour.Message);
        Assert.Null(miseAJour.InnerException);
        // La base est celle d'avant, octet pour octet : mêmes migrations, mêmes données, toujours en mode WAL.
        Assert.Equal(empreinte, Empreinte(DossierDeLaBase.Fichier));
        Assert.Equal(BaseDe12.Migration, MigrationsDe(DossierDeLaBase.Fichier)[^1]);
        Assert.Equal("wal", ModeDuJournal(DossierDeLaBase.Fichier));
        // La copie d'avant est là ; aucune copie de travail ne reste.
        Assert.True(EstSaine(CopieDAvant));
        Assert.DoesNotContain(Fichiers(_dossier), nom => nom.Contains(".migration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_nouvel_essai_de_la_meme_mise_a_jour_remplace_sa_copie_d_avant()
    {
        await BaseDe12.PeuplerAsync(DossierDeLaBase);
        using (var connexion = Ouvrir(DossierDeLaBase.Fichier))
        {
            connexion.Executer("""CREATE INDEX "IX_EvenementsDeCandidature_EtapeCorrigeeId" ON "EvenementsDeCandidature" ("Type")""");
        }

        await Assert.ThrowsAnyAsync<Exception>(LancerAsync);
        Assert.Equal(["Comptable", "Brasseuse"], PostesDe(CopieDAvant));

        // Entre deux essais, l'ancienne version de Sollicito a servi : une candidature de plus, et l'obstacle levé.
        using (var connexion = Ouvrir(DossierDeLaBase.Fichier))
        {
            connexion.Executer("""DROP INDEX "IX_EvenementsDeCandidature_EtapeCorrigeeId" """);
            connexion.Executer("INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES (3, 1, 'Contrôleuse de gestion', '2026-11-04 09:00:00', 'Envoyee')");
        }

        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
        }

        // Une seule copie d'avant cette mise à jour : celle du dernier essai, qui porte la candidature de plus.
        Assert.Single(Fichiers(DossierDeLaBase.DossierDesSauvegardes), nom => nom.StartsWith("sollicito-avant-", StringComparison.Ordinal));
        Assert.Equal(["Comptable", "Brasseuse", "Contrôleuse de gestion"], PostesDe(CopieDAvant));
        Assert.Equal(BaseDe12.DerniereMigration, MigrationsDe(DossierDeLaBase.Fichier)[^1]);
    }

    [Fact]
    public async Task Les_restes_d_une_copie_d_avant_interrompue_se_retirent_a_la_mise_a_jour_suivante()
    {
        await BaseDe12.PeuplerAsync(DossierDeLaBase);
        Directory.CreateDirectory(DossierDeLaBase.DossierDesSauvegardes);
        var reste = Path.Combine(DossierDeLaBase.DossierDesSauvegardes, NomsDesCopies.AvantMiseAJour(BaseDe12.DerniereMigration) + ".0a1b2c3d.partielle");
        var autre = Path.Combine(DossierDeLaBase.DossierDesSauvegardes, "notes-personnelles.txt");
        await File.WriteAllTextAsync(reste, "fictif", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(autre, "fictif", TestContext.Current.CancellationToken);

        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
        }

        Assert.False(File.Exists(reste));
        Assert.True(File.Exists(autre));
        Assert.True(EstSaine(CopieDAvant));
    }

    [Fact]
    public async Task Les_restes_d_une_mise_a_jour_interrompue_se_retirent_au_lancement_sans_toucher_a_la_base()
    {
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
        }

        // Ce que laisse un arrêt brutal en pleine mise à jour : la copie de travail et son journal, et les fichiers
        // provisoires des réglages. À côté, des fichiers qui ne sont pas à Sollicito, et le journal de la base elle-même.
        // Les copies de travail se nomment comme MiseAJourDeLaBase les nomme : un changement de leur forme se verrait ici.
        var copieDeTravail = RestesDUnArret.NomDUneCopieDeTravail();
        string[] restes =
        [
            copieDeTravail,
            copieDeTravail + "-journal",
            RestesDUnArret.NomDUneCopieDeTravail() + "-journal",
            "reglages.json.0123456789abcdef0123456789abcdef.provisoire",
            "sauvegardes.json.0123456789abcdef0123456789abcdef.provisoire",
        ];
        string[] autres = ["sollicito.db.copie-personnelle", "notes.migration", "sollicito.db.0123.migration"];
        foreach (var nom in restes.Concat(autres))
        {
            await File.WriteAllTextAsync(Path.Combine(_dossier, nom), "fictif", TestContext.Current.CancellationToken);
        }

        var empreinte = Empreinte(DossierDeLaBase.Fichier);
        await using (var relancee = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await relancee.AttendreLaPremiereVerificationAsync();
        }

        Assert.All(restes, nom => Assert.False(File.Exists(Path.Combine(_dossier, nom)), nom));
        Assert.All(autres, nom => Assert.True(File.Exists(Path.Combine(_dossier, nom)), nom));
        Assert.Equal(empreinte, Empreinte(DossierDeLaBase.Fichier));
    }

    [Fact]
    public async Task Une_copie_d_avant_impossible_n_essaie_aucune_migration_et_le_dit()
    {
        await BaseDe12.PeuplerAsync(DossierDeLaBase);
        // Un fichier à la place du dossier des sauvegardes : la copie d'avant ne peut pas s'écrire.
        await File.WriteAllTextAsync(DossierDeLaBase.DossierDesSauvegardes, "fictif", TestContext.Current.CancellationToken);
        var empreinte = Empreinte(DossierDeLaBase.Fichier);

        var echec = await Assert.ThrowsAnyAsync<Exception>(LancerAsync);

        var miseAJour = Assert.Single(Chaine(echec).OfType<MiseAJourDeLaBaseEchouee>());
        Assert.Equal(RaisonDeLEchec.CopieDAvant, miseAJour.Raison);
        Assert.Equal(MiseAJourDeLaBaseEchouee.MessageDeLaCopieDAvant, miseAJour.Message);
        Assert.Equal(empreinte, Empreinte(DossierDeLaBase.Fichier));
        Assert.Equal(BaseDe12.Migration, MigrationsDe(DossierDeLaBase.Fichier)[^1]);
    }

    [Fact]
    public async Task Sans_migration_en_attente_aucune_copie_d_avant_ne_se_fait()
    {
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
        }

        await using (var relancee = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await relancee.AttendreLaPremiereVerificationAsync();
        }

        Assert.DoesNotContain(Fichiers(DossierDeLaBase.DossierDesSauvegardes), nom => nom.StartsWith("sollicito-avant-", StringComparison.Ordinal));
    }

    // Le lancement se fait sur un autre fil, borné : un lancement qui ne finirait pas ne bloque pas le test.
    private Task<ApiLancee> LancerAsync() =>
        Task.Run(() => LancerSurAsync(_dossier, new HorlogeDeTest())).WaitAsync(DelaiDuLancement, TestContext.Current.CancellationToken);

    private static string Empreinte(string fichier) => Convert.ToHexString(SHA256.HashData(Octets(fichier)));

    private static IEnumerable<Exception> Chaine(Exception erreur)
    {
        for (Exception? courante = erreur; courante is not null; courante = courante.InnerException)
        {
            yield return courante;
            if (courante is AggregateException groupe)
            {
                foreach (var interieure in groupe.InnerExceptions.SelectMany(Chaine))
                {
                    yield return interieure;
                }
            }
        }
    }
}
