namespace Sollicito.Fenetre;

/// <summary>Les réglages qui distinguent la version de développement de la version publiée.</summary>
internal static class ReglagesDeLaVersion
{
#if DEBUG
    /// <summary>En développement (configuration Debug) : outils de développement, menu contextuel et raccourcis du navigateur.</summary>
    public const bool OutilsDuNavigateur = true;
#else
    /// <summary>
    /// Version publiée, et version de test qui la reprend : outils de développement, menu contextuel par défaut et
    /// raccourcis du navigateur coupés (ADR-0003, « Réglages de la version publiée »).
    /// </summary>
    public const bool OutilsDuNavigateur = false;
#endif
}
