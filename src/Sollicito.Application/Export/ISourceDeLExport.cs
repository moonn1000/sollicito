namespace Sollicito.Application.Export;

/// <summary>
/// La base, telle que l'export la lit (#49, ADR-0020) : un seul instantané, ouvert le temps de l'export, dont chaque
/// table se lit ligne à ligne, dans l'ordre des identifiants.
/// </summary>
public interface ISourceDeLExport
{
    /// <summary>
    /// Ouvre un instantané de la base : toutes ses lectures voient le même état, même si l'utilisateur écrit pendant
    /// l'export, et aucune n'écrit.
    /// </summary>
    Task<IInstantaneDeLaBase> OuvrirAsync(CancellationToken annulation);
}

/// <summary>
/// Un instantané de la base, à refermer après l'export. Ses tables se lisent l'une après l'autre, chacune une fois, sans
/// qu'aucune ne soit tenue en mémoire.
/// </summary>
public interface IInstantaneDeLaBase : IAsyncDisposable
{
    /// <summary>Les entreprises, par identifiant croissant.</summary>
    IAsyncEnumerable<EntrepriseExportee> Entreprises(CancellationToken annulation);

    /// <summary>Les contacts, par identifiant croissant.</summary>
    IAsyncEnumerable<ContactExporte> Contacts(CancellationToken annulation);

    /// <summary>Les offres, par identifiant croissant.</summary>
    IAsyncEnumerable<OffreExportee> Offres(CancellationToken annulation);

    /// <summary>Les candidatures, par identifiant croissant.</summary>
    IAsyncEnumerable<CandidatureExportee> Candidatures(CancellationToken annulation);

    /// <summary>Les relances, par identifiant croissant.</summary>
    IAsyncEnumerable<RelanceExportee> Relances(CancellationToken annulation);

    /// <summary>Les événements des candidatures, toutes sortes mêlées, par identifiant croissant.</summary>
    IAsyncEnumerable<EvenementExporte> Evenements(CancellationToken annulation);
}
