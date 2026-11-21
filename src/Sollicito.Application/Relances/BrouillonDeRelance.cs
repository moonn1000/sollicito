using System.Globalization;
using Sollicito.Application.Contacts;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Relances;

/// <summary>
/// Le brouillon d'une relance (#14, ADR-0011) : les champs dont la fenêtre hôte fait le lien <c>mailto</c> qui l'ouvre
/// dans la messagerie de l'utilisateur. Sollicito n'envoie rien : c'est l'utilisateur qui relit et envoie le brouillon
/// (ADR-0001, D8). Le texte est le minimum : le poste, la date de l'envoi et le nom du contact, ni le nom de
/// l'entreprise ni signature, puisque la V1 n'a pas de profil et que la messagerie ajoute la sienne.
/// </summary>
/// <param name="Destinataire">L'adresse du contact destinataire, ou rien : le brouillon s'ouvre alors sans destinataire.</param>
/// <param name="Objet">L'objet du courriel.</param>
/// <param name="Corps">Son texte, dont les lignes se séparent par U+000A.</param>
public sealed record BrouillonDeRelance(string? Destinataire, string Objet, string Corps)
{
    private static readonly string[] Mois =
    [
        "janvier", "février", "mars", "avril", "mai", "juin",
        "juillet", "août", "septembre", "octobre", "novembre", "décembre",
    ];

    /// <summary>Le brouillon d'une relance à faire, dont la date d'envoi se lit dans le fuseau du poste.</summary>
    public static BrouillonDeRelance De(RelancePourLeBrouillon relance, TimeZoneInfo fuseau)
    {
        ArgumentNullException.ThrowIfNull(relance);
        var salut = relance.Destinataire is { } contact ? $"Bonjour {contact.Nom}," : "Bonjour,";
        var envoi = EnToutesLettres(DelaiDeRelance.JourLocal(relance.EnvoyeeLe, fuseau));
        var corps = $"{salut}\n\n"
            + $"Je me permets de revenir vers vous au sujet de ma candidature au poste de {relance.Poste}, envoyée le {envoi}. "
            + "Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.\n\n"
            + "Bien cordialement,";
        return new BrouillonDeRelance(relance.Destinataire?.Email, $"Candidature au poste de {relance.Poste}", corps);
    }

    /// <summary>
    /// Une date en toutes lettres, en français, sans dépendre de la langue de Windows : « 1er septembre 2026 »,
    /// « 27 mars 2026 ».
    /// </summary>
    public static string EnToutesLettres(DateOnly jour)
    {
        var quantieme = jour.Day == 1 ? "1er" : jour.Day.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{quantieme} {Mois[jour.Month - 1]} {jour.Year}");
    }
}

/// <summary>Ce que le brouillon d'une relance à faire reprend de la base.</summary>
/// <param name="Id">L'identifiant de la relance.</param>
/// <param name="Poste">Le poste de sa candidature.</param>
/// <param name="EnvoyeeLe">L'instant de l'envoi de sa candidature, en UTC.</param>
/// <param name="Destinataire">Son destinataire, ou rien.</param>
public sealed record RelancePourLeBrouillon(int Id, string Poste, DateTime EnvoyeeLe, ContactConnu? Destinataire);
