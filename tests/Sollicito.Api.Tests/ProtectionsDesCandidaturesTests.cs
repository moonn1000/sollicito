using System.Net;
using System.Text.Json.Nodes;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les routes des candidatures passent par les protections de #10 (ADR-0001, D9 ; ADR-0003) : <c>Host</c> (400),
/// jeton (401), <c>Origin</c> (403), et figurent dans la documentation OpenAPI. Une requête refusée n'enregistre rien.
/// </summary>
public sealed class ProtectionsDesCandidaturesTests
{
    private const string Corps = """{"entreprise":"Lumière Verte SA","poste":"Comptable","envoyeeLe":"2026-11-02T10:00"}""";

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/api/candidatures" },
        { "POST", "/api/candidatures" },
        { "GET", "/api/entreprises" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string chemin)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: false, requete => Preparer(api, methode, requete));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
        {
            Preparer(api, methode, requete);
            requete.Headers.Host = "localhost:" + api.Port;
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task D_une_autre_origine_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
        {
            Preparer(api, methode, requete);
            requete.Headers.Remove("Origin");
            requete.Headers.Add("Origin", "http://exemple.invalid");
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task Un_enregistrement_sans_l_origine_de_l_interface_est_refuse(string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, "/api/candidatures", jeton: true, requete =>
        {
            requete.Content = new StringContent(Corps, System.Text.Encoding.UTF8, "application/json");
            if (origine is not null)
            {
                requete.Headers.Add("Origin", origine);
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Fact]
    public async Task Venues_de_l_interface_les_routes_repondent()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var enregistrement = await api.EnvoyerCorpsAsync(Corps);

        Assert.Equal(HttpStatusCode.Created, enregistrement.StatusCode);
        Assert.Single(await api.ListerAsync());
        Assert.Single(await api.ListerLesEntreprisesAsync());
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!.AsObject();
        var candidatures = chemins["/api/candidatures"]!.AsObject();
        Assert.Equal("ListerLesCandidatures", candidatures["get"]!["operationId"]!.GetValue<string>());
        var enregistrer = candidatures["post"]!.AsObject();
        Assert.Equal("EnregistrerUneCandidature", enregistrer["operationId"]!.GetValue<string>());
        Assert.NotNull(enregistrer["requestBody"]!["content"]!["application/json"]);
        Assert.NotNull(enregistrer["responses"]!["201"]);
        Assert.NotNull(enregistrer["responses"]!["400"]);
        Assert.Equal("ListerLesEntreprises", chemins["/api/entreprises"]!["get"]!["operationId"]!.GetValue<string>());
    }

    // Le corps et l'origine de l'interface pour un enregistrement ; rien pour une lecture.
    private static void Preparer(ApiLancee api, string methode, HttpRequestMessage requete)
    {
        if (methode == "POST")
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Content = new StringContent(Corps, System.Text.Encoding.UTF8, "application/json");
        }
    }
}
