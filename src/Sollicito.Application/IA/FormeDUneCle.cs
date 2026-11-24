namespace Sollicito.Application.IA;

/// <summary>
/// La forme d'une clé d'API (#43, ADR-0016) : ses blancs du bord retirés, de <see cref="LongueurMinimale"/> à
/// <see cref="LongueurMaximale"/> caractères ASCII imprimables, sans espace, et le préfixe de son fournisseur. Le préfixe
/// garde « une clé, une adresse » (ADR-0001, D7) : une clé d'OpenAI collée sous Claude partirait chez Anthropic. Un refus
/// ne reprend jamais la saisie, ni sa longueur, ni son début : il ne dit que la règle.
/// </summary>
public static class FormeDUneCle
{
    /// <summary>La plus courte des clés admises.</summary>
    public const int LongueurMinimale = 20;

    /// <summary>La plus longue des clés admises.</summary>
    public const int LongueurMaximale = 512;

    /// <summary>Le début de toute clé d'API de Claude.</summary>
    public const string PrefixeDeClaude = "sk-ant-";

    /// <summary>Le début de toute clé d'API d'OpenAI ; une clé qui commence par <see cref="PrefixeDeClaude"/> n'en est pas une.</summary>
    public const string PrefixeDOpenAI = "sk-";

    /// <summary>
    /// Vérifie la saisie d'une clé pour <paramref name="fournisseur"/>, qui doit en prendre une. Rend <c>null</c> et la clé
    /// admise, ses blancs du bord retirés ; sinon le motif du refus, sans clé.
    /// </summary>
    public static MotifDuRefusDUneCle? Verifier(FournisseurDIA fournisseur, string? saisie, out CleDIA? cle)
    {
        if (!fournisseur.PrendUneCle())
        {
            throw new ArgumentOutOfRangeException(nameof(fournisseur), fournisseur, "Ce fournisseur d'IA ne prend aucune clé.");
        }

        cle = null;
        var texte = saisie?.Trim();
        if (string.IsNullOrEmpty(texte))
        {
            return MotifDuRefusDUneCle.Manquante;
        }

        foreach (var caractere in texte)
        {
            if (caractere is < '!' or > '~')
            {
                return MotifDuRefusDUneCle.CaracteresRefuses;
            }
        }

        if (texte.Length is < LongueurMinimale or > LongueurMaximale)
        {
            return MotifDuRefusDUneCle.LongueurHorsBornes;
        }

        var deClaude = texte.StartsWith(PrefixeDeClaude, StringComparison.Ordinal);
        var motif = fournisseur switch
        {
            FournisseurDIA.Claude when !deClaude => MotifDuRefusDUneCle.SansLePrefixeDeClaude,
            FournisseurDIA.OpenAI when deClaude => MotifDuRefusDUneCle.CleDeClaudeChezOpenAI,
            FournisseurDIA.OpenAI when !texte.StartsWith(PrefixeDOpenAI, StringComparison.Ordinal) => MotifDuRefusDUneCle.SansLePrefixeDOpenAI,
            _ => (MotifDuRefusDUneCle?)null,
        };
        if (motif is null)
        {
            cle = new CleDIA(texte);
        }

        return motif;
    }

    /// <summary>Le message d'un refus, tel que l'écran le montre sous le champ de la clé.</summary>
    public static string Message(this MotifDuRefusDUneCle motif) => motif switch
    {
        MotifDuRefusDUneCle.Manquante => "Collez la clé d'API.",
        MotifDuRefusDUneCle.CaracteresRefuses => "Une clé d'API ne contient ni espace ni caractère accentué.",
        MotifDuRefusDUneCle.LongueurHorsBornes => $"Une clé d'API fait de {LongueurMinimale} à {LongueurMaximale} caractères.",
        MotifDuRefusDUneCle.SansLePrefixeDeClaude => $"Une clé de Claude commence par {PrefixeDeClaude}.",
        MotifDuRefusDUneCle.SansLePrefixeDOpenAI => $"Une clé d'OpenAI commence par {PrefixeDOpenAI}.",
        MotifDuRefusDUneCle.CleDeClaudeChezOpenAI => "Cette clé est une clé de Claude : choisissez Claude, ou collez une clé d'OpenAI.",
        _ => throw new ArgumentOutOfRangeException(nameof(motif), motif, "Motif de refus inconnu."),
    };
}

/// <summary>Pourquoi une clé saisie n'est pas admise.</summary>
public enum MotifDuRefusDUneCle
{
    /// <summary>Rien n'a été saisi, ou seulement des blancs.</summary>
    Manquante,

    /// <summary>Un espace, un caractère accentué, un contrôle : tout ce qui n'est pas de l'ASCII imprimable.</summary>
    CaracteresRefuses,

    /// <summary>Moins de <see cref="FormeDUneCle.LongueurMinimale"/> caractères, ou plus de <see cref="FormeDUneCle.LongueurMaximale"/>.</summary>
    LongueurHorsBornes,

    /// <summary>Une clé saisie pour Claude qui ne commence pas par <see cref="FormeDUneCle.PrefixeDeClaude"/>.</summary>
    SansLePrefixeDeClaude,

    /// <summary>Une clé saisie pour OpenAI qui ne commence pas par <see cref="FormeDUneCle.PrefixeDOpenAI"/>.</summary>
    SansLePrefixeDOpenAI,

    /// <summary>Une clé de Claude saisie pour OpenAI : elle partirait chez un autre fournisseur que le sien.</summary>
    CleDeClaudeChezOpenAI,
}
