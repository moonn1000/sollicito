using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Les appels de la modification et de la suppression d'une candidature (#25), comme l'écran de modification et la
/// fiche les envoient : le jeton dans son en-tête, l'origine de l'interface.
/// </summary>
public static class AppelsDeLaModification
{
    /// <summary>Envoie une modification, champ par champ, comme l'écran de modification.</summary>
    public static Task<HttpResponseMessage> ModifierAsync(this ApiLancee api, int id, string? entreprise, string? poste, string? envoyeeLe) =>
        api.EnvoyerALaModificationAsync(id, new JsonObject
        {
            ["entreprise"] = entreprise,
            ["poste"] = poste,
            ["envoyeeLe"] = envoyeeLe,
        }.ToJsonString());

    /// <summary>Envoie une modification qui doit être admise, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> ModifierAvecSuccesAsync(this ApiLancee api, int id, string entreprise, string poste, string envoyeeLe)
    {
        using var reponse = await api.ModifierAsync(id, entreprise, poste, envoyeeLe);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Envoie un corps écrit à la main vers la route de modification.</summary>
    public static Task<HttpResponseMessage> EnvoyerALaModificationAsync(this ApiLancee api, int id, string corps, string typeDuCorps = "application/json") =>
        api.EnvoyerAsync(HttpMethod.Put, AdresseDeLaFiche(id), jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Content = new StringContent(corps, Encoding.UTF8, typeDuCorps);
        });

    /// <summary>Demande la suppression d'une candidature, comme la fiche après sa confirmation.</summary>
    public static Task<HttpResponseMessage> SupprimerAsync(this ApiLancee api, int id) =>
        api.EnvoyerAsync(HttpMethod.Delete, AdresseDeLaFiche(id), jeton: true, requete => requete.Headers.Add("Origin", api.Origine));
}
