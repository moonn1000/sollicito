using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sollicito.Application.Sauvegardes;

/// <summary>
/// Les noms des copies de la base (#26, ADR-0008). Sollicito ne reconnaît, et n'efface, que les fichiers qu'il a nommés
/// ainsi ; tout autre fichier du dossier des sauvegardes reste.
/// <list type="bullet">
/// <item>La copie du jour : <c>sollicito-&lt;installation&gt;-&lt;AAAA-MM-JJ&gt;.db</c>, une par jour d'utilisation et
/// par installation, datée du jour local du poste.</item>
/// <item>La copie d'avant une mise à jour : <c>sollicito-avant-&lt;migration&gt;.db</c>, une par migration cible.</item>
/// <item>La copie d'avant une restauration (#28) : <c>sollicito-avant-restauration-&lt;AAAAMMJJ-HHMMSSmmm&gt;.db</c>,
/// l'instant en UTC ; celle d'une base abîmée, copiée telle quelle, finit par <c>-brute.db</c>, et ne se restaure
/// pas.</item>
/// <item>Un fichier provisoire : le nom final, une marque tirée au hasard, puis <c>.partielle</c>. Il ne prend son nom
/// final qu'une fois la copie vérifiée.</item>
/// </list>
/// </summary>
public static partial class NomsDesCopies
{
    /// <summary>Le début du nom de toute copie.</summary>
    public const string Prefixe = "sollicito-";

    /// <summary>L'extension d'une copie achevée.</summary>
    public const string Extension = ".db";

    /// <summary>La fin du nom d'un fichier provisoire, pas encore vérifié.</summary>
    public const string SuffixeProvisoire = ".partielle";

    private const string MarqueAvantMiseAJour = "avant-";

    /// <summary>
    /// L'identifiant d'une installation : huit caractères hexadécimaux, hachés (SHA-256) à partir du nom du poste et du
    /// dossier de données, propre au compte Windows. Il ne révèle ni l'un ni l'autre, et ne se range nulle part : deux
    /// postes, ou deux comptes, qui partagent un dossier de sauvegardes ne touchent pas aux copies l'un de l'autre.
    /// </summary>
    public static string Installation(string nomDuPoste, string dossierDeDonnees)
    {
        ArgumentNullException.ThrowIfNull(nomDuPoste);
        ArgumentNullException.ThrowIfNull(dossierDeDonnees);

        var empreinte = SHA256.HashData(Encoding.UTF8.GetBytes($"{nomDuPoste}\n{Path.TrimEndingDirectorySeparator(dossierDeDonnees)}"));
        return Convert.ToHexStringLower(empreinte.AsSpan(0, 4));
    }

    /// <summary>Le nom de la copie d'un jour, pour une installation.</summary>
    public static string DuJour(string installation, DateOnly jour)
    {
        if (!FormeDeLInstallation().IsMatch(installation))
        {
            throw new ArgumentException("L'identifiant d'une installation tient en huit caractères hexadécimaux.", nameof(installation));
        }

        return $"{Prefixe}{installation}-{jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{Extension}";
    }

    /// <summary>Le nom de la copie d'avant une mise à jour, pour la migration que vise cette mise à jour.</summary>
    public static string AvantMiseAJour(string migration)
    {
        if (!FormeDUneMigration().IsMatch(migration))
        {
            throw new ArgumentException("Une migration se désigne par son identifiant d'Entity Framework Core.", nameof(migration));
        }

        return $"{Prefixe}{MarqueAvantMiseAJour}{migration}{Extension}";
    }

    /// <summary>Le nom de la copie d'avant une restauration, faite à cet instant (#28).</summary>
    public static string AvantRestauration(DateTimeOffset instant) => $"{Prefixe}{MarqueAvantRestauration}{Horodatage(instant)}{Extension}";

    /// <summary>
    /// Le nom de la copie brute d'avant une restauration : les fichiers d'une base abîmée, copiés tels quels (#28). Elle
    /// ne se restaure pas : <see cref="Lire"/> ne la reconnaît pas.
    /// </summary>
    public static string AvantRestaurationBrute(DateTimeOffset instant) => $"{Prefixe}{MarqueAvantRestauration}{Horodatage(instant)}-brute{Extension}";

    /// <summary>Un nom provisoire pour une copie à venir : il ne désigne aucune copie tant qu'il n'est pas renommé.</summary>
    public static string Provisoire(string nomFinal) =>
        $"{nomFinal}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}{SuffixeProvisoire}";

