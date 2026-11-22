using System.Net;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// Un champ répété rend le corps illisible (400) sur toutes les routes de l'API (#36) : sans le réglage
/// <c>AllowDuplicateProperties = false</c>, le dernier l'emporterait, et le corps strict de #10 se contournerait. Une
/// route de chaque famille : candidatures, fiche, contacts, relances, réglages, offres, « Postuler » (#38). Les réglages
/// JSON du web lisent les noms sans tenir compte de la casse : un champ répété à la casse près (« envoyeeLe », puis
/// « EnvoyeeLe ») est le même champ, répété, et se refuse de même (suites de #36, #98). Rien ne s'écrit.
/// </summary>
public sealed class ChampsRepetesTests
{
    public static TheoryData<string, string, string> Corps() => new()
    {
        { "POST", "/api/candidatures", """{"entreprise":"Lumière Verte SA","poste":"Comptable","poste":"Chauffeur","envoyeeLe":"2026-11-10T09:00"}""" },
        { "POST", "/api/candidatures/1/etapes", """{"type":"Appel","type":"Autre","survenuLe":"2026-11-12T09:00"}""" },
        { "POST", "/api/entreprises/1/contacts", """{"nom":"Jeanne Martin","email":"jeanne@exemple.invalid","email":"autre@exemple.invalid"}""" },
        { "POST", "/api/candidatures/1/relances", """{"delai":7,"delai":30}""" },
        { "PUT", "/api/reglages/relances", """{"delaiParDefaut":10,"delaiParDefaut":20}""" },
        { "POST", "/api/offres", """{"texte":"Offre fictive.","poste":"Comptable","garderLesDeux":false,"garderLesDeux":true}""" },
        { "POST", "/api/offres/1/candidature", """{"envoyeeLe":"2026-11-10T09:00","envoyeeLe":"2026-11-11T09:00"}""" },
        { "POST", "/api/offres/1/candidature", """{"envoyeeLe":"2026-11-10T09:00","EnvoyeeLe":"2026-11-11T09:00"}""" },
    };

    [Theory]
    [MemberData(nameof(Corps))]
    public async Task Un_champ_repete_rend_le_corps_illisible_et_rien_ne_s_ecrit(string methode, string chemin, string corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-10T09:00");
        // Une offre à qui postuler, d'un autre texte que celle qu'on colle (#38).
        await api.CollerAvecSuccesAsync("Offre fictive à laquelle postuler.", "Brasseuse", entreprise: "Brasserie Lune Rousse");
        var avant = await InstantaneAsync(api);

        using var reponse = await api.EcrireAsync(new HttpMethod(methode), chemin, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, await InstantaneAsync(api));
        // Le même corps, sans la répétition, est lu : c'est bien la répétition qui le refuse.
        using var sansRepetition = await api.EcrireAsync(new HttpMethod(methode), chemin, SansLaRepetition(corps));
        Assert.True(sansRepetition.IsSuccessStatusCode, $"{methode} {chemin} : {sansRepetition.StatusCode}");
    }

    // La seconde occurrence du champ répété retirée, à la casse près.
    private static string SansLaRepetition(string corps)
    {
        foreach (var champ in new[] { "poste", "type", "email", "delai", "delaiParDefaut", "garderLesDeux", "envoyeeLe" })
        {
            var marque = "\"" + champ + "\":";
            var premiere = corps.IndexOf(marque, StringComparison.Ordinal);
            var seconde = premiere < 0 ? -1 : corps.IndexOf(marque, premiere + marque.Length, StringComparison.OrdinalIgnoreCase);
            if (seconde >= 0)
            {
                var fin = corps.IndexOfAny([',', '}'], seconde);
                return corps[..(seconde - 1)] + corps[fin..];
            }
        }

        throw new ArgumentException("Aucun champ répété.", nameof(corps));
    }

    // Les candidatures, la fiche (historique et relances), les contacts, les entreprises, le réglage du délai, l'offre.
    private static async Task<string> InstantaneAsync(ApiLancee api)
    {
        using var reglage = await api.EnvoyerAsync(HttpMethod.Get, "/api/reglages/relances", jeton: true);
        using var offre = await api.EnvoyerAsync(HttpMethod.Get, AppelsDesOffres.AdresseDeLOffre(1), jeton: true);
        return (await api.ListerAsync()).ToJsonString()
            + (await api.FicheAsync(1)).ToJsonString()
            + (await api.ContactsAsync(1)).ToJsonString()
            + (await api.ListerLesEntreprisesAsync()).ToJsonString()
            + await reglage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            + offre.StatusCode;
    }
}
