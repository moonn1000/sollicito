using System.Net;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Aucune politique CORS n'ouvre l'API à une autre origine (ADR-0001, D9) : aucune réponse n'autorise une
/// autre origine à lire l'API, pas même la réponse à une requête de contrôle préalable.
/// </summary>
public sealed class AucuneOuvertureCorsTests(ApiLancee api) : IClassFixture<ApiLancee>
{
    [Fact]
    public void Aucun_service_CORS_n_est_inscrit()
    {
        Assert.Null(api.Services.GetService<ICorsService>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Une_requete_de_controle_prealable_d_une_autre_origine_n_obtient_aucune_autorisation(bool jeton)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Options, "/api/etat", jeton, requete =>
        {
            requete.Headers.Add("Origin", "http://exemple.invalid");
            requete.Headers.Add("Access-Control-Request-Method", "POST");
            requete.Headers.Add("Access-Control-Request-Headers", "sollicito-jeton, content-type");
        });

        Assert.Equal(jeton ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, reponse.StatusCode);
        AucuneAutorisationCors(reponse);
    }

    [Theory]
    [InlineData("http://exemple.invalid")]
    [InlineData("null")]
    public async Task Une_lecture_d_une_autre_origine_n_obtient_aucune_autorisation(string origine)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.Add("Origin", origine));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        AucuneAutorisationCors(reponse);
    }

    [Fact]
    public async Task Une_reponse_servie_n_ouvre_pas_non_plus_l_api()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.Add("Origin", api.Origine));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        AucuneAutorisationCors(reponse);
    }

    private static void AucuneAutorisationCors(HttpResponseMessage reponse) =>
        Assert.DoesNotContain(
            reponse.Headers.Concat(reponse.Content.Headers),
            enTete => enTete.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
}
