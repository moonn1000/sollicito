using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests;

/// <summary>
/// Un lancement finit toujours : par l'API démarrée, ou par une erreur que la fenêtre signale. Ni un verrou des
/// migrations abandonné par un processus arrêté en pleine préparation de la base, ni un second lancement sur le même
/// dossier ne le font attendre sans fin.
/// </summary>
public sealed class LancementDeLaBaseTests
{
    // Sans le correctif, le lancement attendrait sans fin : ce délai fait échouer le test au lieu de le bloquer. 60 s, comme
    // dans MiseAJourDeLaBaseTests : sous la charge de la suite complète, un lancement a dépassé 30 s à la CI (#129).
    private static readonly TimeSpan DelaiDuLancement = TimeSpan.FromSeconds(60);

    // Le temps laissé à un lancement abandonné pour finir, avant qu'il soit libéré (#129).
    private static readonly TimeSpan DelaiDeLaLiberation = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Un_verrou_des_migrations_abandonne_ne_bloque_pas_le_lancement_suivant()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await using (await LancerAsync(dossier))
            {
            }

            using (var connexion = Ouvrir(dossier))
            {
                // EF Core tient bien son verrou dans cette table : il l'a créée au premier lancement.
                Assert.Equal(1L, Lire(connexion, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{VerrouDesMigrations.Table}'"));
                // La ligne qu'y laisse un processus arrêté entre la prise du verrou et sa libération.
                Executer(connexion, $"INSERT INTO \"{VerrouDesMigrations.Table}\" (\"Id\", \"Timestamp\") VALUES (1, '2026-11-19 12:00:00+00:00')");
            }

            await using var relancee = await LancerAsync(dossier);

            using var etat = await relancee.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true);
            Assert.Equal(HttpStatusCode.OK, etat.StatusCode);
            using var connexionApres = Ouvrir(dossier);
            Assert.Equal(0L, Lire(connexionApres, $"SELECT COUNT(*) FROM \"{VerrouDesMigrations.Table}\""));
        }
        finally
        {
            // Avec réessai : un antivirus peut tenir un instant la base ou une copie tout juste écrite (#26).
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    [Fact]
    public async Task Un_second_lancement_sur_le_meme_dossier_est_refuse_aussitot()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await using (var premier = await LancerAsync(dossier))
            {
                var refus = await Assert.ThrowsAnyAsync<Exception>(() => LancerAsync(dossier));

                Assert.Contains(Chaine(refus), erreur => erreur is BaseDejaOuverte);
                // Le premier lancement, lui, répond toujours.
                using var etat = await premier.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true);
                Assert.Equal(HttpStatusCode.OK, etat.StatusCode);
                Assert.True(File.Exists(Path.Combine(dossier, VerrouDeLInstance.NomDuFichier)));
            }

