using System.Globalization;
using System.Text;

namespace Sollicito.Domain.Entreprises;

/// <summary>
/// La clé d'une entreprise, tirée de son nom (ADR-0005 ; modèle de domaine, « Reconnaître une entreprise ») : deux
/// noms qui donnent la même clé désignent la même entreprise. Elle se calcule dans cet ordre :
/// <list type="number">
/// <item>décomposer les caractères (Unicode NFKD), retirer les accents, écrire « œ » et « æ » en deux lettres ;</item>
/// <item>passer en minuscules, sans dépendre de la langue du poste ;</item>
/// <item>retirer les points, et remplacer par une espace tout autre caractère qui n'est ni une lettre ni un chiffre ;</item>
/// <item>réduire les suites d'espaces à une seule, sans espace au début ni à la fin ;</item>
/// <item>retirer la forme juridique qui termine le nom, tant qu'il y en a une ;</item>
/// <item>retirer ensuite la forme juridique qui ouvre le nom, parmi les seules formes belges qui ne commencent jamais
/// une marque.</item>
/// </list>
/// Les étapes 5 et 6 ne retirent jamais le dernier mot. Un nom sans lettre ni chiffre donne une clé vide.
/// </summary>
public static class CleDEntreprise
{
    /// <summary>Les formes juridiques retirées à la fin du nom (étape 5).</summary>
    public static readonly IReadOnlySet<string> FormesEnFin = new HashSet<string>(StringComparer.Ordinal)
    {
        "sa", "nv", "srl", "bv", "sprl", "bvba", "sc", "cv", "scrl", "cvba", "snc", "vof", "scomm", "commv", "scs",
        "gcv", "sca", "cva", "asbl", "vzw", "aisbl", "ivzw", "se", "sas", "sarl", "gmbh", "ag", "ltd", "inc", "llc",
        "plc",
    };

    /// <summary>Les formes juridiques retirées au début du nom (étape 6) : des formes belges, jamais des initiales.</summary>
    public static readonly IReadOnlySet<string> FormesEnTete = new HashSet<string>(StringComparer.Ordinal)
    {
        "asbl", "vzw", "aisbl", "ivzw", "srl", "sprl", "bvba", "scrl", "cvba",
    };

    /// <summary>La clé du nom.</summary>
    /// <param name="nom">Un nom d'entreprise, en Unicode valide (voir <see cref="Textes.TexteDUneLigne.EstAdmis"/>).</param>
    public static string De(string nom)
    {
        ArgumentNullException.ThrowIfNull(nom);

        // 1. Décomposition NFKD, accents retirés, « œ » et « æ » en deux lettres.
        var sansAccents = new StringBuilder(nom.Length);
        foreach (var rune in nom.Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            var categorie = Rune.GetUnicodeCategory(rune);
            if (categorie is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            sansAccents.Append(rune.Value switch
            {
                'œ' => "oe",
                'Œ' => "OE",
                'æ' => "ae",
                'Æ' => "AE",
                _ => rune.ToString(),
            });
        }

        // 2. Minuscules, en culture invariante.
        var minuscules = sansAccents.ToString().ToLowerInvariant();

        // 3. Points retirés ; tout autre caractère qui n'est ni une lettre ni un chiffre devient une espace.
        var lettresEtChiffres = new StringBuilder(minuscules.Length);
        foreach (var rune in minuscules.EnumerateRunes())
        {
            if (rune.Value == '.')
            {
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                lettresEtChiffres.Append(rune.ToString());
            }
            else
            {
                lettresEtChiffres.Append(' ');
            }
        }

        // 4. Une seule espace entre deux mots, aucune au début ni à la fin.
        var mots = lettresEtChiffres.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // 5. La forme juridique qui termine le nom, tant qu'il y en a une, sans jamais retirer le dernier mot.
        while (mots.Count > 1 && FormesEnFin.Contains(mots[^1]))
        {
            mots.RemoveAt(mots.Count - 1);
        }

        // 6. Puis celle qui l'ouvre, parmi les formes belges qui ne commencent jamais une marque.
        while (mots.Count > 1 && FormesEnTete.Contains(mots[0]))
        {
            mots.RemoveAt(0);
        }

        return string.Join(' ', mots);
    }
}
