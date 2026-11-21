using System.Globalization;
using System.Net;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les trois routes des sauvegardes (#26) passent par les protections de #10 (ADR-0001, D9 ; ADR-0003) : <c>Host</c>
/// (400), jeton (401), <c>Origin</c> (403), et figurent dans la documentation OpenAPI. Une requête refusée ne copie rien
/// et ne change pas de dossier.
/// </summary>
public sealed class ProtectionsDesSauvegardesTests : IAsyncLifetime
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", "/api/sauvegardes" },
        { "POST", "/api/sauvegardes" },
        { "POST", "/api/sauvegardes/dossier-par-defaut" },
    };

    public static TheoryData<string, string?> EcrituresSansLOrigineDeLInterface
    {
        get
        {
            var donnees = new TheoryData<string, string?>();
            foreach (var chemin in new[] { "/api/sauvegardes", "/api/sauvegardes/dossier-par-defaut" })
            {
                foreach (var origine in new[] { "http://exemple.invalid", "null", null })
                {
                    donnees.Add(chemin, origine);
                }
            }

            return donnees;
        }
    }

    private string ParDefaut => new DossierDeLaBase(_dossier).DossierDesSauvegardes;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_dossier);

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string chemin)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        var avant = await ApresLeLancementAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: false, requete => requete.Headers.Add("Origin", api.Origine));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(avant, Empreinte(api));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        var avant = await ApresLeLancementAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(avant, Empreinte(api));
    }

    [Theory]
    [MemberData(nameof(EcrituresSansLOrigineDeLInterface))]
    public async Task Une_ecriture_sans_l_origine_de_l_interface_est_refusee_meme_avec_le_jeton(string chemin, string? origine)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        var avant = await ApresLeLancementAsync(api);

        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, chemin, jeton: true, requete =>
        {
            if (origine is not null)
            {
                requete.Headers.Add("Origin", origine);
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Equal(avant, Empreinte(api));
    }

    [Fact]
    public async Task Une_lecture_d_une_autre_origine_est_refusee()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await ApresLeLancementAsync(api);

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/sauvegardes", jeton: true, requete => requete.Headers.Add("Origin", "http://exemple.invalid"));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Venues_de_l_interface_les_routes_repondent(string methode, string chemin)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await ApresLeLancementAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete => requete.Headers.Add("Origin", api.Origine));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!;
        Assert.Equal("LireLesSauvegardes", chemins["/api/sauvegardes"]!["get"]!["operationId"]!.GetValue<string>());
        Assert.Equal("SauvegarderMaintenant", chemins["/api/sauvegardes"]!["post"]!["operationId"]!.GetValue<string>());
        var retour = chemins["/api/sauvegardes/dossier-par-defaut"]!["post"]!.AsObject();
        Assert.Equal("RevenirAuDossierParDefaut", retour["operationId"]!.GetValue<string>());
        Assert.NotNull(retour["responses"]!["200"]);
        Assert.NotNull(retour["responses"]!["409"]);
    }

    // Après la copie du lancement : l'empreinte des copies du dossier par défaut, et le réglage absent.
    private async Task<string> ApresLeLancementAsync(ApiLancee api)
    {
        await api.AttendreLaPremiereVerificationAsync();
        return Empreinte(api);
    }

    // Les copies, leur date, et le dossier des sauvegardes : ce qu'une requête refusée ne doit pas changer.
    private string Empreinte(ApiLancee api) => string.Join(
        " | ",
        Fichiers(ParDefaut).Select(nom => $"{nom}@{File.GetLastWriteTimeUtc(Path.Combine(ParDefaut, nom)).Ticks}")
            .Append(api.Sauvegardes().Etat.Dossier)
            .Append(File.Exists(new DossierDeLaBase(_dossier).FichierDesReglages).ToString()));
}
