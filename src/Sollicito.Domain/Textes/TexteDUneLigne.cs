using System.Diagnostics.CodeAnalysis;

namespace Sollicito.Domain.Textes;

/// <summary>
/// Les règles communes aux textes d'une ligne que saisit l'utilisateur, comme le nom d'une entreprise ou un poste
/// (modèle de domaine, « Saisie d'une candidature ») : les blancs du début et de la fin sont retirés ; le texte qui
/// reste est requis, de <see cref="LongueurMaximale"/> caractères au plus, sans caractère de contrôle (saut de ligne,
/// tabulation…), sans contrôle de la direction du texte, ni caractère invalide (moitié isolée d'une paire de
/// substitution UTF-16, non-caractère Unicode) : voir <see cref="EstAdmis"/>.
/// </summary>
public static class TexteDUneLigne
{
    /// <summary>Longueur maximale, en unités UTF-16, une fois retirés les blancs du début et de la fin.</summary>
    public const int LongueurMaximale = 200;

    /// <summary>
    /// Lit un texte saisi. Rend vrai et le texte sans ses blancs du début et de la fin, ou faux et le message qui dit
    /// pourquoi il est refusé. Le message ne reprend jamais le texte refusé.
    /// </summary>
    /// <param name="saisie">Le texte tel qu'il a été saisi, ou rien.</param>
    /// <param name="champ">Les mots qui désignent le champ dans les messages.</param>
    /// <param name="texte">Le texte lu, s'il est admis.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(
        string? saisie,
        LibelleDuChamp champ,
        [NotNullWhen(true)] out string? texte,
        [NotNullWhen(false)] out string? erreur)
    {
        ArgumentNullException.ThrowIfNull(champ);
        texte = null;

        var lu = saisie?.Trim() ?? "";
        if (lu.Length == 0)
        {
            erreur = champ.Manquant;
            return false;
        }

        if (lu.Length > LongueurMaximale)
        {
            erreur = $"{champ.Sujet} ne doit pas dépasser {LongueurMaximale} caractères.";
            return false;
        }

        if (!EstAdmis(lu))
        {
            erreur = $"{champ.Sujet} ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide.";
            return false;
        }

        texte = lu;
        erreur = null;
        return true;
    }

    /// <summary>
    /// Vrai si le texte n'a aucun caractère de contrôle, aucun contrôle de la direction du texte (U+202A à U+202E,
    /// U+2066 à U+2069, qui feraient s'afficher un nom à l'envers ou en désordre dans le tableau de bord), aucune
    /// moitié isolée d'une paire de substitution, et aucun non-caractère Unicode (U+FDD0 à U+FDEF, et les deux derniers
    /// points de code de chaque plan, U+FFFE et U+FFFF à U+10FFFE et U+10FFFF). Un texte admis se normalise toujours :
    /// la clé d'une entreprise se calcule (<see cref="Entreprises.CleDEntreprise"/>).
    /// </summary>
    public static bool EstAdmis(string texte) => SansCaractereInterdit(texte, sautsDeLigneEtTabulationsAdmis: false);

    /// <summary>
    /// La règle de <see cref="EstAdmis"/>, qui peut admettre en plus les sauts de ligne (U+000A) et les tabulations
    /// (U+0009), seuls caractères de contrôle d'un texte sur plusieurs lignes (<see cref="TexteSurPlusieursLignes"/>).
    /// </summary>
    internal static bool SansCaractereInterdit(string texte, bool sautsDeLigneEtTabulationsAdmis)
    {
        ArgumentNullException.ThrowIfNull(texte);
        for (var i = 0; i < texte.Length; i++)
        {
            var caractere = texte[i];
            if (sautsDeLigneEtTabulationsAdmis && caractere is '\n' or '\t')
            {
                continue;
            }

            if (char.IsControl(caractere) || caractere is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩'))
            {
                return false;
            }

            int pointDeCode = caractere;
            if (char.IsHighSurrogate(caractere))
            {
                if (i + 1 >= texte.Length || !char.IsLowSurrogate(texte[i + 1]))
                {
                    return false;
                }

                pointDeCode = char.ConvertToUtf32(caractere, texte[i + 1]);
                i++;
            }
            else if (char.IsLowSurrogate(caractere))
            {
                return false;
            }

            if (EstUnNonCaractere(pointDeCode))
            {
                return false;
            }
        }

        return true;
    }

    // Les 66 non-caractères d'Unicode : U+FDD0 à U+FDEF, et U+xFFFE et U+xFFFF de chacun des 17 plans.
    private static bool EstUnNonCaractere(int pointDeCode) =>
        pointDeCode is >= 0xFDD0 and <= 0xFDEF || (pointDeCode & 0xFFFE) == 0xFFFE;
}

/// <summary>Les mots qui désignent un champ dans les messages de refus.</summary>
/// <param name="Manquant">Le message d'un champ vide, par exemple « Indiquez le poste. ».</param>
/// <param name="Sujet">Le sujet des autres messages, par exemple « Le poste ».</param>
public sealed record LibelleDuChamp(string Manquant, string Sujet);
