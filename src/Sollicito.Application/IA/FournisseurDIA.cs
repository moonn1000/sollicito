namespace Sollicito.Application.IA;

/// <summary>
/// Le fournisseur d'IA choisi (#43, ADR-0016) : aucun par défaut, Claude ou OpenAI par la clé de l'utilisateur, ou un
/// modèle local servi par Ollama, sans clé en V1 (ADR-0004, D7). Il s'écrit en toutes lettres, par son nom exact, dans
/// les réglages comme dans l'API : ni nombre, ni autre casse.
/// </summary>
public enum FournisseurDIA
{
    /// <summary>Aucune IA : la valeur par défaut, et tout fonctionne sans elle.</summary>
    Aucun,

    /// <summary>Claude, d'Anthropic, par la clé de l'utilisateur.</summary>
    Claude,

    /// <summary>OpenAI, par la clé de l'utilisateur.</summary>
    OpenAI,

    /// <summary>Un modèle local, servi par Ollama sur le poste, sans clé.</summary>
    Ollama,
}

/// <summary>Ce que l'on sait d'un fournisseur d'IA sans l'appeler.</summary>
public static class FournisseursDIA
{
    /// <summary>Vrai pour un fournisseur en ligne, qui s'appelle par la clé de l'utilisateur : Claude et OpenAI.</summary>
    public static bool PrendUneCle(this FournisseurDIA fournisseur) => fournisseur is FournisseurDIA.Claude or FournisseurDIA.OpenAI;

    /// <summary>Le nom du fournisseur, tel que les réglages et l'API l'écrivent.</summary>
    public static string Nom(this FournisseurDIA fournisseur) => fournisseur switch
    {
        FournisseurDIA.Aucun => "Aucun",
        FournisseurDIA.Claude => "Claude",
        FournisseurDIA.OpenAI => "OpenAI",
        FournisseurDIA.Ollama => "Ollama",
        _ => throw new ArgumentOutOfRangeException(nameof(fournisseur), fournisseur, "Fournisseur d'IA inconnu."),
    };

    /// <summary>
    /// Lit un fournisseur par son nom exact : <c>Aucun</c>, <c>Claude</c>, <c>OpenAI</c> ou <c>Ollama</c>. Une autre
    /// casse, un nombre, un blanc ou tout autre nom ne se lit pas.
    /// </summary>
    public static bool EssayerDeLire(string? nom, out FournisseurDIA fournisseur)
    {
        foreach (var candidat in Enum.GetValues<FournisseurDIA>())
        {
            if (string.Equals(nom, candidat.Nom(), StringComparison.Ordinal))
            {
                fournisseur = candidat;
                return true;
            }
        }

        fournisseur = FournisseurDIA.Aucun;
        return false;
    }
}
