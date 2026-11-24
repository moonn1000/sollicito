using System.Text;

namespace Sollicito.Domain.Textes;

/// <summary>
/// Les fins de ligne d'un texte sur plusieurs lignes, comme la note d'une étape (modèle de domaine, « Noter une étape »,
/// et « Coller une offre » pour le texte d'une offre) : chacune s'écrit U+000A, quelle que soit sa forme d'origine.
/// Comptent comme fins de ligne : CRLF, CR, U+000B (tabulation verticale), U+000C (le saut de page d'un texte copié
/// d'un PDF), U+0085, U+2028 et U+2029.
/// </summary>
internal static class FinsDeLigne
{
    /// <summary>Le texte, chaque fin de ligne écrite U+000A : CRLF en compte une seule.</summary>
    public static string Uniques(string texte)
    {
        ArgumentNullException.ThrowIfNull(texte);

        var lignes = new StringBuilder(texte.Length);
        for (var i = 0; i < texte.Length; i++)
        {
            var caractere = texte[i];
            if (caractere == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n')
            {
                continue;
            }

            lignes.Append(caractere is '\r' or '\v' or '\f' or (char)0x0085 or (char)0x2028 or (char)0x2029 ? '\n' : caractere);
        }

        return lignes.ToString();
    }
}
