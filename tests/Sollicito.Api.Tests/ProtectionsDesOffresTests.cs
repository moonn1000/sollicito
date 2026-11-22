using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les routes des offres (#36), leur liste et « Postuler » (#38) passent par les protections de #10 (ADR-0001, D9 ;
/// ADR-0003) : <c>Host</c> (400), jeton (401), <c>Origin</c> (403) ; elles figurent dans la documentation OpenAPI, et la
/// porte de la base (#28) les garde d'office (503). Une requête refusée n'enregistre ni ne change rien.
/// </summary>
public sealed class ProtectionsDesOffresTests
{
    // {offre} : une offre enregistrée.
    private static readonly (string Methode, string Chemin, string? Corps, HttpStatusCode Attendu)[] LesRoutes =
    [
        ("POST", "/api/offres", """{"texte":"Autre offre fictive.","poste":"Chauffeur"}""", HttpStatusCode.Created),
        ("GET", "/api/offres", null, HttpStatusCode.OK),
        ("GET", "/api/offres/{offre}", null, HttpStatusCode.OK),
        ("PUT", "/api/offres/{offre}", """{"poste":"Chauffeur"}""", HttpStatusCode.OK),
        ("POST", "/api/offres/{offre}/candidature", """{"envoyeeLe":"2026-11-18T10:00"}""", HttpStatusCode.Created),
    ];

    public static TheoryData<string, string, string?> Routes
    {
        get
        {
            var donnees = new TheoryData<string, string, string?>();
            foreach (var (methode, chemin, corps, _) in LesRoutes)
            {
                donnees.Add(methode, chemin, corps);
            }

            return donnees;
        }
    }

    public static TheoryData<string, string, string?, HttpStatusCode> RoutesEtReponses
    {
        get
        {
            var donnees = new TheoryData<string, string, string?, HttpStatusCode>();
            foreach (var (methode, chemin, corps, attendu) in LesRoutes)
            {
                donnees.Add(methode, chemin, corps, attendu);
            }

            return donnees;
        }
    }

    public static TheoryData<string, string, string?, string?> SansLOrigineDeLInterface
    {
        get
        {
            var donnees = new TheoryData<string, string, string?, string?>();
            foreach (var (methode, chemin, corps, _) in LesRoutes)
            {
                // Une lecture peut se passer d'origine ; d'une autre origine, elle est refusée aussi.
                foreach (var origine in methode == "GET" ? new string?[] { "http://exemple.invalid" } : new string?[] { "http://exemple.invalid", "null", null })
                {
                    donnees.Add(methode, chemin, corps, origine);
                }
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (offre, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Remplir(chemin, offre), jeton: false, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(avant, await InstantaneAsync(api, offre));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (offre, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Remplir(chemin, offre), jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, await InstantaneAsync(api, offre));
    }

    [Theory]
    [MemberData(nameof(SansLOrigineDeLInterface))]
    public async Task Sans_l_origine_de_l_interface_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps, string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (offre, avant) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Remplir(chemin, offre), jeton: true, requete =>
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
        Assert.Equal(avant, await InstantaneAsync(api, offre));
    }

    [Theory]
    [MemberData(nameof(RoutesEtReponses))]
    public async Task Venues_de_l_interface_les_routes_repondent(string methode, string chemin, string? corps, HttpStatusCode attendu)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (offre, _) = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Remplir(chemin, offre), jeton: true, requete => Preparer(api, requete, corps));

        Assert.Equal(attendu, reponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Porte_fermee_pendant_une_restauration_la_route_est_refusee_par_503(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (offre, avant) = await PreparerAsync(api);
        var porte = api.Services.GetRequiredService<PorteDeLaBase>();
        Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        try
        {
            using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), Remplir(chemin, offre), jeton: true, requete => Preparer(api, requete, corps));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
            Assert.Equal(PorteDeLaBase.TitreDuRefus, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        }
        finally
        {
            porte.Ouvrir();
        }

        Assert.Equal(avant, await InstantaneAsync(api, offre));
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!;
        foreach (var (chemin, methode, operation, codes) in new (string, string, string, string[])[]
        {
            ("/api/offres", "post", "CollerUneOffre", ["201", "400", "409", "413"]),
            ("/api/offres", "get", "ListerLesOffres", ["200"]),
            ("/api/offres/{id}", "get", "LireUneOffre", ["200", "404"]),
            ("/api/offres/{id}", "put", "CorrigerUneOffre", ["200", "400", "404", "409"]),
            ("/api/offres/{id}/candidature", "post", "PostulerAUneOffre", ["201", "400", "404", "409", "413"]),
        })
        {
            var route = chemins[chemin]![methode]!.AsObject();
            Assert.Equal(operation, route["operationId"]!.GetValue<string>());
            Assert.All(codes, code => Assert.NotNull(route["responses"]![code]));
        }
    }

    private static string Remplir(string chemin, int offre) =>
        chemin.Replace("{offre}", offre.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static void Preparer(ApiLancee api, HttpRequestMessage requete, string? corps)
    {
        if (requete.Method != HttpMethod.Get)
        {
            requete.Headers.Add("Origin", api.Origine);
        }

        if (corps is not null)
        {
            requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        }
    }

    private static async Task<(int Offre, string Avant)> PreparerAsync(ApiLancee api)
    {
        var offre = AppelsDesRelances.Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        return (offre, await InstantaneAsync(api, offre));
    }

    // Ce qu'une requête refusée ne doit pas changer : l'offre, sa candidature, les offres suivantes, les candidatures, les
    // entreprises proposées.
    private static async Task<string> InstantaneAsync(ApiLancee api, int offre)
    {
        var instantane = new StringBuilder((await api.LireLOffreAsync(offre)).ToJsonString());
        using (var suivante = await api.EnvoyerAsync(HttpMethod.Get, AdresseDeLOffre(offre + 1), jeton: true))
        {
            instantane.Append(suivante.StatusCode);
        }

        instantane.Append((await api.ListerAsync()).ToJsonString());
        instantane.Append((await api.ListerLesEntreprisesAsync()).ToJsonString());
        return instantane.ToString();
    }
}
