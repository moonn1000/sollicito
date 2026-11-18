namespace Sollicito.Api;

/// <summary>Réponse de la route <c>GET /api/etat</c> : l'API est démarrée et répond.</summary>
/// <param name="Statut">« pret » dès que l'API répond.</param>
public sealed record EtatDeLApplication(string Statut)
{
    /// <summary>L'état d'une API démarrée.</summary>
    public static EtatDeLApplication Pret { get; } = new("pret");
}
