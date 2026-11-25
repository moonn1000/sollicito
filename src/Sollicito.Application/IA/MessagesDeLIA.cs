namespace Sollicito.Application.IA;

/// <summary>
/// Ce que l'écran dit d'une demande à l'IA (#48, ADR-0017) : un texte par issue et par fournisseur en ligne, jamais le
/// message du fournisseur. L'essai de la clé s'en sert, et l'extraction d'une offre (#46) et le brouillon de relance (#47)
/// le reprendront. Ses textes sont publics : les tests et l'écran les comparent mot pour mot.
/// </summary>
public static class MessagesDeLIA
{
    /// <summary>Une autre demande à l'IA est en cours.</summary>
    public const string UneDemandeEnCours = "Une demande à l'IA est déjà en cours : attendez sa réponse.";

    /// <summary>Le fournisseur réglé prend une clé, et aucune n'est enregistrée.</summary>
    public const string SansCle = "Aucune clé n'est enregistrée : enregistrez d'abord votre clé.";

    /// <summary>La clé enregistrée ne se relit pas sur ce poste.</summary>
    public const string CleIllisible = "La clé enregistrée ne peut pas être relue sur ce poste : saisissez-la de nouveau.";

    /// <summary>Aucun fournisseur en ligne n'est réglé, ou le réglage ne se lit pas.</summary>
    public const string SansFournisseurEnLigne = "Choisissez d'abord Claude ou OpenAI, puis enregistrez votre clé.";

    /// <summary>L'essai a reçu une réponse : la clé, le modèle et le chemin jusqu'au fournisseur fonctionnent.</summary>
    public static string Reussite(FournisseurDIA fournisseur) => $"{Nom(fournisseur)} a répondu : la clé fonctionne.";

    /// <summary>Le texte d'une demande qui a échoué, pour <paramref name="fournisseur"/>, un fournisseur en ligne.</summary>
    public static string Message(this SorteDEchec sorte, FournisseurDIA fournisseur)
    {
        var nom = Nom(fournisseur);
        var chez = Chez(fournisseur);
        return sorte switch
        {
            SorteDEchec.CleRefusee => $"{nom} a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA.",
            SorteDEchec.QuotaAtteint =>
                $"{nom} refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte {chez}. Réessayez plus tard, ou vérifiez votre compte.",
            SorteDEchec.AccesRefuse =>
                $"{nom} refuse l'accès avec cette clé, par exemple depuis ce pays ou pour ce compte. Vérifiez votre compte {chez}.",
            SorteDEchec.ModeleIndisponible =>
                $"{nom} ne sert plus le modèle qu'utilise cette version de Sollicito. Installez la dernière version de Sollicito.",
            SorteDEchec.FournisseurEnPanne => $"{nom} ne répond pas pour l'instant, à cause d'une erreur de son côté. Réessayez plus tard.",
            SorteDEchec.Injoignable => $"{nom} n'a pas pu être joint : vérifiez votre connexion à Internet, puis réessayez.",
            SorteDEchec.DelaiDepasse => $"{nom} n'a pas répondu à temps. Réessayez plus tard.",
            SorteDEchec.ReponseIllisible or SorteDEchec.ReponseTropLongue => $"La réponse {De(fournisseur)} n'a pas pu être lue. Réessayez plus tard.",
            SorteDEchec.RefusDuFournisseur => $"{nom} a refusé la demande. Vérifiez votre compte {chez}.",
            SorteDEchec.UneDemandeEnCours => UneDemandeEnCours,
            _ => throw new ArgumentOutOfRangeException(nameof(sorte), sorte, "Sorte d'échec inconnue."),
        };
    }

    // Le nom du fournisseur en ligne, tel que l'écran l'écrit.
    private static string Nom(FournisseurDIA fournisseur) => fournisseur switch
    {
        FournisseurDIA.Claude => "Claude",
        FournisseurDIA.OpenAI => "OpenAI",
        _ => throw new ArgumentOutOfRangeException(nameof(fournisseur), fournisseur, "Ce fournisseur d'IA n'est pas en ligne."),
    };

    // « de Claude », « d'OpenAI ».
    private static string De(FournisseurDIA fournisseur) => fournisseur == FournisseurDIA.OpenAI ? "d'OpenAI" : "de " + Nom(fournisseur);

    // Le compte de l'utilisateur, chez la société qui sert le fournisseur.
    private static string Chez(FournisseurDIA fournisseur) => fournisseur == FournisseurDIA.OpenAI ? "chez OpenAI" : "chez Anthropic";
}
