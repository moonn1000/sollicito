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
    /// <summary>
    /// Envoie une modification, champ par champ, comme l'écran de modification ; avec l'accord de corriger le nom de
    /// l'entreprise, si le test le donne.
    /// </summary>
    public static Task<HttpResponseMessage> ModifierAsync(this ApiLancee api, int id, string? entreprise, string? poste, string? envoyeeLe, bool? corrigerLeNom = null)
    {
        var corps = new JsonObject
        {
            ["entreprise"] = entreprise,
            ["poste"] = poste,
            ["envoyeeLe"] = envoyeeLe,
        };
        if (corrigerLeNom is { } accord)
        {
            corps["corrigerLeNomDeLEntreprise"] = accord;
        }

        return api.EnvoyerALaModificationAsync(id, corps.ToJsonString());
    }

    /// <summary>Envoie une modification qui doit être admise, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> ModifierAvecSuccesAsync(this ApiLancee api, int id, string entreprise, string poste, string envoyeeLe, bool? corrigerLeNom = null)
    {
        using var reponse = await api.ModifierAsync(id, entreprise, poste, envoyeeLe, corrigerLeNom);
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

    /// <summary>L'adresse de la correction d'une étape de la candidature.</summary>
    public static string AdresseDeLaCorrection(int id, int etape) =>
        AdresseDeLaFiche(id) + "/etapes/" + etape.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/correction";

    /// <summary>Corrige une étape, champ par champ, comme le formulaire de correction de la fiche l'envoie.</summary>
    public static Task<HttpResponseMessage> CorrigerUneEtapeAsync(this ApiLancee api, int id, int etape, string? type, string? survenuLe, string? note) =>
        api.EnvoyerALaCorrectionDEtapeAsync(id, etape, new JsonObject
        {
            ["type"] = type,
            ["survenuLe"] = survenuLe,
            ["note"] = note,
        }.ToJsonString());

    /// <summary>Corrige une étape, ce qui doit être admis, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> CorrigerUneEtapeAvecSuccesAsync(this ApiLancee api, int id, int etape, string type, string survenuLe, string? note)
    {
        using var reponse = await api.CorrigerUneEtapeAsync(id, etape, type, survenuLe, note);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Envoie un corps écrit à la main vers la route de correction d'une étape.</summary>
    public static Task<HttpResponseMessage> EnvoyerALaCorrectionDEtapeAsync(this ApiLancee api, int id, int etape, string corps, string typeDuCorps = "application/json") =>
        api.EnvoyerAsync(HttpMethod.Post, AdresseDeLaCorrection(id, etape), jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Content = new StringContent(corps, Encoding.UTF8, typeDuCorps);
        });

    /// <summary>Demande la suppression d'une candidature, comme la fiche après sa confirmation.</summary>
    public static Task<HttpResponseMessage> SupprimerAsync(this ApiLancee api, int id) =>
        api.EnvoyerAsync(HttpMethod.Delete, AdresseDeLaFiche(id), jeton: true, requete => requete.Headers.Add("Origin", api.Origine));
}
