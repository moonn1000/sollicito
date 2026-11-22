using System.Globalization;

namespace Sollicito.Domain.Offres;

/// <summary>
/// La date de publication d'une offre (modèle de domaine, « Coller une offre ») : facultative, un jour du calendrier,
/// sans heure ni fuseau. Elle s'écrit <c>AAAA-MM-JJ</c> exactement, comme la rend un champ <c>type=date</c> ; elle
/// n'est ni avant le 1er janvier 2000, ni après aujourd'hui, en date locale du poste : une offre n'est pas publiée
/// demain, et une année mal tapée (2062 pour 2026) se voit.
/// </summary>
public static class DateDePublication
{
    /// <summary>Le premier jour admis.</summary>
    public static readonly DateOnly Plancher = new(2000, 1, 1);

    /// <summary>Le message d'une date illisible.</summary>
    public const string Illisible = "La date de publication s'écrit AAAA-MM-JJ, par exemple 2026-11-18.";

    /// <summary>Le message d'une date après aujourd'hui.</summary>
    public const string Future = "La date de publication ne peut pas être après aujourd'hui.";

    /// <summary>Le message d'une date avant le plancher.</summary>
    public const string TropAncienne = "La date de publication ne peut pas être avant le 1er janvier 2000.";

    /// <summary>
    /// Lit une date saisie. Vide (rien, ou que des blancs), elle ne fait aucune date. Rend faux et le motif du refus
    /// sinon.
    /// </summary>
    /// <param name="saisie">La date telle qu'elle a été saisie.</param>
    /// <param name="aujourdhui">Aujourd'hui, en date locale du poste.</param>
    /// <param name="date">La date lue, ou rien.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(string? saisie, DateOnly aujourdhui, out DateOnly? date, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? erreur)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = null;
            return true;
        }

        if (!DateOnly.TryParseExact(saisie.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lue))
        {
            erreur = Illisible;
            return false;
        }

        if (lue < Plancher)
        {
            erreur = TropAncienne;
            return false;
        }

        if (lue > aujourdhui)
        {
            erreur = Future;
            return false;
        }

        date = lue;
        erreur = null;
        return true;
    }
}
