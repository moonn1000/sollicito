using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Offres;

/// <summary>
/// L'adresse de l'annonce d'une offre (ADR-0001, D9 ; ADR-0013) : facultative, elle suit la règle des liens que la
/// fenêtre ouvre dans le navigateur par défaut (ADR-0006), en plus stricte.
/// <list type="bullet">
/// <item>Ses blancs du début et de la fin sont retirés ; il reste au plus <see cref="LongueurMaximale"/> caractères,
/// sans blanc ni caractère de contrôle, de direction ou invalide.</item>
/// <item>Elle commence par <c>http://</c> ou <c>https://</c>, et c'est une adresse absolue, avec un hôte.</item>
/// <item>Elle ne porte pas d'identifiants (<c>nom@…</c>, <c>nom:motdepasse@…</c>), forme qui sert à tromper sur la
/// destination.</item>
/// </list>
/// Elle se garde telle que saisie, blancs de bord retirés. Sollicito ne la lit jamais : seul le navigateur par défaut
/// l'ouvre, sur le clic de l'utilisateur. Le message de refus ne reprend jamais l'adresse.
/// </summary>
public static class AdresseDUneAnnonce
{
    /// <summary>Longueur maximale, en unités UTF-16.</summary>
    public const int LongueurMaximale = 2_000;

    /// <summary>Le message d'une adresse qui n'est pas une adresse web complète.</summary>
    public const string Invalide = "L'adresse de l'annonce doit être une adresse web complète, qui commence par http:// ou https://.";

    /// <summary>Le message d'une adresse qui porte des identifiants.</summary>
    public const string AvecIdentifiants = "L'adresse de l'annonce ne doit pas porter de nom d'utilisateur ni de mot de passe.";

    /// <summary>Le message d'une adresse trop longue.</summary>
    public const string TropLongue = "L'adresse de l'annonce ne doit pas dépasser 2 000 caractères.";

    /// <summary>
    /// Lit une adresse saisie. Vide (rien, ou que des blancs), elle ne fait aucune adresse. Rend faux et le motif du
    /// refus sinon.
    /// </summary>
    public static bool EssayerDeLire(string? saisie, out string? adresse, [NotNullWhen(false)] out string? erreur)
    {
        adresse = null;
        var lue = saisie?.Trim() ?? "";
        if (lue.Length == 0)
        {
            erreur = null;
            return true;
        }

        if (lue.Length > LongueurMaximale)
        {
            erreur = TropLongue;
            return false;
        }

        if (!TexteDUneLigne.EstAdmis(lue)
            || lue.Any(char.IsWhiteSpace)
            || !(lue.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || lue.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            || !Uri.TryCreate(lue, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Host.Length == 0)
        {
            erreur = Invalide;
            return false;
        }

        if (uri.UserInfo.Length > 0)
        {
            erreur = AvecIdentifiants;
            return false;
        }

        adresse = lue;
        erreur = null;
        return true;
    }
}
