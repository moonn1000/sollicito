using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Sollicito.Domain.Offres;

/// <summary>
/// La date de publication d'une offre (modèle de domaine, « Coller une offre ») : facultative, un jour du calendrier,
/// sans heure ni fuseau. Elle s'écrit <c>AAAA-MM-JJ</c> exactement, comme la rend un champ <c>type=date</c> ; une ligne
/// d'un fichier CSV (#40, ADR-0015) l'écrit aussi <c>JJ/MM/AAAA</c>, le jour d'abord, comme l'affiche un tableur en
/// français. Elle n'est ni avant le 1er janvier 2000, ni après aujourd'hui, en date locale du poste : une offre n'est pas
/// publiée demain, et une année mal tapée (2062 pour 2026) se voit.
/// </summary>
public static class DateDePublication
{
    /// <summary>Le premier jour admis.</summary>
    public static readonly DateOnly Plancher = new(2000, 1, 1);

    /// <summary>Le message d'une date illisible.</summary>
    public const string Illisible = "La date de publication s'écrit AAAA-MM-JJ, par exemple 2026-11-18.";

    /// <summary>Le message d'une date illisible là où le jour peut venir d'abord : une ligne d'un fichier CSV (#40).</summary>
    public const string IllisibleJourEnPremier = "La date de publication s'écrit JJ/MM/AAAA ou AAAA-MM-JJ, par exemple 18/11/2026.";

    /// <summary>Le message d'une date après aujourd'hui.</summary>
    public const string Future = "La date de publication ne peut pas être après aujourd'hui.";

    /// <summary>Le message d'une date avant le plancher.</summary>
    public const string TropAncienne = "La date de publication ne peut pas être avant le 1er janvier 2000.";

    /// <summary>
    /// Lit une date saisie, <c>AAAA-MM-JJ</c> exactement. Vide (rien, ou que des blancs), elle ne fait aucune date. Rend
    /// faux et le motif du refus sinon.
    /// </summary>
    /// <param name="saisie">La date telle qu'elle a été saisie.</param>
    /// <param name="aujourdhui">Aujourd'hui, en date locale du poste.</param>
    /// <param name="date">La date lue, ou rien.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(string? saisie, DateOnly aujourdhui, out DateOnly? date, [NotNullWhen(false)] out string? erreur) =>
        EssayerDeLire(saisie, aujourdhui, jourEnPremierAdmis: false, out date, out erreur);

    /// <summary>
    /// Lit une date saisie : <c>AAAA-MM-JJ</c> exactement ou, si <paramref name="jourEnPremierAdmis"/>, <c>J/M/AAAA</c>,
    /// le jour puis le mois sur un ou deux chiffres chacun, l'année sur quatre. Le mois ne vient jamais en premier.
    /// Vide (rien, ou que des blancs), elle ne fait aucune date. Rend faux et le motif du refus sinon.
    /// </summary>
    /// <param name="saisie">La date telle qu'elle a été saisie.</param>
    /// <param name="aujourdhui">Aujourd'hui, en date locale du poste.</param>
    /// <param name="jourEnPremierAdmis">Vrai pour une ligne d'un fichier CSV (#40), qui admet aussi <c>JJ/MM/AAAA</c>.</param>
    /// <param name="date">La date lue, ou rien.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(string? saisie, DateOnly aujourdhui, bool jourEnPremierAdmis, out DateOnly? date, [NotNullWhen(false)] out string? erreur)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = null;
            return true;
        }

        var texte = saisie.Trim();
        DateOnly lue;
        if (!DateOnly.TryParseExact(texte, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out lue)
            && !(jourEnPremierAdmis && EssayerDeLireLeJourEnPremier(texte, out lue)))
        {
            erreur = jourEnPremierAdmis ? IllisibleJourEnPremier : Illisible;
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

    /// <summary>La forme qui entre dans l'empreinte d'une offre (ADR-0015) : <c>AAAA-MM-JJ</c>, quelle que soit la forme saisie.</summary>
    public static string FormeCanonique(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // J/M/AAAA : chiffres ASCII seulement, un jour qui existe dans ce mois de cette année.
    private static bool EssayerDeLireLeJourEnPremier(string texte, out DateOnly date)
    {
        date = default;
        var parties = texte.Split('/');
        if (parties.Length != 3 || !EstUnNombre(parties[0], 1, 2) || !EstUnNombre(parties[1], 1, 2) || !EstUnNombre(parties[2], 4, 4))
        {
            return false;
        }

        var jour = int.Parse(parties[0], NumberStyles.None, CultureInfo.InvariantCulture);
        var mois = int.Parse(parties[1], NumberStyles.None, CultureInfo.InvariantCulture);
        var annee = int.Parse(parties[2], NumberStyles.None, CultureInfo.InvariantCulture);
        if (annee < 1 || mois is < 1 or > 12 || jour < 1 || jour > DateTime.DaysInMonth(annee, mois))
        {
            return false;
        }

        date = new DateOnly(annee, mois, jour);
        return true;
    }

    private static bool EstUnNombre(string texte, int auMoins, int auPlus) =>
        texte.Length >= auMoins && texte.Length <= auPlus && texte.All(chiffre => chiffre is >= '0' and <= '9');
}
