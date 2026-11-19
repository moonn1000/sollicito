using System.Diagnostics.CodeAnalysis;

namespace Sollicito.Domain.Textes;

/// <summary>
/// Les règles communes aux textes d'une ligne que saisit l'utilisateur, comme le nom d'une entreprise ou un poste
/// (modèle de domaine, « Saisie d'une candidature ») : les blancs du début et de la fin sont retirés ; le texte qui
/// reste est requis, de <see cref="LongueurMaximale"/> caractères au plus, sans caractère de contrôle (saut de ligne,
/// tabulation…) ni caractère invalide (moitié isolée d'une paire de substitution UTF-16).
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

    /// <summary>Vrai si le texte n'a aucun caractère de contrôle, et aucune moitié isolée d'une paire de substitution.</summary>
    public static bool EstAdmis(string texte)
    {
        ArgumentNullException.ThrowIfNull(texte);
        for (var i = 0; i < texte.Length; i++)
        {
            var caractere = texte[i];
            if (char.IsControl(caractere))
            {
                return false;
            }

            if (char.IsHighSurrogate(caractere))
            {
                if (i + 1 >= texte.Length || !char.IsLowSurrogate(texte[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(caractere))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Les mots qui désignent un champ dans les messages de refus.</summary>
/// <param name="Manquant">Le message d'un champ vide, par exemple « Indiquez le poste. ».</param>
/// <param name="Sujet">Le sujet des autres messages, par exemple « Le poste ».</param>
public sealed record LibelleDuChamp(string Manquant, string Sujet);
