using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les contrôles passent avant le routage (src/Sollicito.Api/ApiLocale.cs) : une requête refusée n'est jamais
/// routée. Le routage d'ASP.NET Core note chaque requête qu'il traite (« Request matched endpoint ») ; aucune
/// requête refusée ne doit y paraître.
/// </summary>
public sealed class OrdreDesControlesTests
{
    private const string CategorieDuRoutage = "Microsoft.AspNetCore.Routing.EndpointRoutingMiddleware";

    [Fact]
    public async Task Une_requete_refusee_n_est_jamais_routee()
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection([new("Logging:LogLevel:Default", "Trace")]))
            .ConfigureLogging(journaux => journaux.AddProvider(capture))))
        {
            using var hote = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.Host = "localhost:" + api.Port);
            using var jeton = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: false);
            using var origine = await api.EnvoyerAsync(HttpMethod.Post, "/api/etat", jeton: true);

            Assert.Equal(
                [HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
                [hote.StatusCode, jeton.StatusCode, origine.StatusCode]);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.AspNetCore.", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(" " + CategorieDuRoutage + " : ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_requete_acceptee_est_routee()
    {
        // Témoin : le routage note bien les requêtes qu'il traite, sans quoi le test précédent ne prouverait rien.
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection([new("Logging:LogLevel:Default", "Trace")]))
            .ConfigureLogging(journaux => journaux.AddProvider(capture))))
        {
            using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true);
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        }

        Assert.Contains(capture.Entrees, entree => entree.Contains(" " + CategorieDuRoutage + " : ", StringComparison.Ordinal));
    }
}
