using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Offres;

/// <summary>Les offres enregistrées, et les entreprises qu'elles désignent (un port, que l'infrastructure implémente sur la base).</summary>
public interface IDepotDesOffres
{
    /// <summary>
    /// Commence une écriture exclusive : jusqu'à sa fin, aucune autre écriture ne touche la base, même d'un autre
    /// processus. Ce qu'elle ajoute ou change n'est gardé que si elle est validée.
    /// </summary>
    Task<IEcritureDesOffres> CommencerUneEcritureAsync(CancellationToken annulation);

    /// <summary>La fiche de l'offre de cet identifiant, doublons possibles et candidature compris ; rien si elle n'existe pas.</summary>
    Task<FicheDUneOffre?> LireAsync(int id, CancellationToken annulation);

    /// <summary>
    /// Toutes les offres, sans leur texte, avec leur candidature (#38) : les plus récemment ajoutées d'abord, puis par
    /// ordre d'enregistrement, la plus récente d'abord.
    /// </summary>
    Task<IReadOnlyList<OffreListee>> ListerAsync(CancellationToken annulation);
}

/// <summary>Une écriture exclusive dans la base. La quitter sans l'avoir validée l'annule.</summary>
public interface IEcritureDesOffres : IAsyncDisposable
{
    /// <summary>L'entreprise de cette clé, si elle est enregistrée.</summary>
    Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation);

    /// <summary>L'offre de cette source et de cette empreinte, si elle est enregistrée (règle 2).</summary>
    Task<OffreDejaEnregistree?> TrouverParEmpreinteAsync(SourceDOffre source, string empreinte, CancellationToken annulation);

    /// <summary>
    /// Les offres de cette entreprise et de ce poste normalisé, quelle que soit leur source (règle 3), sauf celle de cet
    /// identifiant, les plus anciennes d'abord.
    /// </summary>
    Task<IReadOnlyList<OffreSignalee>> ListerLesDoublonsPossiblesAsync(int entrepriseId, string posteNormalise, int? sauf, CancellationToken annulation);

    /// <summary>L'offre de cet identifiant, si elle est enregistrée : ce qui change en elle est gardé à la validation.</summary>
    Task<Offre?> TrouverLOffreAsync(int id, CancellationToken annulation);

    /// <summary>Ajoute une entreprise, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Entreprise entreprise, CancellationToken annulation);

    /// <summary>Ajoute une offre, qui reçoit aussitôt son identifiant.</summary>
    Task AjouterAsync(Offre offre, CancellationToken annulation);

    /// <summary>Garde tout ce que l'écriture a ajouté, et ce qui a changé dans les offres qu'elle a trouvées.</summary>
    Task ValiderAsync(CancellationToken annulation);
}
