using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

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

    // Une base qu'une autre connexion occupe se réessaie jusqu'à ce délai, celui que Microsoft.Data.Sqlite donne d'office
    // à une commande. Ses propres essais ne regardent pas l'annulation : ils se font donc par tranches d'une seconde,
    // entre lesquelles elle se voit.
    private static readonly TimeSpan AttenteDUneBaseOccupee = TimeSpan.FromSeconds(30);
    private const int TrancheDAttenteEnSecondes = 1;
    private static readonly TimeSpan EntreDeuxTranches = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Écrit dans <paramref name="destination"/>, qui ne doit pas exister, un instantané de la base : tout ce qui y est
    /// validé, même ce qui n'est encore que dans son journal WAL, et rien d'une écriture en cours. L'annulation arrête la
    /// copie, en pleine écriture (<c>sqlite3_interrupt</c>) comme quand elle attend une base occupée, et lève
    /// <see cref="OperationCanceledException"/> : les appels de SQLite ne regardent pas le jeton d'eux-mêmes.
    /// </summary>
    public static async Task CopierAsync(string baseSource, string destination, CancellationToken annulation)
    {
        await using var connexion = new SqliteConnection(ChaineDeConnexion(baseSource, SqliteOpenMode.ReadWrite));
        await connexion.OpenAsync(annulation);
        await using var commande = connexion.CreateCommand();
        commande.CommandText = CopierVers;
        commande.Parameters.AddWithValue("$destination", destination);
        commande.CommandTimeout = TrancheDAttenteEnSecondes;
        // sqlite3_interrupt se lance d'un autre fil, sans risque : SQLite l'a prévu pour cela.
        using var interruption = annulation.Register(static poignee => raw.sqlite3_interrupt((sqlite3)poignee!), connexion.Handle);
        var debut = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                await commande.ExecuteNonQueryAsync(annulation);
                break;
            }
            catch (SqliteException erreur) when (annulation.IsCancellationRequested)
            {
                throw new OperationCanceledException("La copie de la base a été interrompue.", erreur, annulation);
            }
            catch (SqliteException erreur) when (EstOccupee(erreur) && Stopwatch.GetElapsedTime(debut) < AttenteDUneBaseOccupee)
            {
                await Task.Delay(EntreDeuxTranches, annulation);
            }
        }

        annulation.ThrowIfCancellationRequested();
    }

    // SQLITE_BUSY et SQLITE_LOCKED, quel que soit leur code étendu : ceux que Microsoft.Data.Sqlite réessaie lui-même.
    private static bool EstOccupee(SqliteException erreur) => (erreur.SqliteErrorCode & 0xFF) is raw.SQLITE_BUSY or raw.SQLITE_LOCKED;

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
