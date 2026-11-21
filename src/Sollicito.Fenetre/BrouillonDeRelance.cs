using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Sollicito.Application.Relances;
using Sollicito.Domain.Contacts;

namespace Sollicito.Fenetre;

/// <summary>
/// Le lien <c>mailto</c> qui ouvre le brouillon d'une relance dans la messagerie de l'utilisateur (#14, ADR-0011 ;
/// ADR-0001, D8 ; ADR-0003, « Brouillon de relance »). La fenêtre le construit seule, à partir des champs que l'API a lus
/// en base, selon la RFC 6068 :
/// <list type="bullet">
/// <item>le destinataire, s'il y en a un, revérifié par la règle de l'adresse d'un contact, puis encodé : tout sauf les
/// caractères non réservés et l'<c>@</c> ;</item>
/// <item>l'objet et le corps, en UTF-8 encodé, chaque fin de ligne en <c>%0D%0A</c> ;</item>
/// <item>aucun autre en-tête : ni copie, ni copie cachée, ni pièce jointe.</item>
/// </list>
/// Rien de la page n'y entre : elle ne donne que l'identifiant de la relance.
/// </summary>
internal static class LienDuBrouillon
{
    /// <summary>Le schéma du seul lien que la fenêtre lance pour un brouillon.</summary>
    public const string Schema = "mailto:";

    /// <summary>Le lien du brouillon, ou rien si son destinataire n'a pas la forme d'une adresse de contact.</summary>
    public static string? Construire(BrouillonDeRelance brouillon)
    {
        ArgumentNullException.ThrowIfNull(brouillon);
        var lien = new StringBuilder(Schema);
        if (brouillon.Destinataire is { } adresse)
        {
            if (!AdresseElectronique.EstValide(adresse))
            {
                return null;
            }

            // Une seule @, que la règle garantit : la partie locale et le domaine s'encodent à part, et l'@ reste.
            var arobase = adresse.IndexOf('@', StringComparison.Ordinal);
            lien.Append(Uri.EscapeDataString(adresse[..arobase])).Append('@').Append(Uri.EscapeDataString(adresse[(arobase + 1)..]));
        }

        lien.Append("?subject=").Append(Uri.EscapeDataString(brouillon.Objet));
        lien.Append("&body=").Append(Uri.EscapeDataString(FinsDeLigneDuCourriel(brouillon.Corps)));
        return lien.ToString();
    }

    // Toute fin de ligne devient CR LF, celle des courriels (RFC 6068, section 5).
    private static string FinsDeLigneDuCourriel(string texte) =>
        texte.ReplaceLineEndings("\n").Replace("\n", "\r\n", StringComparison.Ordinal);
}

/// <summary>Ce qui ouvre le brouillon d'une relance : la messagerie par défaut, par le shell de Windows, dans la version publiée.</summary>
internal interface IOuvreurDeBrouillons
{
    /// <summary>Ouvre le lien <c>mailto</c> ; faux si aucune messagerie n'est associée à ces liens sur le poste.</summary>
    bool Ouvrir(string lien);
}

/// <summary>
/// Ouvre un brouillon dans la messagerie par défaut, par le shell de Windows (<c>UseShellExecute</c>), comme un lien
/// externe (ADR-0003). Seul un lien <c>mailto:</c> passe : la fenêtre n'en lance aucun autre par ce chemin.
/// </summary>
internal sealed class OuvreurDeBrouillonsParLeShell : IOuvreurDeBrouillons
{
    public bool Ouvrir(string lien)
    {
        ArgumentNullException.ThrowIfNull(lien);
        if (!lien.StartsWith(LienDuBrouillon.Schema, StringComparison.Ordinal))
        {
            throw new ArgumentException("Seul un lien mailto ouvre un brouillon.", nameof(lien));
        }

        try
        {
            using var messagerie = Process.Start(new ProcessStartInfo(lien) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            // Aucune messagerie associée aux liens mailto : rien ne s'ouvre.
            return false;
        }
    }
}

/// <summary>L'issue de l'ouverture d'un brouillon, telle que la fenêtre la rend à l'interface.</summary>
internal enum IssueDuBrouillon
{
    /// <summary>Le brouillon s'est ouvert dans la messagerie : c'est l'utilisateur qui l'envoie.</summary>
    Ouvert,

    /// <summary>La relance n'existe pas, n'est plus à faire, ou son brouillon ne se construit pas : rien ne s'ouvre.</summary>
    Refuse,

    /// <summary>Aucune messagerie n'est associée aux liens mailto sur le poste : rien ne s'ouvre.</summary>
    Impossible,
}
