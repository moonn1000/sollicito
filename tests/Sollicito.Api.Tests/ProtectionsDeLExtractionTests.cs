using System.Globalization;
using System.Net;
using System.Text;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDeLExtraction;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;

namespace Sollicito.Api.Tests;

/// <summary>
/// La route de l'extraction (#46) passe par les protections de #10 (ADR-0001, D9 ; ADR-0003) : <c>Host</c> (400), jeton
/// (401), <c>Origin</c> (403), et figure dans la documentation OpenAPI. Une requête refusée ne demande rien à l'IA. Elle
/// ne touche pas la base : la porte d'une restauration ne la garde pas (voir <see cref="ExtractionDUneOffreTests"/>).
/// </summary>
public sealed class ProtectionsDeLExtractionTests
{
    private const string Corps = """{"texte":"Lumière Verte SA recrute un comptable à Liège."}""";

    [Fact]
    public async Task Sans_jeton_l_extraction_est_refusee_et_rien_ne_part()
    {
        await using var api = await PreparerAsync();

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, AdresseDeLExtraction, jeton: false, requete => Preparer(api, requete, api.Origine));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Sous_un_autre_Host_l_extraction_est_refusee_meme_avec_le_jeton()
    {
        await using var api = await PreparerAsync();

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, AdresseDeLExtraction, jeton: true, requete =>
        {
            Preparer(api, requete, api.Origine);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Theory]
    [InlineData("http://exemple.invalid")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task Sans_l_origine_de_l_interface_l_extraction_est_refusee_meme_avec_le_jeton(string? origine)
    {
        await using var api = await PreparerAsync();

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, AdresseDeLExtraction, jeton: true, requete => Preparer(api, requete, origine));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Venue_de_l_interface_l_extraction_repond()
    {
        await using var api = await PreparerAsync();

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, AdresseDeLExtraction, jeton: true, requete => Preparer(api, requete, api.Origine));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task L_extraction_figure_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var route = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]![AdresseDeLExtraction]!["post"]!.AsObject();
        Assert.Equal("ExtraireUneOffre", route["operationId"]!.GetValue<string>());
        Assert.Equal("Offres", route["tags"]![0]!.GetValue<string>());
        Assert.All(new[] { "200", "400", "409", "413" }, code => Assert.NotNull(route["responses"]![code]));
    }

    private static async Task<ApiLancee> PreparerAsync()
    {
        var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);
        api.FauxFournisseur.RepondreTexte("""{"entreprise":null,"poste":null,"lieu":null,"publieeLe":null,"adresse":null}""");
        return api;
    }

    private static void Preparer(ApiLancee api, HttpRequestMessage requete, string? origine)
    {
        if (origine is not null)
        {
            requete.Headers.Add("Origin", origine);
        }

        requete.Content = new StringContent(Corps, Encoding.UTF8, "application/json");
    }
}
