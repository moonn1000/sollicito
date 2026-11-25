using System.Net;
using System.Text.Json.Nodes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>Les appels de l'extraction d'une offre collée (#46), et les réponses du faux fournisseur qui portent son texte.</summary>
public static class AppelsDeLExtraction
{
    /// <summary>L'adresse de l'extraction.</summary>
    public const string AdresseDeLExtraction = "/api/offres/extraction";

    /// <summary>Demande l'extraction de ce texte, comme l'écran « Coller une offre » le fait.</summary>
    public static Task<HttpResponseMessage> ExtraireAsync(this ApiLancee api, string? texte) =>
        api.EcrireAsync(HttpMethod.Post, AdresseDeLExtraction, new JsonObject { ["texte"] = texte }.ToJsonString());

    /// <summary>Demande l'extraction, et rend le résultat d'une extraction partie (200).</summary>
    public static async Task<JsonObject> ExtraireAvecResultatAsync(this ApiLancee api, string texte)
    {
        using var reponse = await api.ExtraireAsync(texte);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Répond, à chaque requête qui suit, comme son fournisseur répondrait avec ce texte de l'IA.</summary>
    public static void RepondreTexte(this FauxFournisseurEnLigne faux, string texte)
    {
        ArgumentNullException.ThrowIfNull(faux);
        faux.Repondre((requete, _) => Task.FromResult(FauxFournisseurEnLigne.Json(HttpStatusCode.OK, Reponse(requete, texte))));
    }

    /// <summary>La réponse complète du fournisseur de <paramref name="requete"/>, dont le texte de l'IA est <paramref name="texte"/>.</summary>
    public static string Reponse(HttpRequestMessage requete, string texte)
    {
        ArgumentNullException.ThrowIfNull(requete);
        return requete.RequestUri?.Host == "api.anthropic.com"
            ? new JsonObject
            {
                ["id"] = "msg_1",
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = "claude-haiku-5-5",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texte }),
                ["stop_reason"] = "end_turn",
                ["stop_sequence"] = null,
            }.ToJsonString()
            : new JsonObject
            {
                ["id"] = "resp_1",
                ["object"] = "response",
                ["status"] = "completed",
                ["model"] = "gpt-6-luna",
                ["output"] = new JsonArray(new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = texte }),
                }),
            }.ToJsonString();
    }

    /// <summary>Le résultat attendu d'une extraction proposée, écrit comme l'API le rend.</summary>
    public static string Proposee(string? entreprise, string? poste, string? lieu, string? publieeLe, string? adresse) =>
        new JsonObject
        {
            ["issue"] = "Proposee",
            ["champs"] = new JsonObject
            {
                ["entreprise"] = entreprise,
                ["poste"] = poste,
                ["lieu"] = lieu,
                ["publieeLe"] = publieeLe,
                ["adresse"] = adresse,
            },
            ["echec"] = null,
            ["message"] = null,
        }.ToJsonString();

    /// <summary>Le résultat attendu d'une extraction en échec, écrit comme l'API le rend.</summary>
    public static string EnEchec(string echec, string message) =>
        new JsonObject { ["issue"] = "Echec", ["champs"] = null, ["echec"] = echec, ["message"] = message }.ToJsonString();
}
