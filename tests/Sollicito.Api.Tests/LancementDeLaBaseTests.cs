using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
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
    // Sans le correctif, le lancement attendrait sans fin : ce délai fait échouer le test au lieu de le bloquer.
    private static readonly TimeSpan DelaiDuLancement = TimeSpan.FromSeconds(30);

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
            Directory.Delete(dossier, recursive: true);
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
            Directory.Delete(dossier, recursive: true);
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
            Directory.Delete(dossier, recursive: true);
        }
    }

    // Le lancement se fait sur un autre fil : sans quoi un lancement qui ne finit pas bloquerait le test lui-même.
    private static Task<ApiLancee> LancerAsync(string dossier) =>
        Task.Run(() => ApiLancee.LancerAsync(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(new DossierDeLaBase(dossier)))))
            .WaitAsync(DelaiDuLancement, TestContext.Current.CancellationToken);

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
