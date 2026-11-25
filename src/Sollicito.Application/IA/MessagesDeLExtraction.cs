namespace Sollicito.Application.IA;

/// <summary>
/// Ce que l'écran dit d'une extraction (#46, ADR-0018), quand elle n'a rien proposé : jamais le texte de l'offre, ni
/// celui de l'IA, ni le message du fournisseur. Les échecs du port reprennent les textes de #48 (<see cref="MessagesDeLIA"/>),
/// suivis de la saisie à la main, qui reste possible. Ces textes sont publics : les tests et l'écran les comparent mot pour
/// mot.
/// </summary>
public static class MessagesDeLExtraction
{
    /// <summary>Ce qui suit l'échec d'une demande : la saisie à la main reste possible.</summary>
    public const string SaisieALaMain = "Vous pouvez remplir les champs vous-même.";

    /// <summary>Aucune IA n'est prête : aucune n'est choisie, le réglage ne se lit pas, ou le modèle local n'est pas encore branché.</summary>
    public const string AucuneIAPrete =
        "Aucune IA n'est prête : choisissez-en une et enregistrez sa clé dans les réglages, ou remplissez les champs vous-même.";

    /// <summary>Le titre d'un texte refusé avant toute demande ; le motif suit, sous le champ du texte.</summary>
    public const string TitreDuTexteRefuse = "Rien n'a été demandé à l'IA : corrigez le champ signalé.";

    /// <summary>La réponse de <paramref name="fournisseur"/> ne suit pas le schéma : elle est écartée en entier.</summary>
    public static string HorsSchema(FournisseurDIA fournisseur) =>
        $"La réponse {De(fournisseur)} ne correspond pas à ce qu'attend Sollicito : elle a été écartée. Réessayez, ou remplissez les champs vous-même.";

    /// <summary>Le texte d'une demande d'extraction qui a échoué chez <paramref name="fournisseur"/>, un fournisseur en ligne.</summary>
    public static string Echec(SorteDEchec sorte, FournisseurDIA fournisseur) => $"{sorte.Message(fournisseur)} {SaisieALaMain}";

    /// <summary>Le texte d'un refus sans requête : une clé absente ou illisible, ou aucune IA prête.</summary>
    public static string Refus(RaisonDIndisponibilite raison) => raison switch
    {
        RaisonDIndisponibilite.CleAbsente => MessagesDeLIA.SansCle,
        RaisonDIndisponibilite.CleIllisible => MessagesDeLIA.CleIllisible,
        _ => AucuneIAPrete,
    };

    // « de Claude », « d'OpenAI ».
    private static string De(FournisseurDIA fournisseur) => fournisseur switch
    {
        FournisseurDIA.Claude => "de Claude",
        FournisseurDIA.OpenAI => "d'OpenAI",
        _ => throw new ArgumentOutOfRangeException(nameof(fournisseur), fournisseur, "Ce fournisseur d'IA n'est pas en ligne."),
    };
}
