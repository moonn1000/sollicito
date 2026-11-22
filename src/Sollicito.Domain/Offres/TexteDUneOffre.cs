using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Offres;

/// <summary>
/// Le texte d'une offre collée (#36, ADR-0013 ; modèle de domaine, « Coller une offre ») : gardé tel que collé, à deux
/// retouches près.
/// <list type="bullet">
/// <item>Ses fins de ligne s'écrivent U+000A. Comptent comme fins de ligne : CRLF, CR, U+000B (tabulation verticale),
/// U+000C (le saut de page d'un texte copié d'un PDF), U+0085, U+2028 et U+2029.</item>
/// <item>Ses blancs du début et de la fin sont retirés.</item>
/// </list>
/// Rien d'autre ne change : ni HTML ni Markdown n'y sont lus, aucune ligne n'est recoupée, rien n'est tronqué. Le texte
/// est requis, de <see cref="LongueurMaximale"/> caractères au plus (unités UTF-16, comme les compte le navigateur).
/// Il refuse, comme un texte saisi (#11), tout autre caractère de contrôle que le saut de ligne et la tabulation, les
/// contrôles de la direction du texte, les non-caractères et les moitiés isolées d'une paire de substitution ; le refus
/// donne la ligne du premier caractère refusé, jamais le texte.
/// </summary>
public static class TexteDUneOffre
{
    /// <summary>Longueur maximale, en unités UTF-16, une fois les fins de ligne et les blancs de bord ramenés.</summary>
    public const int LongueurMaximale = 50_000;

    /// <summary>Le message d'un texte vide.</summary>
    public const string Manquant = "Collez le texte de l'offre.";

    /// <summary>Le message d'un texte trop long.</summary>
    public const string TropLong = "Le texte de l'offre ne doit pas dépasser 50 000 caractères.";

    /// <summary>Le message d'un texte qui porte un caractère refusé, à cette ligne (la première est la ligne 1).</summary>
    public static string CaractereRefuse(int ligne) => string.Create(
        CultureInfo.InvariantCulture,
        $"Le texte de l'offre contient, à la ligne {ligne}, un caractère de contrôle ou invalide : retirez-le, puis réessayez.");

    /// <summary>
    /// Lit un texte collé. Rend vrai et le texte retouché, ou faux et le message qui dit pourquoi il est refusé. Le
    /// message ne reprend jamais le texte.
    /// </summary>
    public static bool EssayerDeLire(string? saisie, [NotNullWhen(true)] out string? texte, [NotNullWhen(false)] out string? erreur)
    {
        texte = null;
        var lignes = AvecDesFinsDeLigneUniques(saisie ?? "");

        // La ligne se compte avant le retrait des blancs de bord : c'est celle du texte tel que l'écran le montre.
        var refuse = TexteDUneLigne.PremierCaractereInterdit(lignes, sautsDeLigneEtTabulationsAdmis: true);
        if (refuse >= 0)
        {
            erreur = CaractereRefuse(lignes.AsSpan(0, refuse).Count('\n') + 1);
            return false;
        }

        var lu = lignes.Trim();
        if (lu.Length == 0)
        {
            erreur = Manquant;
            return false;
        }

        if (lu.Length > LongueurMaximale)
        {
            erreur = TropLong;
            return false;
        }

        texte = lu;
        erreur = null;
        return true;
    }

    /// <summary>Le texte, chaque fin de ligne écrite U+000A : CRLF en compte une seule.</summary>
    internal static string AvecDesFinsDeLigneUniques(string texte)
    {
        var lignes = new StringBuilder(texte.Length);
        for (var i = 0; i < texte.Length; i++)
        {
            var caractere = texte[i];
            if (caractere == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n')
            {
                continue;
            }

            lignes.Append(caractere is '\r' or '\v' or '\f' or '\u0085' or (char)0x2028 or (char)0x2029 ? '\n' : caractere);
        }

        return lignes.ToString();
    }
}
