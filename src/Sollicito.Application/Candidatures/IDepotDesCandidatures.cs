using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Entreprises;

namespace Sollicito.Application.Candidatures;

/// <summary>Les candidatures et les entreprises enregistrées (un port, que l'infrastructure implémente sur la base).</summary>
public interface IDepotDesCandidatures
{
    /// <summary>
    /// Commence une écriture exclusive : jusqu'à sa fin, aucune autre écriture ne touche la base, même d'un autre
    /// processus. Ce qu'elle ajoute ou change n'est gardé que si elle est validée.
    /// </summary>
    Task<IEcritureDesCandidatures> CommencerUneEcritureAsync(CancellationToken annulation);

    /// <summary>Les candidatures, les plus récentes d'abord : par date d'envoi, puis par ordre d'enregistrement.</summary>
    Task<IReadOnlyList<CandidatureListee>> ListerAsync(CancellationToken annulation);

    /// <summary>Les entreprises enregistrées, dans l'ordre de leur clé.</summary>
    Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation);

    /// <summary>La candidature de cet identifiant, avec tout son historique et son entreprise, pour la lire ; rien si elle n'existe pas.</summary>
    Task<CandidatureSuivie?> TrouverAsync(int id, CancellationToken annulation);
}

/// <summary>Une écriture exclusive dans la base. La quitter sans l'avoir validée l'annule.</summary>
public interface IEcritureDesCandidatures : IAsyncDisposable
{
    /// <summary>L'entreprise de cette clé, si elle est enregistrée.</summary>
    Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation);

    /// <summary>
    /// La candidature de cet identifiant, avec tout son historique et son entreprise, pour la faire avancer ou la
    /// modifier : ce qui change en elle (statut, événements ajoutés, entreprise, poste, date d'envoi) est gardé à la
    /// validation. Rien si elle n'existe pas.
    /// </summary>
    Task<CandidatureSuivie?> TrouverLaCandidatureAsync(int id, CancellationToken annulation);

    /// <summary>
    /// Supprime la candidature de cet identifiant et tous ses événements, corrections comprises ; son entreprise reste.
    /// La suppression n'est gardée qu'à la validation. Rend faux si la candidature n'existe pas.
    /// </summary>
    Task<bool> SupprimerLaCandidatureAsync(int id, CancellationToken annulation);

    /// <summary>Ajoute une entreprise, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Entreprise entreprise, CancellationToken annulation);

    /// <summary>Ajoute une candidature, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Candidature candidature, CancellationToken annulation);

    /// <summary>
    /// Garde tout ce que l'écriture a ajouté, et ce qui a changé dans les candidatures qu'elle a trouvées. Les
    /// événements ajoutés reçoivent leur identifiant.
    /// </summary>
    Task ValiderAsync(CancellationToken annulation);
}

/// <summary>Une candidature, son historique compris, et son entreprise.</summary>
/// <param name="Candidature">La candidature, avec tous ses événements, dans l'ordre de leur enregistrement.</param>
/// <param name="Entreprise">Son entreprise.</param>
public sealed record CandidatureSuivie(Candidature Candidature, EntrepriseConnue Entreprise);
