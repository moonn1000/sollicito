using System.Net;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Une requête dont l'en-tête <c>Host</c> n'est pas l'adresse d'écoute est refusée (400), quelle que soit la
/// route, même avec le bon jeton (ADR-0001, D9). C'est la parade au rebinding DNS, et à la navigation vers
/// <c>localhost</c> qu'une fenêtre annule mais dont la requête part quand même (prototype de #9).
/// </summary>
public sealed class ControleDeLHoteTests(ApiLancee api) : IClassFixture<ApiLancee>
{
    public static TheoryData<string, string> HotesEtrangers()
    {
        string[] hotes =
        [
            "localhost:{port}",
            "LOCALHOST:{port}",
            "exemple.invalid",
            "exemple.invalid:{port}",
            "127.0.0.1",
            "127.0.0.1:1",
            "127.0.0.2:{port}",
            "[::1]:{port}",
            "2130706433:{port}",
            "0.0.0.0:{port}",
        ];
        string[] chemins = ["/", "/index.html", "/api/etat", "/api/openapi/v1.json"];

        var donnees = new TheoryData<string, string>();
        foreach (var hote in hotes)
        {
            foreach (var chemin in chemins)
            {
                donnees.Add(hote, chemin);
            }
        }

        return donnees;
    }

    [Theory]
    [MemberData(nameof(HotesEtrangers))]
    public async Task Un_Host_inattendu_est_refuse_meme_avec_le_jeton(string hote, string chemin)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: true, requete => requete.Headers.Host = api.Remplir(hote));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_ecriture_vers_un_Host_inattendu_est_refusee()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, "/api/etat", jeton: true, requete =>
        {
            requete.Headers.Host = "localhost:" + api.Port;
            requete.Headers.Add("Origin", "http://localhost:" + api.Port);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_Host_de_l_adresse_d_ecoute_passe()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.Host = "127.0.0.1:" + api.Port);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_requete_sans_Host_est_refusee()
    {
        // HTTP/1.0 n'oblige pas à envoyer Host.
        var ligne = await api.EnvoyerBrutAsync(api.Remplir("GET /api/etat HTTP/1.0\r\nSollicito-Jeton: {jeton}\r\n\r\n"));

        Assert.StartsWith("HTTP/1.1 400 ", ligne, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_adresse_absolue_vers_un_autre_hote_est_refusee()
    {
        var ligne = await api.EnvoyerBrutAsync(api.Remplir(
            "GET http://exemple.invalid/api/etat HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nSollicito-Jeton: {jeton}\r\nConnection: close\r\n\r\n"));

        Assert.StartsWith("HTTP/1.1 400 ", ligne, StringComparison.Ordinal);
    }
}
