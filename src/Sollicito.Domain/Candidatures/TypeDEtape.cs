namespace Sollicito.Domain.Candidatures;

/// <summary>Le type d'une étape de l'historique (modèle de domaine, « Types énumérés »).</summary>
public enum TypeDEtape
{
    /// <summary>Un appel téléphonique.</summary>
    Appel,

    /// <summary>Un entretien. Le noter ne fait pas passer la candidature au statut Entretien.</summary>
    Entretien,

    /// <summary>Un échange écrit : courriel, message.</summary>
    Echange,

    /// <summary>Toute autre étape.</summary>
    Autre,
}
