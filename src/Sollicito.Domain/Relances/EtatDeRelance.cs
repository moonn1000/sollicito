namespace Sollicito.Domain.Relances;

/// <summary>
/// L'état d'une relance (modèle de domaine, « Relances ») : à faire quand on la planifie, puis faite ou abandonnée,
/// pour de bon.
/// </summary>
public enum EtatDeRelance
{
    /// <summary>Planifiée, pas encore faite : une seule à la fois par candidature.</summary>
    AFaire,

    /// <summary>L'utilisateur a relancé l'entreprise.</summary>
    Faite,

    /// <summary>L'utilisateur y a renoncé, ou la candidature s'est close.</summary>
    Abandonnee,
}
