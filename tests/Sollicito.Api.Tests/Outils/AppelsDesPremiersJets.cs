using System.Net;
using System.Text.Json.Nodes;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Les appels du premier jet d'une relance (#47), comme l'interface les envoie, et les réponses que le faux fournisseur rend
/// à leur place : un texte écrit par un écrivain JSON, donc échappé, quel qu'il soit.
/// </summary>
public static class AppelsDesPremiersJets
{
    /// <summary>L'adresse du premier jet de la relance d'une candidature.</summary>
    public static string AdresseDuPremierJet(int candidature, int relance) => $"{AdresseDesRelances(candidature)}/{Texte(relance)}/premier-jet";

    /// <summary>L'adresse du dépôt du texte relu de la relance d'une candidature.</summary>
    public static string AdresseDuBrouillonRelu(int candidature, int relance) => $"{AdresseDesRelances(candidature)}/{Texte(relance)}/brouillon-relu";

    /// <summary>Demande le premier jet, sans corps, comme le bloc de la fiche.</summary>
    public static Task<HttpResponseMessage> DemanderUnPremierJetAsync(this ApiLancee api, int candidature, int relance) =>
        api.EcrireAsync(HttpMethod.Post, AdresseDuPremierJet(candidature, relance), corps: null);

    /// <summary>Demande le premier jet, et rend le résultat d'une demande partie (200).</summary>
    public static async Task<JsonObject> PremierJetAsync(this ApiLancee api, int candidature, int relance)
    {
        using var reponse = await api.DemanderUnPremierJetAsync(candidature, relance);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Dépose le texte relu.</summary>
    public static Task<HttpResponseMessage> DeposerLeBrouillonReluAsync(this ApiLancee api, int candidature, int relance, string? texte) =>
        api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(candidature, relance), new JsonObject { ["texte"] = texte }.ToJsonString());

    /// <summary>Le résultat attendu d'un premier jet, écrit comme l'API le rend.</summary>
    public static string PremierJet(bool reussi, string? texte, string? echec, string? message) =>
        new JsonObject { ["reussi"] = reussi, ["texte"] = texte, ["echec"] = echec, ["message"] = message }.ToJsonString();

    // Répondre par un texte complet, comme le fournisseur de la requête le rendrait : la méthode `RepondreTexte` d'`AppelsDeLExtraction`
    // (#46), que les tests du premier jet emploient aussi.

    /// <summary>La réponse complète du fournisseur de <paramref name="requete"/>, qui porte <paramref name="texte"/>.</summary>
    public static string Reponse(HttpRequestMessage requete, string texte)
    {
        ArgumentNullException.ThrowIfNull(requete);
        return requete.RequestUri?.Host == "api.anthropic.com"
            ? new JsonObject
            {
                ["id"] = "msg_47",
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = "claude-haiku-5-5",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texte }),
                ["stop_reason"] = "end_turn",
                ["stop_sequence"] = null,
            }.ToJsonString()
            : new JsonObject
            {
                ["id"] = "resp_47",
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
}
