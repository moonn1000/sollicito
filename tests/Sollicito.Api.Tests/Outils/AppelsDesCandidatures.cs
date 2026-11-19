using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>Les appels de l'interface aux routes des candidatures : le jeton dans son en-tête, l'origine de l'interface.</summary>
public static class AppelsDesCandidatures
{
    /// <summary>Envoie une candidature, comme l'écran de saisie.</summary>
    public static Task<HttpResponseMessage> EnregistrerAsync(this ApiLancee api, string? entreprise, string? poste, string? envoyeeLe) =>
        api.EnvoyerCorpsAsync(new JsonObject
        {
            ["entreprise"] = entreprise,
            ["poste"] = poste,
            ["envoyeeLe"] = envoyeeLe,
        }.ToJsonString());

    /// <summary>Envoie un corps écrit à la main vers la route d'enregistrement.</summary>
    public static Task<HttpResponseMessage> EnvoyerCorpsAsync(this ApiLancee api, string corps, string typeDuCorps = "application/json") =>
        api.EnvoyerAsync(HttpMethod.Post, "/api/candidatures", jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Content = new StringContent(corps, Encoding.UTF8, typeDuCorps);
        });

    /// <summary>Enregistre une candidature qui doit être admise, et rend ce que l'API en rend.</summary>
    public static async Task<JsonObject> EnregistrerAvecSuccesAsync(this ApiLancee api, string entreprise, string poste, string envoyeeLe)
    {
        using var reponse = await api.EnregistrerAsync(entreprise, poste, envoyeeLe);
        Assert.Equal(System.Net.HttpStatusCode.Created, reponse.StatusCode);
        return (await LireAsync(reponse)).AsObject();
    }

    /// <summary>Les candidatures du tableau de bord.</summary>
    public static async Task<JsonArray> ListerAsync(this ApiLancee api) => (await LireAsync(api, "/api/candidatures")).AsArray();

    /// <summary>Les entreprises que l'écran de saisie propose.</summary>
    public static async Task<JsonArray> ListerLesEntreprisesAsync(this ApiLancee api) => (await LireAsync(api, "/api/entreprises")).AsArray();

    /// <summary>Le corps JSON d'une réponse.</summary>
    public static async Task<JsonNode> LireAsync(HttpResponseMessage reponse) =>
        await reponse.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException("Réponse sans corps JSON.");

    private static async Task<JsonNode> LireAsync(ApiLancee api, string chemin)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: true);
        Assert.Equal(System.Net.HttpStatusCode.OK, reponse.StatusCode);
        return await LireAsync(reponse);
    }
}
