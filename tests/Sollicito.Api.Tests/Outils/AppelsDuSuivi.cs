using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Les appels de la fiche d'une candidature (#12) : la lire, changer son statut, corriger son dernier changement,
/// noter une étape. Le jeton dans son en-tête, l'origine de l'interface sur chaque écriture.
/// </summary>
public static class AppelsDuSuivi
{
    /// <summary>L'adresse de la fiche d'une candidature.</summary>
    public static string AdresseDeLaFiche(int id) => "/api/candidatures/" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Lit la fiche, telle que l'API la rend.</summary>
    public static Task<HttpResponseMessage> LireLaFicheAsync(this ApiLancee api, int id) =>
        api.EnvoyerAsync(HttpMethod.Get, AdresseDeLaFiche(id), jeton: true);

    /// <summary>Lit la fiche d'une candidature qui doit exister.</summary>
    public static async Task<JsonObject> FicheAsync(this ApiLancee api, int id)
    {
        using var reponse = await api.LireLaFicheAsync(id);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Demande le passage à un statut, donné par son nom, comme la fiche l'envoie.</summary>
    public static Task<HttpResponseMessage> ChangerLeStatutAsync(this ApiLancee api, int id, string? statut) =>
        api.EnvoyerAuStatutAsync(id, new JsonObject { ["statut"] = statut }.ToJsonString());

    /// <summary>Demande un passage qui doit être admis, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> ChangerLeStatutAvecSuccesAsync(this ApiLancee api, int id, string statut)
    {
        using var reponse = await api.ChangerLeStatutAsync(id, statut);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Envoie un corps écrit à la main vers la route du statut.</summary>
    public static Task<HttpResponseMessage> EnvoyerAuStatutAsync(this ApiLancee api, int id, string corps, string typeDuCorps = "application/json") =>
        api.EcrireAsync(AdresseDeLaFiche(id) + "/statut", corps, typeDuCorps);

    /// <summary>Demande la correction d'un changement de statut, désigné par son identifiant.</summary>
    public static Task<HttpResponseMessage> CorrigerAsync(this ApiLancee api, int id, int changement) =>
        api.EcrireAsync(AdresseDeLaFiche(id) + "/changements/" + changement.ToString(CultureInfo.InvariantCulture) + "/correction", corps: null);

    /// <summary>Demande la correction du changement que la fiche propose de corriger, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> CorrigerAvecSuccesAsync(this ApiLancee api, int id)
    {
        var aCorriger = (await api.FicheAsync(id))["correction"]!["changement"]!.GetValue<int>();
        using var reponse = await api.CorrigerAsync(id, aCorriger);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Note une étape, champ par champ, comme le formulaire de la fiche l'envoie.</summary>
    public static Task<HttpResponseMessage> NoterUneEtapeAsync(this ApiLancee api, int id, string? type, string? survenuLe, string? note) =>
        api.EnvoyerALEtapeAsync(id, new JsonObject
        {
            ["type"] = type,
            ["survenuLe"] = survenuLe,
            ["note"] = note,
        }.ToJsonString());

    /// <summary>Note une étape qui doit être admise, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> NoterUneEtapeAvecSuccesAsync(this ApiLancee api, int id, string type, string survenuLe, string? note)
    {
        using var reponse = await api.NoterUneEtapeAsync(id, type, survenuLe, note);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Envoie un corps écrit à la main vers la route des étapes.</summary>
    public static Task<HttpResponseMessage> EnvoyerALEtapeAsync(this ApiLancee api, int id, string corps, string typeDuCorps = "application/json") =>
        api.EcrireAsync(AdresseDeLaFiche(id) + "/etapes", corps, typeDuCorps);

    /// <summary>L'identifiant d'une candidature, tel que l'API l'a rendu.</summary>
    public static int IdDe(JsonObject candidature) => candidature["id"]!.GetValue<int>();

    private static Task<HttpResponseMessage> EcrireAsync(this ApiLancee api, string chemin, string? corps, string typeDuCorps = "application/json") =>
        api.EnvoyerAsync(HttpMethod.Post, chemin, jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            if (corps is not null)
            {
                requete.Content = new StringContent(corps, Encoding.UTF8, typeDuCorps);
            }
        });
}
