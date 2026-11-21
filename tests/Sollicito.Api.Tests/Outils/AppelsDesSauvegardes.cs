using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests.Outils;

/// <summary>Les appels de l'interface aux routes des sauvegardes (#26), et ce que les tests lisent des copies sur le disque.</summary>
public static class AppelsDesSauvegardes
{
    private static readonly TimeSpan DelaiDeLaPremiereVerification = TimeSpan.FromSeconds(30);

    /// <summary>Le service des copies de l'API lancée.</summary>
    public static SauvegardesDeLaBase Sauvegardes(this ApiLancee api) => api.Services.GetRequiredService<SauvegardesDeLaBase>();

    /// <summary>Attend la première vérification du lancement, faite en arrière-plan, et rend l'état qui la suit.</summary>
    public static async Task<EtatDesSauvegardes> AttendreLaPremiereVerificationAsync(this ApiLancee api)
    {
        var debut = DateTime.UtcNow;
        while (!api.Sauvegardes().Etat.Verifie)
        {
            Assert.True(DateTime.UtcNow - debut < DelaiDeLaPremiereVerification, "La vérification du lancement ne s'est pas faite.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return api.Sauvegardes().Etat;
    }

    /// <summary>L'état des sauvegardes, par la route qu'en lit l'interface.</summary>
    public static async Task<JsonObject> LireLesSauvegardesAsync(this ApiLancee api)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/sauvegardes", jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>« Sauvegarder maintenant », venu de l'interface.</summary>
    public static async Task<JsonObject> SauvegarderMaintenantAsync(this ApiLancee api)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, "/api/sauvegardes", jeton: true, requete => requete.Headers.Add("Origin", api.Origine));
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>« Revenir au dossier par défaut », venu de l'interface.</summary>
    public static Task<HttpResponseMessage> RevenirAuDossierParDefautAsync(this ApiLancee api) =>
        api.EnvoyerAsync(HttpMethod.Post, "/api/sauvegardes/dossier-par-defaut", jeton: true, requete => requete.Headers.Add("Origin", api.Origine));

    /// <summary>Le nom de la copie d'un jour, pour l'installation de l'API lancée.</summary>
    public static string CopieDuJour(this ApiLancee api, DateOnly jour) => NomsDesCopies.DuJour(api.Sauvegardes().Installation, jour);

    /// <summary>Lance l'API sur ce dossier de données, que le test garde après elle, avec son horloge.</summary>
    public static Task<ApiLancee> LancerSurAsync(string dossierDeDonnees, TimeProvider horloge, Action<IWebHostBuilder>? ajuster = null) =>
        ApiLancee.LancerAsync(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new DossierDeLaBase(dossierDeDonnees));
                services.AddSingleton(horloge);
            });
            ajuster?.Invoke(builder);
        });

    /// <summary>Les noms de fichiers d'un dossier, dans l'ordre alphabétique ; aucun s'il n'existe pas.</summary>
    public static string[] Fichiers(string dossier) =>
        Directory.Exists(dossier) ? [.. Directory.EnumerateFiles(dossier).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)] : [];

    /// <summary>Les postes des candidatures d'une copie, relus dans le fichier.</summary>
    public static string[] PostesDe(string copie) => Lire(copie, "SELECT Poste FROM Candidatures ORDER BY Id");

    /// <summary>Les noms des entreprises d'une copie, relus dans le fichier.</summary>
    public static string[] EntreprisesDe(string copie) => Lire(copie, "SELECT Nom FROM Entreprises ORDER BY Id");

    /// <summary>Vrai si le fichier se relit comme une base SQLite saine.</summary>
    public static bool EstSaine(string fichier) => Lire(fichier, "PRAGMA integrity_check") is ["ok"];

    /// <summary>Le mode de journal d'une base.</summary>
    public static string ModeDuJournal(string fichier) => Assert.Single(Lire(fichier, "PRAGMA journal_mode"));

    /// <summary>Les migrations appliquées à une base, dans l'ordre.</summary>
    public static string[] MigrationsDe(string fichier) => Lire(fichier, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId");

    /// <summary>Une connexion à la base, sans regroupement.</summary>
    public static SqliteConnection Ouvrir(string fichier, SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var connexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fichier, Mode = mode, Pooling = false }.ToString());
        connexion.Open();
        return connexion;
    }

    /// <summary>Exécute une instruction sur une connexion ouverte.</summary>
    public static void Executer(this SqliteConnection connexion, string instruction)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = instruction;
        commande.ExecuteNonQuery();
    }

    /// <summary>Les octets d'un fichier, lus sans gêner un outil du poste (antivirus, indexation) qui le tiendrait ouvert.</summary>
    public static byte[] Octets(string fichier)
    {
        using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memoire = new MemoryStream();
        flux.CopyTo(memoire);
        return memoire.ToArray();
    }

    /// <summary>Le texte UTF-8 d'un fichier, lu comme <see cref="Octets"/>.</summary>
    public static string Texte(string fichier) => System.Text.Encoding.UTF8.GetString(Octets(fichier));

    /// <summary>Efface un fichier, en réessayant le temps qu'un outil du poste (antivirus) le lâche.</summary>
    public static async Task EffacerAsync(string fichier)
    {
        for (var essai = 1; File.Exists(fichier); essai++)
        {
            try
            {
                File.Delete(fichier);
            }
            catch (IOException) when (essai < 20)
            {
                await Task.Delay(100);
            }
        }
    }

    /// <summary>Supprime un dossier de test, en réessayant le temps qu'un outil du poste le lâche.</summary>
    public static async Task SupprimerAsync(string dossier)
    {
        for (var essai = 1; Directory.Exists(dossier); essai++)
        {
            try
            {
                Directory.Delete(dossier, recursive: true);
            }
            catch (IOException) when (essai < 20)
            {
                await Task.Delay(100);
            }
            catch (UnauthorizedAccessException) when (essai < 20)
            {
                await Task.Delay(100);
            }
        }
    }

    private static string[] Lire(string fichier, string requete)
    {
        using var connexion = Ouvrir(fichier, SqliteOpenMode.ReadOnly);
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        using var lecture = commande.ExecuteReader();
        var valeurs = new List<string>();
        while (lecture.Read())
        {
            valeurs.Add(Convert.ToString(lecture.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return [.. valeurs];
    }
}
