using System.Globalization;
using System.Net;
using System.Text;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les quatre routes du réglage de l'IA (#43) et celle de l'essai de la clé (#48) passent par les protections de #10
/// (ADR-0001, D9 ; ADR-0003) : <c>Host</c> (400), jeton (401), <c>Origin</c> (403), et figurent dans la documentation
/// OpenAPI. Une requête refusée ne change rien : ni le fournisseur, ni la clé, ni le fichier des réglages, et aucun essai
/// ne part vers le fournisseur.
/// </summary>
public sealed class ProtectionsDeLIATests
{
    private static readonly (string Methode, string Chemin, string? Corps)[] LesRoutes =
    [
        ("GET", Adresse, null),
        ("PUT", Adresse, """{"fournisseur":"OpenAI"}"""),
        ("PUT", AdresseDeLaCle, $$"""{"cle":"{{AutreCleDeClaude}}"}"""),
        ("DELETE", AdresseDeLaCle, null),
        ("POST", AdresseDeLEssai, null),
    ];

    public static TheoryData<string, string, string?> Routes
    {
        get
        {
            var donnees = new TheoryData<string, string, string?>();
            foreach (var (methode, chemin, corps) in LesRoutes)
            {
                donnees.Add(methode, chemin, corps);
            }

            return donnees;
        }
    }

    public static TheoryData<string, string, string?, string?> SansLOrigineDeLInterface
    {
        get
        {
            var donnees = new TheoryData<string, string, string?, string?>();
            foreach (var (methode, chemin, corps) in LesRoutes)
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
        var avant = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: false, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(avant, await InstantaneAsync(api));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var avant = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, await InstantaneAsync(api));
    }

    [Theory]
    [MemberData(nameof(SansLOrigineDeLInterface))]
    public async Task Sans_l_origine_de_l_interface_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps, string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var avant = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
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
        Assert.Equal(avant, await InstantaneAsync(api));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Venues_de_l_interface_les_routes_repondent_sans_jamais_rendre_la_cle(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.DoesNotContain(PartieReconnaissable, await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
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
            (Adresse, "get", "LireLEtatDeLIA", ["200"]),
            (Adresse, "put", "ChoisirLeFournisseurDIA", ["200", "400", "409", "413"]),
            (AdresseDeLaCle, "put", "EnregistrerLaCleDIA", ["200", "400", "409", "413"]),
            (AdresseDeLaCle, "delete", "RetirerLaCleDIA", ["200", "409"]),
            (AdresseDeLEssai, "post", "EssayerLaCleDIA", ["200", "400", "409"]),
        })
        {
            var route = chemins[chemin]![methode]!.AsObject();
            Assert.Equal(operation, route["operationId"]!.GetValue<string>());
            Assert.All(codes, code => Assert.NotNull(route["responses"]![code]));
        }
    }

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

    // Claude, et sa clé enregistrée ; le faux fournisseur répond à un essai.
    private static async Task<string> PreparerAsync(ApiLancee api)
    {
        api.FauxFournisseur.RepondreReussi();
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);
        return await InstantaneAsync(api);
    }

    // Ce qu'une requête refusée ne doit pas changer : l'état, le fichier des réglages, la clé chiffrée, et les essais partis.
    private static async Task<string> InstantaneAsync(ApiLancee api)
    {
        var dossier = new DossierDeLaBase(api.DossierDeDonnees);
        return string.Join(
            '\n',
            api.FauxFournisseur.Recues.Count.ToString(CultureInfo.InvariantCulture),
            (await api.EtatDeLIAAsync()).ToJsonString(),
            await File.ReadAllTextAsync(dossier.FichierDesReglages, TestContext.Current.CancellationToken),
            File.Exists(dossier.FichierDeLaCle) ? await File.ReadAllTextAsync(dossier.FichierDeLaCle, TestContext.Current.CancellationToken) : "(sans clé)");
    }
}
