namespace Sollicito.Application.IA;

/// <summary>
/// L'essai de la clé (#48, ADR-0017) : une demande fixe, écrite ici, qui ne porte aucune donnée de l'utilisateur. Elle ne
/// part que sur un clic, par le port, comme toute autre demande ; sa réponse prouve la clé, le modèle et le chemin jusqu'au
/// fournisseur. Son texte n'est ni montré ni comparé : il n'est pas fiable.
/// </summary>
public static class EssaiDeLaCle
{
    /// <summary>Ce que l'essai demande à l'IA.</summary>
    public const string Consignes = "Réponds uniquement par le mot OK.";

    /// <summary>La seule donnée de l'essai : une phrase fixe, jamais une donnée de l'utilisateur.</summary>
    public const string Donnees = "Essai de connexion de Sollicito.";

    /// <summary>La plus longue réponse admise.</summary>
    public const int CaracteresDeLaReponse = 20;

    /// <summary>La durée de l'essai.</summary>
    public static readonly TimeSpan Duree = TimeSpan.FromSeconds(20);

    /// <summary>La demande de l'essai.</summary>
    public static DemandeALIA Demande() => new(Consignes, Donnees, CaracteresDeLaReponse, Duree);
}
