using Sollicito.Domain.Candidatures;

namespace Sollicito.Application.Candidatures;

/// <summary>Une candidature, telle que le tableau de bord la liste.</summary>
/// <param name="Id">L'identifiant de la candidature.</param>
/// <param name="Entreprise">Son entreprise.</param>
/// <param name="Poste">Le poste visé.</param>
/// <param name="EnvoyeeLe">L'instant de l'envoi, en UTC.</param>
/// <param name="Statut">Son statut.</param>
public sealed record CandidatureListee(int Id, EntrepriseConnue Entreprise, string Poste, DateTime EnvoyeeLe, StatutDeCandidature Statut);

/// <summary>Une entreprise déjà enregistrée, telle que l'écran de saisie la propose.</summary>
/// <param name="Id">L'identifiant de l'entreprise.</param>
/// <param name="Nom">Son nom affiché : celui de la première saisie.</param>
public sealed record EntrepriseConnue(int Id, string Nom);
