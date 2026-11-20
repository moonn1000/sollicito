using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Ce que la base contient, lu sans passer par les routes : les routes ne montrent pas tout, par exemple les entreprises
/// qu'aucune candidature ne porte plus (#25).
/// </summary>
public static class LaBase
{
    /// <summary>Toutes les entreprises de la base, proposées ou non, dans l'ordre de leur nom.</summary>
    public static async Task<string[]> EntreprisesEnBaseAsync(this ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        var noms = await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Entreprises
            .Select(entreprise => entreprise.Nom)
            .ToListAsync(TestContext.Current.CancellationToken);
        return [.. noms.Order(StringComparer.Ordinal)];
    }

    /// <summary>Le nombre d'événements de toutes les candidatures.</summary>
    public static async Task<int> CompterLesEvenementsAsync(this ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().EvenementsDeCandidature.CountAsync(TestContext.Current.CancellationToken);
    }
}
