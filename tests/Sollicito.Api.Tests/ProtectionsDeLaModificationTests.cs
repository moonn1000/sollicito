using System.Globalization;
using System.Net;
using System.Text;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les trois routes de #25, modifier et supprimer une candidature, et corriger une étape, passent par les protections
/// de #10 (ADR-0001, D9 ; ADR-0003) : <c>Host</c> (400), jeton (401), <c>Origin</c> (403), et figurent dans la
/// documentation OpenAPI. Une requête refusée ne change rien : la candidature reste, telle quelle.
/// </summary>
public sealed class ProtectionsDeLaModificationTests
{
    private const string Modification = """{"entreprise":"Brasserie Lune Rousse","poste":"Brasseuse","envoyeeLe":"2026-11-01T09:00"}""";

    private const string Correction = """{"type":"Entretien","survenuLe":"2026-11-11T10:00","note":"Premier entretien."}""";

    // {id} et {etape} : la candidature du test, et l'étape qu'elle a notée.
    public static TheoryData<string, string, string?> Routes => new()
    {
        { "PUT", "/api/candidatures/{id}", Modification },
        { "DELETE", "/api/candidatures/{id}", null },
        { "POST", "/api/candidatures/{id}/etapes/{etape}/correction", Correction },
    };

    public static TheoryData<string, string, string?, string?> SansLOrigineDeLInterface
    {
        get
        {
            var donnees = new TheoryData<string, string, string?, string?>();
            foreach (var (methode, chemin, corps) in new[]
            {
                ("PUT", "/api/candidatures/{id}", Modification),
                ("DELETE", "/api/candidatures/{id}", null),
                ("POST", "/api/candidatures/{id}/etapes/{etape}/correction", Correction),
            })
            {
                foreach (var origine in new[] { "http://exemple.invalid", "null", null })
                {
                    donnees.Add(methode, chemin, corps, origine);
                }
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, etape, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Chemin(chemin, id, etape), jeton: false, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, etape, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Chemin(chemin, id, etape), jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(SansLOrigineDeLInterface))]
    public async Task Une_ecriture_sans_l_origine_de_l_interface_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps, string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, etape, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Chemin(chemin, id, etape), jeton: true, requete =>
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
    [InlineData("PUT", "/api/candidatures/{id}", Modification, HttpStatusCode.OK)]
    [InlineData("DELETE", "/api/candidatures/{id}", null, HttpStatusCode.NoContent)]
    [InlineData("POST", "/api/candidatures/{id}/etapes/{etape}/correction", Correction, HttpStatusCode.OK)]
    public async Task Venues_de_l_interface_les_routes_repondent(string methode, string chemin, string? corps, HttpStatusCode attendu)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, etape, _) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Chemin(chemin, id, etape), jeton: true, requete => Preparer(api, requete, corps));

        Assert.Equal(attendu, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!;
        var fiche = chemins["/api/candidatures/{id}"]!.AsObject();
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

        var correction = chemins["/api/candidatures/{id}/etapes/{etape}/correction"]!["post"]!.AsObject();
        Assert.Equal("CorrigerUneEtape", correction["operationId"]!.GetValue<string>());
        Assert.NotNull(correction["requestBody"]!["content"]!["application/json"]);
        foreach (var code in new[] { "200", "400", "404", "409" })
        {
            Assert.NotNull(correction["responses"]![code]);
        }
    }

    // Une candidature passée à Entretien, avec un appel noté : rend son identifiant, celui de l'étape, et sa fiche.
    private static async Task<(int Id, int Etape, string Fiche)> PreparerAsync(ApiLancee api)
    {
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var etape = Assert.Single((await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null))["historique"]!.AsArray())!["id"]!.GetValue<int>();
        var fiche = await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        return (id, etape, fiche.ToJsonString());
    }

    private static string Chemin(string modele, int id, int etape) => modele
        .Replace("{id}", id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{etape}", etape.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

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
