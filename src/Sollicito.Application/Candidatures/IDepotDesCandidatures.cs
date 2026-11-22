using Sollicito.Application.Contacts;
using Sollicito.Application.Relances;
using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Contacts;
using Sollicito.Domain.Entreprises;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// Les candidatures, leurs relances, les entreprises et leurs contacts enregistrés (un port, que l'infrastructure
/// implémente sur la base).
/// </summary>
public interface IDepotDesCandidatures
{
    /// <summary>
    /// Commence une écriture exclusive : jusqu'à sa fin, aucune autre écriture ne touche la base, même d'un autre
    /// processus. Ce qu'elle ajoute ou change n'est gardé que si elle est validée.
    /// </summary>
    Task<IEcritureDesCandidatures> CommencerUneEcritureAsync(CancellationToken annulation);

    /// <summary>Les candidatures, les plus récentes d'abord : par date d'envoi, puis par ordre d'enregistrement.</summary>
    Task<IReadOnlyList<CandidatureListee>> ListerAsync(CancellationToken annulation);

    /// <summary>
    /// Les entreprises que proposent les écrans de saisie et de modification, des candidatures comme des offres, dans
    /// l'ordre de leur clé : celles qu'une candidature ou une offre désigne encore (ADR-0013). Une entreprise que plus
    /// rien ne désigne reste en base, et sa clé la reconnaît toujours.
    /// </summary>
    Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation);

    /// <summary>La candidature de cet identifiant, avec tout son historique et son entreprise, pour la lire ; rien si elle n'existe pas.</summary>
    Task<CandidatureSuivie?> TrouverAsync(int id, CancellationToken annulation);

    /// <summary>Les contacts de l'entreprise de cet identifiant, dans l'ordre de leur ajout ; rien si elle n'existe pas (#14).</summary>
    Task<IReadOnlyList<ContactConnu>?> ListerLesContactsAsync(int entrepriseId, CancellationToken annulation);

    /// <summary>
    /// Les relances à faire dont l'échéance tombe au plus tard ce jour-là, avec leur candidature et leur destinataire,
    /// par échéance, puis par ordre de planification (#14).
    /// </summary>
    Task<IReadOnlyList<RelanceSignalee>> ListerLesRelancesAFaireAsync(DateOnly auPlusTardLe, CancellationToken annulation);

    /// <summary>
    /// Ce que le brouillon de cette relance reprend de la base, si elle est à faire ; rien si elle n'existe pas, ou si
    /// elle est close (#14).
    /// </summary>
    Task<RelancePourLeBrouillon?> LireLaRelancePourLeBrouillonAsync(int relanceId, CancellationToken annulation);
}

/// <summary>Une écriture exclusive dans la base. La quitter sans l'avoir validée l'annule.</summary>
public interface IEcritureDesCandidatures : IAsyncDisposable
{
    /// <summary>L'entreprise de cette clé, si elle est enregistrée : ce qui change en elle, son nom, est gardé à la validation.</summary>
    Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation);

    /// <summary>Vrai si l'entreprise de cet identifiant est enregistrée.</summary>
    Task<bool> EntrepriseExisteAsync(int entrepriseId, CancellationToken annulation);

    /// <summary>Le nombre de candidatures que porte l'entreprise de cet identifiant.</summary>
    Task<int> CompterLesCandidaturesAsync(int entrepriseId, CancellationToken annulation);

    /// <summary>
    /// La candidature de cet identifiant, avec tout son historique, ses relances, son entreprise et les destinataires de
    /// ses relances, pour la faire avancer ou la modifier : ce qui change en elle (statut, événements et relances ajoutés
    /// ou changés, entreprise, poste, date d'envoi) est gardé à la validation. Rien si elle n'existe pas.
    /// </summary>
    Task<CandidatureSuivie?> TrouverLaCandidatureAsync(int id, CancellationToken annulation);

    /// <summary>
    /// Supprime la candidature de cet identifiant, tous ses événements, corrections comprises, et ses relances ; son
    /// entreprise reste. La suppression n'est gardée qu'à la validation. Rend faux si la candidature n'existe pas.
    /// </summary>
    Task<bool> SupprimerLaCandidatureAsync(int id, CancellationToken annulation);

    /// <summary>Le contact de cet identifiant, s'il est enregistré : ce qui change en lui est gardé à la validation.</summary>
    Task<Contact?> TrouverLeContactAsync(int id, CancellationToken annulation);

    /// <summary>
    /// Supprime le contact de cet identifiant ; ses relances, à faire comme closes, restent sans destinataire. La
    /// suppression n'est gardée qu'à la validation. Rend faux s'il n'existe pas.
    /// </summary>
    Task<bool> SupprimerLeContactAsync(int id, CancellationToken annulation);

    /// <summary>Ajoute une entreprise, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Entreprise entreprise, CancellationToken annulation);

    /// <summary>Ajoute une candidature, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Candidature candidature, CancellationToken annulation);

    /// <summary>Ajoute un contact, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Contact contact, CancellationToken annulation);

    /// <summary>
    /// Garde tout ce que l'écriture a ajouté, et ce qui a changé dans les candidatures et les contacts qu'elle a
    /// trouvés. Les événements et les relances ajoutés reçoivent leur identifiant.
    /// </summary>
    Task ValiderAsync(CancellationToken annulation);
}

/// <summary>Une candidature, son historique et ses relances compris, et son entreprise.</summary>
/// <param name="Candidature">La candidature, avec tous ses événements et ses relances, dans l'ordre de leur enregistrement.</param>
/// <param name="Entreprise">Son entreprise.</param>
public sealed record CandidatureSuivie(Candidature Candidature, EntrepriseConnue Entreprise)
{
    /// <summary>Les contacts que ses relances désignent, closes comprises, pour les nommer dans la fiche.</summary>
    public IReadOnlyList<ContactConnu> Destinataires { get; init; } = [];
}
