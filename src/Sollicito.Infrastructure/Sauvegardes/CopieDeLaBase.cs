using Microsoft.Data.Sqlite;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// Copier une base SQLite ouverte sans la corrompre (ADR-0008). La base est en mode WAL : Entity Framework Core l'y met à
/// sa création. Une copie de son seul fichier perdrait ce qui dort encore dans son journal, et pourrait prendre une page
/// à moitié écrite. <c>VACUUM INTO</c> écrit au contraire un instantané cohérent, journal compris, dans un fichier
/// autonome. Aucune connexion n'est regroupée : refermée, plus rien ne tient un fichier ouvert.
/// </summary>
public static class CopieDeLaBase
{
    // Le chemin de la copie passe en paramètre, jamais dans le texte de la requête.
    private const string CopierVers = "VACUUM INTO $destination";

    /// <summary>
    /// Écrit dans <paramref name="destination"/>, qui ne doit pas exister, un instantané de la base : tout ce qui y est
    /// validé, même ce qui n'est encore que dans son journal WAL, et rien d'une écriture en cours.
    /// </summary>
    public static async Task CopierAsync(string baseSource, string destination, CancellationToken annulation)
    {
        await using var connexion = new SqliteConnection(ChaineDeConnexion(baseSource, SqliteOpenMode.ReadWrite));
        await connexion.OpenAsync(annulation);
        await using var commande = connexion.CreateCommand();
        commande.CommandText = CopierVers;
        commande.Parameters.AddWithValue("$destination", destination);
        await commande.ExecuteNonQueryAsync(annulation);
    }

    /// <summary>Vrai si le fichier se relit comme une base SQLite saine : <c>PRAGMA integrity_check</c> rend « ok ».</summary>
    public static async Task<bool> EstSaineAsync(string fichier, CancellationToken annulation)
    {
        try
        {
            await using var connexion = new SqliteConnection(ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
            await connexion.OpenAsync(annulation);
            await using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA integrity_check";
            await using var lecture = await commande.ExecuteReaderAsync(annulation);
            var lignes = new List<string>();
            while (await lecture.ReadAsync(annulation))
            {
                lignes.Add(lecture.GetString(0));
            }

            return lignes is ["ok"];
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Remplace le contenu de la base <paramref name="baseDestination"/> par celui de <paramref name="baseSource"/>, par
    /// l'API de sauvegarde de SQLite : en une transaction de la base, qui garde son mode de journal. Une erreur annule
    /// la transaction, et la base reste celle d'avant.
    /// </summary>
    public static async Task RemplacerAsync(string baseSource, string baseDestination, CancellationToken annulation)
    {
        await using var source = new SqliteConnection(ChaineDeConnexion(baseSource, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(ChaineDeConnexion(baseDestination, SqliteOpenMode.ReadWrite));
        await source.OpenAsync(annulation);
        await destination.OpenAsync(annulation);
        source.BackupDatabase(destination);
    }

    /// <summary>Le mode de journal de la base : « wal » pour la base de Sollicito.</summary>
    public static async Task<string> ModeDuJournalAsync(string fichier, CancellationToken annulation)
    {
        await using var connexion = new SqliteConnection(ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
        await connexion.OpenAsync(annulation);
        await using var commande = connexion.CreateCommand();
        commande.CommandText = "PRAGMA journal_mode";
        return (string)(await commande.ExecuteScalarAsync(annulation))!;
    }

    /// <summary>Met la base en mode WAL, celui où Entity Framework Core la crée.</summary>
    public static async Task MettreEnModeWalAsync(string fichier, CancellationToken annulation)
    {
        await using var connexion = new SqliteConnection(ChaineDeConnexion(fichier, SqliteOpenMode.ReadWrite));
        await connexion.OpenAsync(annulation);
        await using var commande = connexion.CreateCommand();
        commande.CommandText = "PRAGMA journal_mode = 'wal'";
        await commande.ExecuteNonQueryAsync(annulation);
    }

    /// <summary>Une connexion sans regroupement, qui ne crée jamais le fichier.</summary>
    public static string ChaineDeConnexion(string fichier, SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = fichier,
        Mode = mode,
        Pooling = false,
    }.ToString();
}
