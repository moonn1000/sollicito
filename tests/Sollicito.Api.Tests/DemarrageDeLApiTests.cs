using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'API démarre en mémoire. Elle ne sert encore aucune route : la première arrive avec l'API locale.
/// </summary>
public sealed class DemarrageDeLApiTests(WebApplicationFactory<Program> usine) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task L_api_demarre_et_ne_sert_encore_aucune_route()
    {
        using var client = usine.CreateClient();

        using var reponse = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }
}
