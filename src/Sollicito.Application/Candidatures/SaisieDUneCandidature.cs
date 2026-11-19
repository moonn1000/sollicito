namespace Sollicito.Application.Candidatures;

/// <summary>
/// Une candidature telle que l'interface l'envoie, champ par champ, sans rien de lu ni de converti : le statut n'en
/// fait pas partie, puisqu'une candidature part toujours d'Envoyée.
/// </summary>
/// <param name="Entreprise">Le nom de l'entreprise, tel que saisi.</param>
/// <param name="Poste">Le poste, tel que saisi.</param>
/// <param name="EnvoyeeLe">
/// La date et l'heure d'envoi telles que l'utilisateur les a tapées, sans décalage : <c>2026-03-27T23:30</c>, les
/// secondes en option.
/// </param>
public sealed record SaisieDUneCandidature(string? Entreprise, string? Poste, string? EnvoyeeLe)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Entreprise = "entreprise";
        public const string Poste = "poste";
        public const string EnvoyeeLe = "envoyeeLe";
    }
}
