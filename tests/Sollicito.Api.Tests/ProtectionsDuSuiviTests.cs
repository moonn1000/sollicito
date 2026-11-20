using System.Net;
using System.Text;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les routes de la fiche d'une candidature (#12) passent par les protections de #10 (ADR-0001, D9 ; ADR-0003) :
/// <c>Host</c> (400), jeton (401), <c>Origin</c> (403), et figurent dans la documentation OpenAPI. Une requête refusée
/// ne change rien : la candidature garde son statut et son historique.
/// </summary>
public sealed class ProtectionsDuSuiviTests
{
    // {id} et {changement} : la candidature du test, et le changement de statut qu'elle propose de corriger.
    public static TheoryData<string, string, string?> Routes => new()
    {
        { "GET", "/api/candidatures/{id}", null },
        { "POST", "/api/candidatures/{id}/statut", """{"statut":"Refusee"}""" },
        { "POST", "/api/candidatures/{id}/changements/{changement}/correction", null },
        { "POST", "/api/candidatures/{id}/etapes", """{"type":"Appel","survenuLe":"2026-11-10T10:00","note":"Rappel."}""" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), await CheminAsync(api, chemin, id), jeton: false, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), await CheminAsync(api, chemin, id), jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Host = "localhost:" + api.Port;
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task D_une_autre_origine_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), await CheminAsync(api, chemin, id), jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Remove("Origin");
            requete.Headers.Add("Origin", "http://exemple.invalid");
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [InlineData("/api/candidatures/{id}/statut", """{"statut":"Refusee"}""", null)]
    [InlineData("/api/candidatures/{id}/statut", """{"statut":"Refusee"}""", "null")]
    [InlineData("/api/candidatures/{id}/changements/{changement}/correction", null, null)]
    [InlineData("/api/candidatures/{id}/changements/{changement}/correction", null, "null")]
    [InlineData("/api/candidatures/{id}/etapes", """{"type":"Appel","survenuLe":"2026-11-10T10:00"}""", null)]
    [InlineData("/api/candidatures/{id}/etapes", """{"type":"Appel","survenuLe":"2026-11-10T10:00"}""", "null")]
    public async Task Une_ecriture_sans_l_origine_de_l_interface_est_refusee(string chemin, string? corps, string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, await CheminAsync(api, chemin, id), jeton: true, requete =>
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
    [MemberData(nameof(Routes))]
    public async Task Venues_de_l_interface_les_routes_repondent(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (id, _) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), await CheminAsync(api, chemin, id), jeton: true, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!.AsObject();
        var fiche = chemins["/api/candidatures/{id}"]!["get"]!.AsObject();
        Assert.Equal("LireLaFicheDUneCandidature", fiche["operationId"]!.GetValue<string>());
        Assert.NotNull(fiche["responses"]!["200"]);
        Assert.NotNull(fiche["responses"]!["404"]);
        var statut = chemins["/api/candidatures/{id}/statut"]!["post"]!.AsObject();
        Assert.Equal("ChangerLeStatutDUneCandidature", statut["operationId"]!.GetValue<string>());
        Assert.NotNull(statut["requestBody"]!["content"]!["application/json"]);
        foreach (var code in new[] { "200", "400", "404", "409" })
        {
            Assert.NotNull(statut["responses"]![code]);
        }

        var correction = chemins["/api/candidatures/{id}/changements/{changement}/correction"]!["post"]!.AsObject();
        Assert.Equal("CorrigerLeDernierChangementDeStatut", correction["operationId"]!.GetValue<string>());
        Assert.NotNull(correction["responses"]!["409"]);
        var etapes = chemins["/api/candidatures/{id}/etapes"]!["post"]!.AsObject();
        Assert.Equal("NoterUneEtape", etapes["operationId"]!.GetValue<string>());
        Assert.NotNull(etapes["requestBody"]!["content"]!["application/json"]);
        Assert.NotNull(etapes["responses"]!["400"]);
    }

    // Une candidature passée à Entretien : sa fiche propose de corriger ce changement. Rend son identifiant et sa fiche.
    private static async Task<(int Id, string Fiche)> PreparerAsync(ApiLancee api)
    {
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var fiche = await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        return (id, fiche.ToJsonString());
    }

    private static async Task<string> CheminAsync(ApiLancee api, string modele, int id)
    {
        var chemin = modele.Replace("{id}", id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (chemin.Contains("{changement}", StringComparison.Ordinal))
        {
            var changement = (await api.FicheAsync(id))["correction"]!["changement"]!.GetValue<int>();
            chemin = chemin.Replace("{changement}", changement.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return chemin;
    }

    // L'origine de l'interface et le corps pour une écriture ; rien pour une lecture.
    private static void Preparer(ApiLancee api, HttpRequestMessage requete, string? corps)
    {
        if (requete.Method == HttpMethod.Get)
        {
            return;
        }

        requete.Headers.Add("Origin", api.Origine);
        if (corps is not null)
        {
            requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        }
    }
}
