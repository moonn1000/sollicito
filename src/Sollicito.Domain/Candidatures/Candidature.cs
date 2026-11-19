using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Une candidature envoyée à une entreprise, pour un poste (modèle de domaine, « Statuts d'une candidature »). Elle
/// part toujours du statut Envoyée, avec sa date et son heure d'envoi, gardées en UTC et jamais dans le futur. Elle
/// désigne son entreprise par son identifiant : l'entreprise vit à part.
/// </summary>
public sealed class Candidature
{
    /// <summary>Les mots qui désignent le poste dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuPoste = new("Indiquez le poste.", "Le poste");

    // Pour Entity Framework Core, qui lit une candidature enregistrée.
    private Candidature(int entrepriseId, string poste, DateTime envoyeeLe, StatutDeCandidature statut)
    {
        EntrepriseId = entrepriseId;
        Poste = poste;
        EnvoyeeLe = envoyeeLe;
        Statut = statut;
    }

    /// <summary>L'identifiant technique, donné par la base.</summary>
    public int Id { get; private set; }

    /// <summary>L'identifiant de l'entreprise à qui la candidature est envoyée.</summary>
    public int EntrepriseId { get; private set; }

    /// <summary>Le poste visé.</summary>
    public string Poste { get; private set; }

    /// <summary>L'instant de l'envoi, en UTC.</summary>
    public DateTime EnvoyeeLe { get; private set; }

    /// <summary>Le statut : Envoyée à l'enregistrement.</summary>
    public StatutDeCandidature Statut { get; private set; }

    /// <summary>Lit un poste saisi (<see cref="TexteDUneLigne"/>).</summary>
    public static bool EssayerDeLireLePoste(string? saisie, [NotNullWhen(true)] out string? poste, [NotNullWhen(false)] out string? erreur) =>
        TexteDUneLigne.EssayerDeLire(saisie, LibelleDuPoste, out poste, out erreur);

    /// <summary>
    /// Enregistre une candidature envoyée : elle part au statut <see cref="StatutDeCandidature.Envoyee"/>. Le poste doit
    /// être admis par <see cref="EssayerDeLireLePoste"/>, et l'envoi lu par <see cref="DateDEnvoi.EssayerDeLire"/>.
    /// </summary>
    /// <param name="entrepriseId">L'identifiant de l'entreprise, déjà enregistrée.</param>
    /// <param name="poste">Le poste saisi.</param>
    /// <param name="envoyeeLe">L'instant de l'envoi, en UTC.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    public static Candidature Enregistrer(int entrepriseId, string? poste, DateTime envoyeeLe, DateTime maintenant)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entrepriseId);
        if (!EssayerDeLireLePoste(poste, out var posteLu, out var erreur))
        {
            throw new ArgumentException(erreur, nameof(poste));
        }

        if (envoyeeLe.Kind != DateTimeKind.Utc || maintenant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("L'envoi et l'instant présent se donnent en UTC.", nameof(envoyeeLe));
        }

        if (envoyeeLe > maintenant)
        {
            throw new ArgumentException(DateDEnvoi.DansLeFutur, nameof(envoyeeLe));
        }

        return new Candidature(entrepriseId, posteLu, envoyeeLe, StatutDeCandidature.Envoyee);
    }
}
