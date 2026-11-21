using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Les appels des contacts et des relances (#14), comme l'interface les envoie : le jeton dans son en-tête, l'origine de
/// l'interface sur chaque écriture.
/// </summary>
public static class AppelsDesRelances
{
    /// <summary>Un nombre dans une adresse.</summary>
    public static string Texte(int nombre) => nombre.ToString(CultureInfo.InvariantCulture);

    /// <summary>Ajoute un contact à l'entreprise.</summary>
    public static Task<HttpResponseMessage> AjouterUnContactAsync(this ApiLancee api, int entreprise, string? nom, string? email) =>
        api.EcrireAsync(HttpMethod.Post, $"/api/entreprises/{Texte(entreprise)}/contacts", new JsonObject { ["nom"] = nom, ["email"] = email }.ToJsonString());

    /// <summary>Ajoute un contact qui doit être admis, et rend ce que l'API en rend.</summary>
    public static async Task<JsonObject> AjouterUnContactAvecSuccesAsync(this ApiLancee api, int entreprise, string nom, string email)
    {
        using var reponse = await api.AjouterUnContactAsync(entreprise, nom, email);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Les contacts de l'entreprise, qui doit exister.</summary>
    public static async Task<JsonArray> ContactsAsync(this ApiLancee api, int entreprise)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, $"/api/entreprises/{Texte(entreprise)}/contacts", jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsArray();
    }

    /// <summary>Corrige le nom et l'adresse d'un contact.</summary>
    public static Task<HttpResponseMessage> ModifierLeContactAsync(this ApiLancee api, int contact, string? nom, string? email) =>
        api.EcrireAsync(HttpMethod.Put, $"/api/contacts/{Texte(contact)}", new JsonObject { ["nom"] = nom, ["email"] = email }.ToJsonString());

    /// <summary>Supprime un contact.</summary>
    public static Task<HttpResponseMessage> SupprimerLeContactAsync(this ApiLancee api, int contact) =>
        api.EcrireAsync(HttpMethod.Delete, $"/api/contacts/{Texte(contact)}", corps: null);

    /// <summary>L'adresse des relances d'une candidature.</summary>
    public static string AdresseDesRelances(int candidature) => AppelsDuSuivi.AdresseDeLaFiche(candidature) + "/relances";

    /// <summary>Planifie une relance ; un délai ou un destinataire absent ne part pas dans le corps.</summary>
    public static Task<HttpResponseMessage> PlanifierAsync(this ApiLancee api, int candidature, int? delai = null, int? destinataire = null) =>
        api.EcrireAsync(HttpMethod.Post, AdresseDesRelances(candidature), Corps(delai, destinataire));

    /// <summary>Planifie une relance qui doit être admise, et rend la fiche à jour.</summary>
    public static async Task<JsonObject> PlanifierAvecSuccesAsync(this ApiLancee api, int candidature, int? delai = null, int? destinataire = null)
    {
        using var reponse = await api.PlanifierAsync(candidature, delai, destinataire);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Change le délai et le destinataire de la relance.</summary>
    public static Task<HttpResponseMessage> ChangerLaRelanceAsync(this ApiLancee api, int candidature, int relance, int? delai = null, int? destinataire = null) =>
        api.EcrireAsync(HttpMethod.Put, $"{AdresseDesRelances(candidature)}/{Texte(relance)}", Corps(delai, destinataire));

    /// <summary>Marque la relance faite.</summary>
    public static Task<HttpResponseMessage> MarquerFaiteAsync(this ApiLancee api, int candidature, int relance) =>
        api.EcrireAsync(HttpMethod.Post, $"{AdresseDesRelances(candidature)}/{Texte(relance)}/faite", corps: null);

    /// <summary>Abandonne la relance.</summary>
    public static Task<HttpResponseMessage> AbandonnerAsync(this ApiLancee api, int candidature, int relance) =>
        api.EcrireAsync(HttpMethod.Post, $"{AdresseDesRelances(candidature)}/{Texte(relance)}/abandon", corps: null);

    /// <summary>Les relances que signale le tableau de bord.</summary>
    public static async Task<JsonObject> RelancesASignalerAsync(this ApiLancee api)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/relances", jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Le délai par défaut des relances, et ce qui ne se lisait pas.</summary>
    public static async Task<JsonObject> ReglageDesRelancesAsync(this ApiLancee api)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/reglages/relances", jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Change le délai par défaut des relances.</summary>
    public static Task<HttpResponseMessage> ChangerLeDelaiParDefautAsync(this ApiLancee api, int? jours) =>
        api.EcrireAsync(HttpMethod.Put, "/api/reglages/relances", new JsonObject { ["delaiParDefaut"] = jours }.ToJsonString());

    /// <summary>La relance à faire de la fiche, ou rien.</summary>
    public static JsonObject? RelanceAFaire(JsonObject fiche) =>
        fiche["relances"]!.AsArray().Select(relance => relance!.AsObject()).SingleOrDefault(relance => relance["etat"]!.GetValue<string>() == "AFaire");

    /// <summary>L'identifiant d'un objet rendu par l'API.</summary>
    public static int Id(JsonNode? objet) => objet!["id"]!.GetValue<int>();

    /// <summary>Envoie une écriture de l'interface, avec ou sans corps JSON.</summary>
    public static Task<HttpResponseMessage> EcrireAsync(this ApiLancee api, HttpMethod methode, string chemin, string? corps, string typeDuCorps = "application/json") =>
        api.EnvoyerAsync(methode, chemin, jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            if (corps is not null)
            {
                requete.Content = new StringContent(corps, Encoding.UTF8, typeDuCorps);
            }
        });

    private static string Corps(int? delai, int? destinataire)
    {
        var corps = new JsonObject();
        if (delai is not null)
        {
            corps["delai"] = delai;
        }

        if (destinataire is not null)
        {
            corps["destinataire"] = destinataire;
        }

        return corps.ToJsonString();
    }
}
