using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Sollicito.Application.Export;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// La base lue pour l'export (#49, ADR-0020) : une connexion en lecture seule, et une seule transaction différée. En mode
/// WAL, toutes ses lectures voient le même instantané, pris à la première, même si l'utilisateur écrit pendant l'export ;
/// elle ne prend aucun verrou d'écriture. Chaque table se lit ligne à ligne, colonne par colonne, telle qu'en base, sans
/// passer par le domaine : l'export garde ainsi les colonnes que le domaine ne montre pas.
/// </summary>
public sealed class SourceDeLExport(DossierDeLaBase dossier) : ISourceDeLExport
{
    /// <summary>
    /// Les colonnes lues, table par table, dans l'ordre de la lecture. Un test les compare au schéma que les migrations
    /// construisent : une colonne ajoutée par une migration, et que l'export n'écrirait pas, le fait échouer.
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string[] Colonnes)> ColonnesLues =
    [
        ("Entreprises", ["Id", "Nom", "Cle"]),
        ("Contacts", ["Id", "EntrepriseId", "Nom", "Email"]),
        ("Offres", ["Id", "Source", "Empreinte", "EntrepriseId", "Poste", "PosteNormalise", "Lieu", "PublieeLe", "Adresse", "Texte", "AjouteeLe"]),
        ("Candidatures", ["Id", "EntrepriseId", "OffreId", "Poste", "EnvoyeeLe", "Statut"]),
        ("Relances", ["Id", "CandidatureId", "DestinataireId", "Echeance", "Etat"]),
        ("EvenementsDeCandidature", ["Id", "CandidatureId", "Sorte", "SurvenuLe", "Ancien", "Nouveau", "CorrigeId", "Type", "Note", "EtapeCorrigeeId", "RelanceId"]),
    ];

    public async Task<IInstantaneDeLaBase> OuvrirAsync(CancellationToken annulation)
    {
        var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(dossier.Fichier, SqliteOpenMode.ReadOnly));
        try
        {
            await connexion.OpenAsync(annulation);
            // Différée : la transaction reste une transaction de lecture, et son instantané se prend à la première lecture.
            var transaction = connexion.BeginTransaction(deferred: true);
            return new Instantane(connexion, transaction);
        }
        catch
        {
            await connexion.DisposeAsync();
            throw;
        }
    }

    // Les deux écritures d'un instant qu'Entity Framework Core donne à SQLite : « 2026-11-02 08:15:00 », et avec sa
    // fraction de seconde quand elle n'est pas nulle, « 2026-11-18 13:31:00.1234567 ». Toujours en UTC (modèle de domaine,
    // « Dates et heures »).
    private static readonly string[] EcrituresDUnInstant = ["yyyy'-'MM'-'dd' 'HH':'mm':'ss", "yyyy'-'MM'-'dd' 'HH':'mm':'ss'.'FFFFFFF"];

    private sealed class Instantane(SqliteConnection connexion, SqliteTransaction transaction) : IInstantaneDeLaBase
    {
        public IAsyncEnumerable<EntrepriseExportee> Entreprises(CancellationToken annulation) =>
            LireAsync(0, lecteur => new EntrepriseExportee(Entier(lecteur, 0), lecteur.GetString(1), lecteur.GetString(2)), annulation);

        public IAsyncEnumerable<ContactExporte> Contacts(CancellationToken annulation) =>
            LireAsync(1, lecteur => new ContactExporte(Entier(lecteur, 0), Entier(lecteur, 1), lecteur.GetString(2), lecteur.GetString(3)), annulation);

        public IAsyncEnumerable<OffreExportee> Offres(CancellationToken annulation) =>
            LireAsync(2, lecteur => new OffreExportee(
                Entier(lecteur, 0),
                lecteur.GetString(1),
                lecteur.GetString(2),
                EntierOuRien(lecteur, 3),
                lecteur.GetString(4),
                lecteur.GetString(5),
                TexteOuRien(lecteur, 6),
                DateOuRien(lecteur, 7),
                TexteOuRien(lecteur, 8),
                TexteOuRien(lecteur, 9),
                Instant(lecteur, 10)), annulation);

        public IAsyncEnumerable<CandidatureExportee> Candidatures(CancellationToken annulation) =>
            LireAsync(3, lecteur => new CandidatureExportee(
                Entier(lecteur, 0),
                Entier(lecteur, 1),
                EntierOuRien(lecteur, 2),
                lecteur.GetString(3),
                Instant(lecteur, 4),
                lecteur.GetString(5)), annulation);

        public IAsyncEnumerable<RelanceExportee> Relances(CancellationToken annulation) =>
            LireAsync(4, lecteur => new RelanceExportee(
                Entier(lecteur, 0),
                Entier(lecteur, 1),
                EntierOuRien(lecteur, 2),
                DateOuRien(lecteur, 3) ?? throw new InvalidDataException("Une relance de la base n'a pas d'échéance."),
                lecteur.GetString(4)), annulation);

        public IAsyncEnumerable<EvenementExporte> Evenements(CancellationToken annulation) =>
            LireAsync(5, lecteur => new EvenementExporte(
                Entier(lecteur, 0),
                Entier(lecteur, 1),
                lecteur.GetString(2),
                Instant(lecteur, 3),
                TexteOuRien(lecteur, 4),
                TexteOuRien(lecteur, 5),
                EntierOuRien(lecteur, 6),
                TexteOuRien(lecteur, 7),
                TexteOuRien(lecteur, 8),
                EntierOuRien(lecteur, 9),
                EntierOuRien(lecteur, 10)), annulation);

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connexion.DisposeAsync();
        }

        // Une table, ligne à ligne, dans l'ordre des identifiants : le lecteur de SQLite ne tient que la ligne en cours.
        private async IAsyncEnumerable<T> LireAsync<T>(int table, Func<SqliteDataReader, T> ligne, [EnumeratorCancellation] CancellationToken annulation)
        {
            var (nom, colonnes) = ColonnesLues[table];
            await using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = $"SELECT {string.Join(", ", colonnes.Select(colonne => $"\"{colonne}\""))} FROM \"{nom}\" ORDER BY \"Id\"";
            await using var lecteur = await commande.ExecuteReaderAsync(annulation);
            while (await lecteur.ReadAsync(annulation))
            {
                yield return ligne(lecteur);
            }
        }
    }

    private static int Entier(SqliteDataReader lecteur, int colonne) => checked((int)lecteur.GetInt64(colonne));

    private static int? EntierOuRien(SqliteDataReader lecteur, int colonne) => lecteur.IsDBNull(colonne) ? null : Entier(lecteur, colonne);

    private static string? TexteOuRien(SqliteDataReader lecteur, int colonne) => lecteur.IsDBNull(colonne) ? null : lecteur.GetString(colonne);

    private static DateOnly? DateOuRien(SqliteDataReader lecteur, int colonne)
    {
        if (lecteur.IsDBNull(colonne))
        {
            return null;
        }

        return DateOnly.TryParseExact(lecteur.GetString(colonne), "yyyy'-'MM'-'dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidDataException("Une date de la base ne se lit pas.");
    }

    // La conversion est explicite : un texte qui n'a pas l'une des deux écritures fait échouer l'export, sans que son
    // contenu entre dans le message.
    private static DateTime Instant(SqliteDataReader lecteur, int colonne) =>
        DateTime.TryParseExact(
            lecteur.GetString(colonne),
            EcrituresDUnInstant,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var instant)
            ? instant
            : throw new InvalidDataException("Un instant de la base ne se lit pas.");
}
