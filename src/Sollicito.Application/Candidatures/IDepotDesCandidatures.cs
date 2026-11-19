using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Entreprises;

namespace Sollicito.Application.Candidatures;

/// <summary>Les candidatures et les entreprises enregistrées (un port, que l'infrastructure implémente sur la base).</summary>
public interface IDepotDesCandidatures
{
    /// <summary>
    /// Commence une écriture exclusive : jusqu'à sa fin, aucune autre écriture ne touche la base, même d'un autre
    /// processus. Ce qu'elle ajoute n'est gardé que si elle est validée.
    /// </summary>
    Task<IEcritureDesCandidatures> CommencerUneEcritureAsync(CancellationToken annulation);

    /// <summary>Les candidatures, les plus récentes d'abord : par date d'envoi, puis par ordre d'enregistrement.</summary>
    Task<IReadOnlyList<CandidatureListee>> ListerAsync(CancellationToken annulation);

    /// <summary>Les entreprises enregistrées, dans l'ordre de leur clé.</summary>
    Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation);
}

/// <summary>Une écriture exclusive dans la base. La quitter sans l'avoir validée l'annule.</summary>
public interface IEcritureDesCandidatures : IAsyncDisposable
{
    /// <summary>L'entreprise de cette clé, si elle est enregistrée.</summary>
    Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation);

    /// <summary>Ajoute une entreprise, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Entreprise entreprise, CancellationToken annulation);

    /// <summary>Ajoute une candidature, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Candidature candidature, CancellationToken annulation);

    /// <summary>Garde tout ce que l'écriture a ajouté.</summary>
    Task ValiderAsync(CancellationToken annulation);
}
