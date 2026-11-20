using System.Globalization;
using System.Net;
using System.Text;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les deux routes de #25, modifier et supprimer une candidature, passent par les protections de #10 (ADR-0001, D9 ;
/// ADR-0003) : <c>Host</c> (400), jeton (401), <c>Origin</c> (403), et figurent dans la documentation OpenAPI. Une
/// requête refusée ne change rien : la candidature reste, telle quelle.
/// </summary>
public sealed class ProtectionsDeLaModificationTests
{
    private const string Modification = """{"entreprise":"Brasserie Lune Rousse","poste":"Brasseuse","envoyeeLe":"2026-11-01T09:00"}""";

    public static TheoryData<string, string?> Routes => new()
    {
        { "PUT", Modification },
        { "DELETE", null },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), AdresseDeLaFiche(id), jeton: false, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), AdresseDeLaFiche(id), jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [InlineData("PUT", Modification, "http://exemple.invalid")]
    [InlineData("PUT", Modification, "null")]
    [InlineData("PUT", Modification, null)]
    [InlineData("DELETE", null, "http://exemple.invalid")]
    [InlineData("DELETE", null, "null")]
    [InlineData("DELETE", null, null)]
    public async Task Une_ecriture_sans_l_origine_de_l_interface_est_refusee_meme_avec_le_jeton(string methode, string? corps, string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), AdresseDeLaFiche(id), jeton: true, requete =>
        {
            if (corps is not null)
            {
                requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
            }

            if (origine is not null)
            {
                requete.Headers.Add("Origin", origine);
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [InlineData("PUT", Modification, HttpStatusCode.OK)]
    [InlineData("DELETE", null, HttpStatusCode.NoContent)]
    public async Task Venues_de_l_interface_les_routes_repondent(string methode, string? corps, HttpStatusCode attendu)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, _) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), AdresseDeLaFiche(id), jeton: true, requete => Preparer(api, requete, corps));

        Assert.Equal(attendu, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var fiche = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!["/api/candidatures/{id}"]!.AsObject();
        var modification = fiche["put"]!.AsObject();
        Assert.Equal("ModifierUneCandidature", modification["operationId"]!.GetValue<string>());
        Assert.NotNull(modification["requestBody"]!["content"]!["application/json"]);
        foreach (var code in new[] { "200", "400", "404" })
        {
            Assert.NotNull(modification["responses"]![code]);
        }

        var suppression = fiche["delete"]!.AsObject();
        Assert.Equal("SupprimerUneCandidature", suppression["operationId"]!.GetValue<string>());
        Assert.NotNull(suppression["responses"]!["204"]);
        Assert.NotNull(suppression["responses"]!["404"]);
    }

    // Une candidature passée à Entretien : rend son identifiant et sa fiche.
    private static async Task<(int Id, string Fiche)> PreparerAsync(ApiLancee api)
    {
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var fiche = await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        return (id, fiche.ToJsonString());
    }

    // L'origine de l'interface, et le corps s'il y en a un.
    private static void Preparer(ApiLancee api, HttpRequestMessage requete, string? corps)
    {
        requete.Headers.Add("Origin", api.Origine);
        if (corps is not null)
        {
            requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        }
    }
}
