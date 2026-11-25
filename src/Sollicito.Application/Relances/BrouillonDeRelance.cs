using System.Globalization;
using Sollicito.Application.Contacts;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Relances;

/// <summary>
/// Le brouillon d'une relance (#14, ADR-0011) : les champs dont la fenêtre hôte fait le lien <c>mailto</c> qui l'ouvre
/// dans la messagerie de l'utilisateur. Sollicito n'envoie rien : c'est l'utilisateur qui relit et envoie le brouillon
/// (ADR-0001, D8). Le texte est le minimum : le poste, la date de l'envoi et le nom du contact, ni le nom de
/// l'entreprise ni signature, puisque la V1 n'a pas de profil et que la messagerie ajoute la sienne. Le premier jet de
/// l'IA (#47, ADR-0019) reprend son objet, son salut et sa formule de politesse.
/// </summary>
/// <param name="Destinataire">L'adresse du contact destinataire, ou rien : le brouillon s'ouvre alors sans destinataire.</param>
/// <param name="Objet">L'objet du courriel.</param>
/// <param name="Corps">Son texte, dont les lignes se séparent par U+000A.</param>
public sealed record BrouillonDeRelance(string? Destinataire, string Objet, string Corps)
{
    /// <summary>La formule de politesse qui clôt le brouillon, sans signature : la messagerie ajoute la sienne.</summary>
    public const string FormuleDePolitesse = "Bien cordialement,";

    /// <summary>Le schéma du lien qui ouvre un brouillon.</summary>
    public const string SchemaDuLien = "mailto:";

    private static readonly string[] Mois =
    [
        "janvier", "février", "mars", "avril", "mai", "juin",
        "juillet", "août", "septembre", "octobre", "novembre", "décembre",
    ];

    /// <summary>Le brouillon d'une relance à faire, dont la date d'envoi se lit dans le fuseau du poste.</summary>
    public static BrouillonDeRelance De(RelancePourLeBrouillon relance, TimeZoneInfo fuseau)
    {
        ArgumentNullException.ThrowIfNull(relance);
        var envoi = EnToutesLettres(DelaiDeRelance.JourLocal(relance.EnvoyeeLe, fuseau));
        var corps = $"{Salut(relance.Destinataire?.Nom)}\n\n"
            + $"Je me permets de revenir vers vous au sujet de ma candidature au poste de {relance.Poste}, envoyée le {envoi}. "
            + "Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.\n\n"
            + FormuleDePolitesse;
        return new BrouillonDeRelance(relance.Destinataire?.Email, ObjetPour(relance.Poste), corps);
    }

    /// <summary>L'objet du brouillon d'une relance : « Candidature au poste de {poste} ».</summary>
    public static string ObjetPour(string poste) => $"Candidature au poste de {poste}";

    /// <summary>Le salut qui ouvre le brouillon : « Bonjour {nom}, », ou « Bonjour, » sans destinataire.</summary>
    public static string Salut(string? nomDuContact) => nomDuContact is { } nom ? $"Bonjour {nom}," : "Bonjour,";

    /// <summary>
    /// Une date en toutes lettres, en français, sans dépendre de la langue de Windows : « 1er septembre 2026 »,
    /// « 27 mars 2026 ».
    /// </summary>
    public static string EnToutesLettres(DateOnly jour)
    {
        var quantieme = jour.Day == 1 ? "1er" : jour.Day.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{quantieme} {Mois[jour.Month - 1]} {jour.Year}");
    }

    /// <summary>
    /// La longueur, en caractères, du lien <c>mailto</c> que la fenêtre hôte bâtira pour ce brouillon, avec le même
    /// encodage (RFC 6068) : le destinataire, puis l'objet et le corps encodés en UTF-8, chaque fin de ligne en
    /// <c>%0D%0A</c>. Le texte relu d'un premier jet (#47) se borne par elle ; un test la compare au lien de l'hôte.
    /// </summary>
    public int LongueurDuLien()
    {
        var longueur = SchemaDuLien.Length;
        if (Destinataire is { } adresse)
        {
            var arobase = adresse.IndexOf('@', StringComparison.Ordinal);
            longueur += arobase < 0
                ? Uri.EscapeDataString(adresse).Length
                : Uri.EscapeDataString(adresse[..arobase]).Length + 1 + Uri.EscapeDataString(adresse[(arobase + 1)..]).Length;
        }

        var corps = Corps.ReplaceLineEndings("\n").Replace("\n", "\r\n", StringComparison.Ordinal);
        return longueur + "?subject=".Length + Uri.EscapeDataString(Objet).Length + "&body=".Length + Uri.EscapeDataString(corps).Length;
    }
}

/// <summary>Ce que le brouillon d'une relance à faire reprend de la base.</summary>
/// <param name="Id">L'identifiant de la relance.</param>
/// <param name="Poste">Le poste de sa candidature.</param>
/// <param name="EnvoyeeLe">L'instant de l'envoi de sa candidature, en UTC.</param>
/// <param name="Destinataire">Son destinataire, ou rien.</param>
public sealed record RelancePourLeBrouillon(int Id, string Poste, DateTime EnvoyeeLe, ContactConnu? Destinataire);
