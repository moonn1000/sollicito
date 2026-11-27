using System.Globalization;

namespace Sollicito.Application.Export;

/// <summary>
/// Ce que le format de l'export fixe (#49, ADR-0020 ; <c>docs/export.md</c>) : son nom et sa version, l'extension et le
/// nom proposé de chaque forme, et l'écriture des instants et des dates. Toute modification du format fait passer à la
/// version suivante.
/// </summary>
public static class FormatDeLExport
{
    /// <summary>Le nom du format JSON, écrit dans le champ <c>format</c> du fichier.</summary>
    public const string NomDuJson = "sollicito-export";

    /// <summary>Le nom du format CSV, écrit dans le fichier <c>export.csv</c> de l'archive.</summary>
    public const string NomDuCsv = "sollicito-export-csv";

    /// <summary>La version du format, la même pour ses deux formes.</summary>
    public const int Version = 1;

    /// <summary>La taille du tampon : l'écriture suit la lecture, et ni une table ni le fichier ne restent en mémoire.</summary>
    public const int Tampon = 64 * 1024;

    /// <summary>Les sortes d'événement que Sollicito écrit, telles que la base les nomme.</summary>
    public const string ChangementDeStatut = nameof(Domain.Candidatures.ChangementDeStatut);

    /// <inheritdoc cref="ChangementDeStatut"/>
    public const string Etape = nameof(Domain.Candidatures.Etape);

    /// <inheritdoc cref="ChangementDeStatut"/>
    public const string ClotureDeRelance = nameof(Domain.Candidatures.ClotureDeRelance);

    /// <summary>L'extension du fichier de cette forme, avec son point : <c>.json</c> ou <c>.zip</c>.</summary>
    public static string Extension(FormatDExport format) => format switch
    {
        FormatDExport.Json => ".json",
        FormatDExport.Csv => ".zip",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Vrai si ce chemin finit par l'extension de cette forme, sans tenir compte de la casse.</summary>
    public static bool AExtensionDe(FormatDExport format, string chemin)
    {
        ArgumentNullException.ThrowIfNull(chemin);
        return string.Equals(Path.GetExtension(chemin), Extension(format), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Le nom que le dialogue propose : <c>sollicito-export-AAAA-MM-JJ</c>, au jour local, et l'extension de la forme.</summary>
    public static string NomPropose(FormatDExport format, DateOnly jour) =>
        "sollicito-export-" + jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Extension(format);

    /// <summary>
    /// Un instant, en ISO 8601, en UTC, suivi de <c>Z</c> : <c>2026-11-02T08:15:00Z</c>, avec sa fraction de seconde
    /// quand elle n'est pas nulle, jusqu'à sept chiffres (<c>2026-11-18T13:31:00.1234567Z</c>).
    /// </summary>
    public static string Instant(DateTime instant)
    {
        if (instant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Un instant de l'export est en UTC.", nameof(instant));
        }

        return instant.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Un jour du calendrier, en ISO 8601 : <c>2026-11-09</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);

    /// <summary>Un identifiant, en chiffres.</summary>
    public static string Nombre(int nombre) => nombre.ToString(CultureInfo.InvariantCulture);
}
