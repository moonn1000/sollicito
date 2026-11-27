using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Application.Candidatures;
using Sollicito.Application.Export;
using Sollicito.Application.Offres;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure;

/// <summary>Les services de l'infrastructure, à enregistrer dans ceux de l'API.</summary>
public static class ServicesDeLInfrastructure
{
    /// <summary>
    /// La base SQLite et les dépôts qui la lisent. Le dossier de la base se lit dans les services au moment d'ouvrir
    /// la base, jamais avant : celui qu'enregistrent les tests, après l'API, est bien celui qui sert.
    /// </summary>
    public static IServiceCollection AjouterLaBase(this IServiceCollection services, DossierDeLaBase dossier)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dossier);

        services.AddSingleton(dossier);
        // Ni données sensibles (valeurs des paramètres) ni erreurs détaillées dans les journaux : ce sont les réglages
        // par défaut d'Entity Framework Core, et rien ici ne les change (ADR-0001, D9, « Secrets et journaux »).
        services.AddDbContext<BaseDeSollicito>((fournisseur, options) =>
            options.UseSqlite(fournisseur.GetRequiredService<DossierDeLaBase>().ChaineDeConnexion()));
        services.AddScoped<IDepotDesCandidatures, DepotDesCandidatures>();
        services.AddScoped<IDepotDesOffres, DepotDesOffres>();
        // L'export (#49) lit la base sur sa propre connexion, en lecture seule, dans une seule transaction.
        services.AddSingleton<ISourceDeLExport, SourceDeLExport>();
        return services;
    }
}
