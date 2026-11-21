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
        using var interruption = InterrompreSurAnnulation(connexion, annulation);
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

    // sqlite3_interrupt se lance d'un autre fil, sans risque : SQLite l'a prévu pour cela, pourvu que la connexion ne se
    // ferme pas avant son retour. L'enregistrement se défait donc avant la connexion, qui se déclare avant lui.
    private static CancellationTokenRegistration InterrompreSurAnnulation(SqliteConnection connexion, CancellationToken annulation) =>
        annulation.Register(static poignee => raw.sqlite3_interrupt((sqlite3)poignee!), connexion.Handle);

    /// <summary>
    /// Vrai si le fichier se relit comme une base SQLite saine : <c>PRAGMA integrity_check</c> rend « ok ». L'annulation
    /// interrompt la relecture en cours, et lève <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<bool> EstSaineAsync(string fichier, CancellationToken annulation)
    {
        try
        {
            await using var connexion = new SqliteConnection(ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
            await connexion.OpenAsync(annulation);
            await using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA integrity_check";
            using var interruption = InterrompreSurAnnulation(connexion, annulation);
            await using var lecture = await commande.ExecuteReaderAsync(annulation);
            var lignes = new List<string>();
            while (await lecture.ReadAsync(annulation))
            {
                lignes.Add(lecture.GetString(0));
            }

            return lignes is ["ok"];
        }
        catch (SqliteException erreur) when (annulation.IsCancellationRequested)
        {
            throw new OperationCanceledException("La vérification de la copie a été interrompue.", erreur, annulation);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Remplace le contenu de la base <paramref name="baseDestination"/> par celui de <paramref name="baseSource"/>, par
    /// l'API de sauvegarde de SQLite : en une transaction de la base, qui garde son mode de journal, et qui ne se valide
    /// qu'une fois la dernière page copiée. La copie avance par tranches de pages ; <paramref name="apresUneTranche"/>,
    /// appelé entre deux tranches, voit donc la transaction ouverte. Une erreur, ou un arrêt avant la fin, défait la
    /// transaction, et la base reste celle d'avant. La base n'est jamais renommée, et ses journaux restent les siens.
    /// L'annulation n'est lue qu'avant la première tranche : un remplacement commencé va au bout.
    /// </summary>
    public static async Task RemplacerAsync(string baseSource, string baseDestination, CancellationToken annulation, Action? apresUneTranche = null)
    {
        await using var source = new SqliteConnection(ChaineDeConnexion(baseSource, SqliteOpenMode.ReadOnly));
        await using var destination = new SqliteConnection(ChaineDeConnexion(baseDestination, SqliteOpenMode.ReadWrite));
        await source.OpenAsync(annulation);
        await destination.OpenAsync(annulation);
        annulation.ThrowIfCancellationRequested();
        var sauvegarde = raw.sqlite3_backup_init(destination.Handle, "main", source.Handle, "main");
        if (sauvegarde is null || sauvegarde.IsInvalid)
        {
            SqliteException.ThrowExceptionForRC(raw.sqlite3_errcode(destination.Handle), destination.Handle);
        }

        int code;
        try
        {
            while ((code = raw.sqlite3_backup_step(sauvegarde, PagesParTranche)) == raw.SQLITE_OK)
            {
                apresUneTranche?.Invoke();
            }
        }
        finally
        {
            // Sans SQLITE_DONE, la transaction de la base se défait ici.
            raw.sqlite3_backup_finish(sauvegarde);
        }

        if (code != raw.SQLITE_DONE)
        {
            SqliteException.ThrowExceptionForRC(code, destination.Handle);
        }
    }

    // Une tranche de 8 pages, soit 32 Kio avec les pages de 4 Kio de la base : même une base neuve, d'une vingtaine de
    // pages, se remplace en plusieurs tranches, et une base de 10 Mo en 320, chacune d'un instant.
    private const int PagesParTranche = 8;

    /// <summary>
    /// Ce que dit <c>PRAGMA quick_check</c> d'une base, lue sans être écrite : saine, ou abîmée. Une base qui ne se lit
    /// plus du tout (page illisible, fichier qui n'est pas une base) est abîmée. L'annulation interrompt la lecture.
    /// </summary>
    public static async Task<bool> SeRelitSaineAsync(string fichier, CancellationToken annulation)
    {
        try
        {
            await using var connexion = new SqliteConnection(ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
            await connexion.OpenAsync(annulation);
            await using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA quick_check";
            using var interruption = InterrompreSurAnnulation(connexion, annulation);
            await using var lecture = await commande.ExecuteReaderAsync(annulation);
            var lignes = new List<string>();
            while (await lecture.ReadAsync(annulation))
            {
                lignes.Add(lecture.GetString(0));
            }

            return lignes is ["ok"];
        }
        catch (SqliteException erreur) when (annulation.IsCancellationRequested)
        {
            throw new OperationCanceledException("La relecture de la base a été interrompue.", erreur, annulation);
        }
        catch (SqliteException erreur) when ((erreur.SqliteErrorCode & 0xFF) is raw.SQLITE_CORRUPT or raw.SQLITE_NOTADB)
        {
            return false;
        }
    }

    /// <summary>
    /// Règle une connexion ouverte sur un fichier qui n'est pas sûr, avant sa première instruction (#28, page « Defense
    /// Against The Dark Arts » de SQLite) : aucun déclencheur ni aucune vue ne s'exécute, le schéma n'appelle que des
    /// fonctions sans effet de bord (<c>trusted_schema</c> coupé), le SQL ne peut pas abîmer le fichier (mode défensif),
    /// les pages se contrôlent à leur lecture (<c>cell_size_check</c>), aucune projection en mémoire, et un texte ou un
    /// blob ne dépasse pas 10 Mo.
    /// </summary>
    public static void ReglerPourUnFichierNonSur(SqliteConnection connexion)
    {
        ArgumentNullException.ThrowIfNull(connexion);
        foreach (var (option, valeur) in new[]
        {
            (raw.SQLITE_DBCONFIG_DEFENSIVE, 1),
            (raw.SQLITE_DBCONFIG_TRUSTED_SCHEMA, 0),
            (raw.SQLITE_DBCONFIG_ENABLE_TRIGGER, 0),
            (raw.SQLITE_DBCONFIG_ENABLE_VIEW, 0),
        })
        {
            var code = raw.sqlite3_db_config(connexion.Handle, option, valeur, out var lue);
            if (code != raw.SQLITE_OK || lue != valeur)
            {
                throw new InvalidOperationException("Le réglage d'une connexion à un fichier non sûr a échoué.");
            }
        }

        raw.sqlite3_limit(connexion.Handle, raw.SQLITE_LIMIT_LENGTH, LongueurMaximaleDUneValeur);
        raw.sqlite3_limit(connexion.Handle, raw.SQLITE_LIMIT_ATTACHED, 0);
        using var commande = connexion.CreateCommand();
        commande.CommandText = "PRAGMA cell_size_check = ON; PRAGMA mmap_size = 0;";
        commande.ExecuteNonQuery();
    }

    // 10 Mo : bien au-delà du plus long texte de Sollicito, bien en deçà de ce qui épuiserait la mémoire.
    private const int LongueurMaximaleDUneValeur = 10_000_000;

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
