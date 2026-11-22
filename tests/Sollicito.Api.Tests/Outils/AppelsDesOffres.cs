using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>Les appels des offres collées (#36) : coller une offre, lire sa fiche, la corriger. Toutes les données sont fictives.</summary>
public static class AppelsDesOffres
{
    /// <summary>La route des offres.</summary>
    public const string Route = "/api/offres";

    /// <summary>L'adresse de la fiche d'une offre.</summary>
    public static string AdresseDeLOffre(int id) => Route + "/" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Le corps d'une offre collée ; un champ à rien n'y paraît pas.</summary>
    public static string Corps(
        string? texte,
        string? poste,
        string? entreprise = null,
        string? lieu = null,
        string? publieeLe = null,
        string? adresse = null,
        bool? garderLesDeux = null)
    {
        var corps = new JsonObject();
        Ajouter(corps, "texte", texte);
        Ajouter(corps, "entreprise", entreprise);
        Ajouter(corps, "poste", poste);
        Ajouter(corps, "lieu", lieu);
        Ajouter(corps, "publieeLe", publieeLe);
        Ajouter(corps, "adresse", adresse);
        if (garderLesDeux is not null)
        {
            corps["garderLesDeux"] = garderLesDeux;
        }

        return corps.ToJsonString();
    }

    /// <summary>Colle une offre, comme l'écran l'envoie.</summary>
    public static Task<HttpResponseMessage> CollerAsync(
        this ApiLancee api,
        string? texte,
        string? poste,
        string? entreprise = null,
        string? lieu = null,
        string? publieeLe = null,
        string? adresse = null,
        bool? garderLesDeux = null) =>
        api.EcrireAsync(HttpMethod.Post, Route, Corps(texte, poste, entreprise, lieu, publieeLe, adresse, garderLesDeux));

    /// <summary>Colle une offre qui doit s'enregistrer, et rend sa fiche.</summary>
    public static async Task<JsonObject> CollerAvecSuccesAsync(
        this ApiLancee api,
        string texte,
        string poste,
        string? entreprise = null,
        string? lieu = null,
        string? publieeLe = null,
        string? adresse = null,
        bool? garderLesDeux = null)
    {
        using var reponse = await api.CollerAsync(texte, poste, entreprise, lieu, publieeLe, adresse, garderLesDeux);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>La fiche d'une offre, qui doit exister.</summary>
    public static async Task<JsonObject> LireLOffreAsync(this ApiLancee api, int id)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, AdresseDeLOffre(id), jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Corrige les champs d'une offre, comme l'écran l'envoie.</summary>
    public static Task<HttpResponseMessage> CorrigerLOffreAsync(
        this ApiLancee api,
        int id,
        string? poste,
        string? entreprise = null,
        string? lieu = null,
        string? publieeLe = null,
        string? adresse = null,
        bool? garderLesDeux = null) =>
        api.EcrireAsync(HttpMethod.Put, AdresseDeLOffre(id), Corps(texte: null, poste, entreprise, lieu, publieeLe, adresse, garderLesDeux));

    /// <summary>Les identifiants des offres d'une liste rendue par l'API.</summary>
    public static int[] Ids(JsonNode? liste) => [.. liste!.AsArray().Select(offre => offre!["id"]!.GetValue<int>())];

    /// <summary>La liste des offres (#38), qui doit se lire.</summary>
    public static async Task<JsonArray> ListerLesOffresAsync(this ApiLancee api)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, Route, jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsArray();
    }

    /// <summary>L'adresse de « Postuler » à une offre (#38).</summary>
    public static string AdresseDePostuler(int offre) => AdresseDeLOffre(offre) + "/candidature";

    /// <summary>Le corps de « Postuler » : la seule date d'envoi ; à rien, elle n'y paraît pas.</summary>
    public static string CorpsDePostuler(string? envoyeeLe)
    {
        var corps = new JsonObject();
        Ajouter(corps, "envoyeeLe", envoyeeLe);
        return corps.ToJsonString();
    }

    /// <summary>Postule à une offre, comme l'écran l'envoie.</summary>
    public static Task<HttpResponseMessage> PostulerAsync(this ApiLancee api, int offre, string? envoyeeLe) =>
        api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(offre), CorpsDePostuler(envoyeeLe));

    /// <summary>Postule à une offre, ce qui doit réussir, et rend la candidature créée.</summary>
    public static async Task<JsonObject> PostulerAvecSuccesAsync(this ApiLancee api, int offre, string envoyeeLe = "2026-11-18T10:00")
    {
        using var reponse = await api.PostulerAsync(offre, envoyeeLe);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    private static void Ajouter(JsonObject corps, string champ, string? valeur)
    {
        if (valeur is not null)
        {
            corps[champ] = valeur;
        }
    }
}