            // Le premier arrêté, son verrou est rendu, et son fichier effacé : un nouveau lancement passe.
            Assert.False(File.Exists(Path.Combine(dossier, VerrouDeLInstance.NomDuFichier)));
            await using var suivant = await LancerAsync(dossier);
            using var etatSuivant = await suivant.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true);
            Assert.Equal(HttpStatusCode.OK, etatSuivant.StatusCode);
        }
        finally
        {
            // Avec réessai : un antivirus peut tenir un instant la base ou une copie tout juste écrite (#26).
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    [Fact]
    public async Task Le_refus_d_un_second_lancement_n_ecrit_pas_le_chemin_du_dossier_dans_le_journal()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        var capture = new CaptureDesJournaux();
        try
        {
            await using (await LancerAsync(dossier))
            {
                // Le second lancement, journaux au niveau le plus bavard : l'hôte y écrit la chaîne des exceptions.
                await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() => ApiLancee.LancerAsync(builder => builder
                    .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection([new("Logging:LogLevel:Default", "Trace")]))
                    .ConfigureLogging(journaux => journaux.AddProvider(capture))
                    .ConfigureTestServices(services => services.AddSingleton(new DossierDeLaBase(dossier)))))
                    .WaitAsync(DelaiDuLancement, TestContext.Current.CancellationToken));
            }

            var entrees = capture.Entrees.ToArray();
            // Témoin : le refus est bien dans la capture.
            Assert.Contains(entrees, entree => entree.Contains("Un autre lancement de Sollicito utilise déjà ce dossier de données.", StringComparison.Ordinal));
            // Le chemin du dossier de données donnerait le nom du compte Windows.
            Assert.DoesNotContain(entrees, entree => entree.Contains(dossier, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            // Avec réessai : un antivirus peut tenir un instant la base ou une copie tout juste écrite (#26).
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    [Fact]
    public async Task Un_verrou_abandonne_sur_une_base_qui_attend_une_migration_ne_bloque_ni_la_copie_d_avant_ni_la_mise_a_jour()
    {
        // Le vrai cas de #79 (#88, point 1) : un lancement arrêté pendant ses migrations laisse la ligne, et une migration attend.
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await BaseDe12.PeuplerAsync(new DossierDeLaBase(dossier));
            using (var connexion = Ouvrir(dossier))
            {
                Executer(connexion, $"INSERT INTO \"{VerrouDesMigrations.Table}\" (\"Id\", \"Timestamp\") VALUES (1, '2026-11-19 12:00:00+00:00')");
            }

            await using (await LancerAsync(dossier))
            {
            }

            using var apres = Ouvrir(dossier);
            Assert.Equal(0L, Lire(apres, $"SELECT COUNT(*) FROM \"{VerrouDesMigrations.Table}\""));
            Assert.Equal(BaseDe12.DerniereMigration, AppelsDesSauvegardes.MigrationsDe(new DossierDeLaBase(dossier).Fichier)[^1]);
            var copieDAvant = Path.Combine(new DossierDeLaBase(dossier).DossierDesSauvegardes, Application.Sauvegardes.NomsDesCopies.AvantMiseAJour(BaseDe12.DerniereMigration));
            Assert.Equal(BaseDe12.Migration, AppelsDesSauvegardes.MigrationsDe(copieDAvant)[^1]);
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    [Fact]
    public async Task Une_table_du_verrou_vide_ne_fait_rien_ecrire_dans_la_base()
    {
        // #88, commentaire 2 : un DELETE sur la table vide écrivait une page dans le journal, avant la copie d'avant.
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await using (await LancerAsync(dossier))
            {
            }

            var fichier = new DossierDeLaBase(dossier).Fichier;
            // Une autre connexion garde la base ouverte : sans elle, la dernière connexion refermée viderait le journal.
            using var temoin = Ouvrir(dossier);
            Assert.Equal(0L, Lire(temoin, $"SELECT COUNT(*) FROM \"{VerrouDesMigrations.Table}\""));
            var journalAvant = new FileInfo(fichier + "-wal").Length;

            var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(new DossierDeLaBase(dossier).ChaineDeConnexion()).Options;
            await using (var baseDeSollicito = new BaseDeSollicito(options))
            {
                Assert.Equal(0, await VerrouDesMigrations.RetirerUneLigneAbandonneeAsync(baseDeSollicito.Database, TestContext.Current.CancellationToken));
            }

            Assert.Equal(journalAvant, new FileInfo(fichier + "-wal").Length);
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    // Le lancement se fait sur un autre fil : sans quoi un lancement qui ne finit pas bloquerait le test lui-même. Au-delà
    // du délai, il continue sur ce fil, et il a déjà pris sollicito.verrou : on lui laisse encore un délai pour finir, on le
    // libère, puis le test échoue en disant le dépassement. Sans cela, le nettoyage du dossier échouerait sur le verrou, et
    // son IOException, levée dans un finally, masquerait la TimeoutException (#129).
    private static async Task<ApiLancee> LancerAsync(string dossier)
    {
        var lancement = Task.Run(() => ApiLancee.LancerAsync(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(new DossierDeLaBase(dossier)))));
        try
        {
            return await lancement.WaitAsync(DelaiDuLancement, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException depassement)
        {
            await LibererAsync(lancement);
            throw new TimeoutException($"Le lancement de l'API a dépassé {DelaiDuLancement.TotalSeconds} s.", depassement);
        }
    }

    // Libère un lancement abandonné, s'il finit dans le délai de la libération. Un lancement qui échoue s'est libéré lui-même
    // (ApiLancee.LancerAsync) ; un lancement qui ne finit toujours pas reste, et le dépassement demeure l'erreur levée.
    private static async Task LibererAsync(Task<ApiLancee> lancement)
    {
        ApiLancee lancee;
        try
        {
            lancee = await lancement.WaitAsync(DelaiDeLaLiberation, CancellationToken.None);
        }
        catch (Exception erreur) when (erreur is TimeoutException or InvalidOperationException or IOException)
        {
            return;
        }

        await lancee.DisposeAsync();
    }

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

    private static SqliteConnection Ouvrir(string dossier)
    {
        var connexion = new SqliteConnection(new DossierDeLaBase(dossier).ChaineDeConnexion());
        connexion.Open();
        return connexion;
    }

    private static long Lire(SqliteConnection connexion, string requete)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        return (long)commande.ExecuteScalar()!;
    }

    private static void Executer(SqliteConnection connexion, string requete)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        commande.ExecuteNonQuery();
    }
}
