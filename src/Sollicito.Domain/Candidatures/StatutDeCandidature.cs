namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Le statut d'une candidature (modèle de domaine, « Statuts d'une candidature »). Envoyée, Entretien et Proposition
/// sont en cours ; les quatre autres closent la candidature. Une candidature part toujours d'Envoyée ; les passages
/// viennent avec #12.
/// </summary>
public enum StatutDeCandidature
{
    /// <summary>La candidature est partie ; on attend une réponse.</summary>
    Envoyee,

    /// <summary>L'entreprise a proposé au moins un entretien.</summary>
    Entretien,

    /// <summary>L'entreprise propose un contrat.</summary>
    Proposition,

    /// <summary>La proposition est acceptée.</summary>
    Acceptee,

    /// <summary>L'entreprise a dit non, ou a retiré sa proposition.</summary>
    Refusee,

    /// <summary>Retrait, ou proposition déclinée.</summary>
    Retiree,

    /// <summary>On n'attend plus de réponse.</summary>
    SansReponse,
}
