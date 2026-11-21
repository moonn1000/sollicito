using System.Diagnostics.CodeAnalysis;

namespace Sollicito.Domain.Contacts;

/// <summary>
/// L'adresse électronique d'un contact (modèle de domaine, « Contact ») : une seule adresse, de la forme
/// <c>nom@domaine</c>, que le lien <c>mailto</c> de la fenêtre hôte ne peut que viser (ADR-0003, ADR-0011). La règle est
/// celle des navigateurs pour un champ <c>type=email</c> (WHATWG), en plus stricte :
/// <list type="bullet">
/// <item>les blancs du début et de la fin sont retirés ; il reste au plus <see cref="LongueurMaximale"/> caractères,
/// en ASCII seulement, avec une seule <c>@</c> ;</item>
/// <item>la partie locale, de 1 à <see cref="LongueurMaximaleDeLaPartieLocale"/> caractères, ne prend que des
/// lettres, des chiffres et <c>. _ + - ' ! # $ &amp; * / = ^ ` { | } ~</c>, sans point au début, à la fin, ni deux à la
/// suite : ni espace, ni virgule, ni point-virgule (pas de liste), ni <c>?</c> (pas de paramètre), ni <c>%</c>, qui
/// sert à l'encodage du lien ;</item>
/// <item>le domaine a au moins deux parties séparées par des points, chacune de 1 à
/// <see cref="LongueurMaximaleDUnePartieDuDomaine"/> lettres, chiffres ou tirets, sans tiret au bord. Un domaine
/// accentué se saisit sous sa forme <c>xn--</c>.</item>
/// </list>
/// Le message de refus ne reprend jamais l'adresse refusée.
/// </summary>
public static class AdresseElectronique
{
    /// <summary>La longueur maximale d'une adresse, une fois retirés les blancs du début et de la fin.</summary>
    public const int LongueurMaximale = 254;

    /// <summary>La longueur maximale de la partie locale, avant l'<c>@</c>.</summary>
    public const int LongueurMaximaleDeLaPartieLocale = 64;

    /// <summary>La longueur maximale d'une partie du domaine, entre deux points.</summary>
    public const int LongueurMaximaleDUnePartieDuDomaine = 63;

    /// <summary>Le message d'une adresse absente.</summary>
    public const string Manquante = "Indiquez l'adresse électronique.";

    /// <summary>Le message d'une adresse trop longue.</summary>
    public const string TropLongue = "L'adresse électronique ne doit pas dépasser 254 caractères.";

    /// <summary>Le message d'une adresse d'une autre forme.</summary>
    public const string Illisible =
        "L'adresse électronique s'écrit nom@domaine.be : une seule adresse, sans espace ni accent, sans virgule, "
        + "point-virgule, « ? » ni « % ».";

    // Les caractères de la partie locale, en plus des lettres et des chiffres ASCII.
    private const string SignesDeLaPartieLocale = "._+-'!#$&*/=^`{|}~";

    /// <summary>
    /// Lit une adresse saisie. Rend vrai et l'adresse sans ses blancs du début et de la fin, ou faux et le message qui
    /// dit pourquoi elle est refusée, sans la reprendre.
    /// </summary>
    public static bool EssayerDeLire(string? saisie, [NotNullWhen(true)] out string? adresse, [NotNullWhen(false)] out string? erreur)
    {
        adresse = null;
        var lue = saisie?.Trim() ?? "";
        if (lue.Length == 0)
        {
            erreur = Manquante;
            return false;
        }

        if (lue.Length > LongueurMaximale)
        {
            erreur = TropLongue;
            return false;
        }

        if (!EstDeLaForme(lue))
        {
            erreur = Illisible;
            return false;
        }

        adresse = lue;
        erreur = null;
        return true;
    }

    /// <summary>
    /// Vrai si l'adresse, telle quelle, suit la règle : la fenêtre hôte la revérifie avant d'en faire le destinataire
    /// d'un brouillon, même lue en base.
    /// </summary>
    public static bool EstValide(string? adresse) =>
        adresse is not null && adresse.Length <= LongueurMaximale && EstDeLaForme(adresse);

    private static bool EstDeLaForme(string adresse)
    {
        var arobase = adresse.IndexOf('@', StringComparison.Ordinal);
        if (arobase <= 0 || arobase != adresse.LastIndexOf('@'))
        {
            return false;
        }

        return EstUnePartieLocale(adresse.AsSpan(0, arobase)) && EstUnDomaine(adresse.AsSpan(arobase + 1));
    }

    private static bool EstUnePartieLocale(ReadOnlySpan<char> partie)
    {
        if (partie.Length is 0 or > LongueurMaximaleDeLaPartieLocale || partie[0] == '.' || partie[^1] == '.' || partie.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var caractere in partie)
        {
            if (!char.IsAsciiLetterOrDigit(caractere) && !SignesDeLaPartieLocale.Contains(caractere, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EstUnDomaine(ReadOnlySpan<char> domaine)
    {
        var parties = 0;
        foreach (var bornes in domaine.Split('.'))
        {
            var partie = domaine[bornes];
            if (partie.Length is 0 or > LongueurMaximaleDUnePartieDuDomaine || partie[0] == '-' || partie[^1] == '-')
            {
                return false;
            }

            foreach (var caractere in partie)
            {
                if (!char.IsAsciiLetterOrDigit(caractere) && caractere != '-')
                {
                    return false;
                }
            }

            parties++;
        }

        return parties >= 2;
    }
}