    /// <summary>La copie que désigne ce nom de fichier, ou <c>null</c> si ce n'est pas un nom de copie.</summary>
    public static CopieNommee? Lire(string nom)
    {
        ArgumentNullException.ThrowIfNull(nom);

        var duJour = FormeDUneCopieDuJour().Match(nom);
        if (duJour.Success
            && DateOnly.TryParseExact(duJour.Groups["jour"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var jour))
        {
            return new CopieNommee.DuJour(nom, duJour.Groups["installation"].Value, jour);
        }

        var restauration = FormeDUneCopieDAvantUneRestauration().Match(nom);
        if (restauration.Success
            && DateTime.TryParseExact(restauration.Groups["instant"].Value, FormatDeLHorodatage, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant))
        {
            return new CopieNommee.AvantRestauration(nom, instant);
        }

        var avant = FormeDUneCopieDAvant().Match(nom);
        return avant.Success ? new CopieNommee.AvantMiseAJour(nom, avant.Groups["migration"].Value) : null;
    }

    private const string MarqueAvantRestauration = "avant-restauration-";

    private const string FormatDeLHorodatage = "yyyyMMdd-HHmmssfff";

    private static string Horodatage(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(FormatDeLHorodatage, CultureInfo.InvariantCulture);

    /// <summary>
    /// Vrai pour un fichier provisoire de Sollicito : le nom final d'une copie, sa marque de huit chiffres hexadécimaux,
    /// puis <c>.partielle</c>. Avec une installation, celui d'une copie du jour de cette installation ; sans, celui d'une
    /// copie d'avant une mise à jour ou une restauration, ou d'une copie brute et de son journal (#28).
    /// </summary>
    public static bool EstUnProvisoireDe(string nom, string? installation)
    {
        ArgumentNullException.ThrowIfNull(nom);
        if (FormeDUnProvisoire().Match(nom) is not { Success: true } provisoire)
        {
            return false;
        }

        var final = provisoire.Groups["final"].Value;
        return installation is null
            ? Lire(final) is CopieNommee.AvantMiseAJour or CopieNommee.AvantRestauration || FormeDUneCopieBrute().IsMatch(final)
            : Lire(final) is CopieNommee.DuJour duJour && duJour.Installation == installation;
    }

    /// <summary>
    /// Vrai pour le journal WAL d'une copie brute (#28) ; <paramref name="copieBrute"/> est alors le nom de la copie brute
    /// elle-même, que le journal suit.
    /// </summary>
    public static bool EstLeJournalDUneCopieBrute(string nom, out string copieBrute)
    {
        ArgumentNullException.ThrowIfNull(nom);
        var forme = FormeDUneCopieBrute().Match(nom);
        copieBrute = forme.Success && forme.Groups["journal"].Success ? forme.Groups["copie"].Value : "";
        return copieBrute.Length > 0;
    }

    [GeneratedRegex(@"^(?<final>.+)\.[0-9a-f]{8}\.partielle\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDUnProvisoire();

    [GeneratedRegex(@"^(?<copie>sollicito-avant-restauration-[0-9]{8}-[0-9]{9}-brute\.db)(?<journal>-wal)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDUneCopieBrute();

    [GeneratedRegex("^[0-9a-f]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDeLInstallation();

    [GeneratedRegex("^[0-9]{14}_[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDUneMigration();

    [GeneratedRegex(@"^sollicito-(?<installation>[0-9a-f]{8})-(?<jour>[0-9]{4}-[0-9]{2}-[0-9]{2})\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDUneCopieDuJour();

    [GeneratedRegex(@"^sollicito-avant-(?<migration>[0-9]{14}_[A-Za-z0-9_]+)\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDUneCopieDAvant();

    [GeneratedRegex(@"^sollicito-avant-restauration-(?<instant>[0-9]{8}-[0-9]{9})\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex FormeDUneCopieDAvantUneRestauration();
}

/// <summary>Une copie, telle que son nom la désigne.</summary>
public abstract record CopieNommee(string Nom)
{
    /// <summary>La copie d'un jour, faite par une installation.</summary>
    public sealed record DuJour(string Nom, string Installation, DateOnly Jour) : CopieNommee(Nom);

    /// <summary>La copie d'avant la mise à jour qui vise cette migration.</summary>
    public sealed record AvantMiseAJour(string Nom, string Migration) : CopieNommee(Nom);

    /// <summary>La copie d'avant une restauration, faite à cet instant, en UTC (#28).</summary>
    public sealed record AvantRestauration(string Nom, DateTime Instant) : CopieNommee(Nom);
}
