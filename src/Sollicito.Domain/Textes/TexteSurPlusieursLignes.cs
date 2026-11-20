using System.Diagnostics.CodeAnalysis;

namespace Sollicito.Domain.Textes;

/// <summary>
/// Les règles d'un texte libre, facultatif, sur plusieurs lignes, comme la note d'une étape (modèle de domaine,
/// « Noter une étape ») : les fins de ligne s'écrivent U+000A, quelle que soit leur forme d'origine ; les blancs du
/// début et de la fin sont retirés ; un texte vide ne fait aucun texte. Sauts de ligne et tabulations sont admis ;
/// les autres règles sont celles d'un texte d'une ligne (<see cref="TexteDUneLigne.EstAdmis"/>).
/// </summary>
public static class TexteSurPlusieursLignes
{
    /// <summary>
    /// Lit un texte saisi. Rend vrai et le texte, ou rien s'il est vide ; sinon faux, et le message qui dit pourquoi il
    /// est refusé. Le message ne reprend jamais le texte refusé.
    /// </summary>
    /// <param name="saisie">Le texte tel qu'il a été saisi, ou rien.</param>
    /// <param name="sujet">Le sujet des messages, par exemple « La note ».</param>
    /// <param name="longueurMaximale">Longueur maximale, en unités UTF-16, une fois les fins de ligne et les blancs ramenés.</param>
    /// <param name="texte">Le texte lu, ou rien s'il est vide.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(
        string? saisie,
        string sujet,
        int longueurMaximale,
        out string? texte,
        [NotNullWhen(false)] out string? erreur)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sujet);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(longueurMaximale);
        texte = null;

        var lu = (saisie ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (lu.Length > longueurMaximale)
        {
            erreur = $"{sujet} ne doit pas dépasser {longueurMaximale} caractères.";
            return false;
        }

        if (!TexteDUneLigne.SansCaractereInterdit(lu, sautsDeLigneEtTabulationsAdmis: true))
        {
            erreur = $"{sujet} ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide.";
            return false;
        }

        texte = lu.Length == 0 ? null : lu;
        erreur = null;
        return true;
    }
}
