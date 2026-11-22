using System.Security.Cryptography;
using System.Text;

namespace Sollicito.Domain.Offres;

/// <summary>
/// L'empreinte d'une offre, la règle 2 du dédoublonnage (ADR-0005, ADR-0013 ; modèle de domaine, « Dédoublonner les
/// offres ») : une offre sans identifiant externe se reconnaît au couple (source, empreinte), unique en base. Elle se
/// calcule sur une ou plusieurs parties : le texte d'une offre collée (#36) en est la seule ; les champs d'une ligne de
/// CSV (#40) en seront plusieurs, dans l'ordre de son format.
/// <list type="number">
/// <item>chaque partie perd ses caractères de contrôle autres que les blancs, U+001F compris ;</item>
/// <item>elle se décompose puis se recompose en Unicode NFKC : la ligature « ﬁ » devient « fi », l'espace insécable une
/// espace ; les accents restent ;</item>
/// <item>elle passe en minuscules, sans dépendre de la langue du poste (culture invariante) ;</item>
/// <item>toute suite de blancs au sens de <see cref="char.IsWhiteSpace(char)"/> (espace, tabulation, saut de ligne,
/// espace fine, séparateur de ligne…) se réduit à une espace, sans blanc au début ni à la fin ;</item>
/// <item>les parties se joignent par U+001F, le séparateur d'unité, qui n'est pas un blanc : une partie vide garde sa
/// place ;</item>
/// <item>le tout, codé en UTF-8, se hache par SHA-256, et s'écrit en 64 chiffres hexadécimaux minuscules.</item>
/// </list>
/// Recoller le même texte retrouve ainsi l'offre enregistrée, même si ses espaces ou ses sauts de ligne ont changé.
/// </summary>
public static class EmpreinteDUneOffre
{
    /// <summary>Le séparateur des parties : U+001F, le séparateur d'unité.</summary>
    public const char Separateur = '\u001F';

    /// <summary>La longueur d'une empreinte : 64 chiffres hexadécimaux.</summary>
    public const int Longueur = 64;

    /// <summary>L'empreinte des parties, dans leur ordre.</summary>
    /// <param name="parties">Au moins une partie, chacune en Unicode valide : sans moitié isolée d'une paire de
    /// substitution ni non-caractère (voir <see cref="Textes.TexteDUneLigne.EstAdmis"/>), que la normalisation
    /// refuserait.</param>
    public static string De(IReadOnlyList<string> parties)
    {
        ArgumentNullException.ThrowIfNull(parties);
        if (parties.Count == 0)
        {
            throw new ArgumentException("Une empreinte se calcule sur une partie au moins.", nameof(parties));
        }

        var jointes = string.Join(Separateur, parties.Select(Normaliser));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jointes)));
    }

    /// <summary>Vrai si le texte a la forme d'une empreinte : 64 chiffres hexadécimaux minuscules.</summary>
    public static bool APourForme(string? empreinte) =>
        empreinte is { Length: Longueur } && empreinte.All(chiffre => chiffre is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    // Les pas 1 à 4, sur une partie.
    private static string Normaliser(string partie)
    {
        ArgumentNullException.ThrowIfNull(partie);
        var sansControle = new StringBuilder(partie.Length);
        foreach (var caractere in partie)
        {
            if (!char.IsControl(caractere) || char.IsWhiteSpace(caractere))
            {
                sansControle.Append(caractere);
            }
        }

        var minuscules = sansControle.ToString().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var normalisee = new StringBuilder(minuscules.Length);
        var blancEnAttente = false;
        foreach (var caractere in minuscules)
        {
            if (char.IsWhiteSpace(caractere))
            {
                blancEnAttente = normalisee.Length > 0;
                continue;
            }

            if (blancEnAttente)
            {
                normalisee.Append(' ');
                blancEnAttente = false;
            }

            normalisee.Append(caractere);
        }

        return normalisee.ToString();
    }
}
