using System.Net;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Chaque réponse, refus compris, porte une politique de sécurité du contenu stricte, envoyée en en-tête : elle
/// interdit les scripts, connexions, cadres et images venus d'ailleurs (ADR-0001, D9, « Fenêtre »).
/// </summary>
public sealed class EnTetesDeSecuriteTests(ApiAvecInterface lancement) : IClassFixture<ApiAvecInterface>
{
    private ApiLancee Api => lancement.Api;

    [Theory]
    [InlineData("GET", "/", false, null, null, HttpStatusCode.OK)]
    [InlineData("GET", "/assets/app.js", false, null, null, HttpStatusCode.OK)]
    [InlineData("GET", "/api/etat", true, null, null, HttpStatusCode.OK)]
    [InlineData("GET", "/api/inconnue", true, null, null, HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/etat", false, null, null, HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/", false, "localhost:{port}", null, HttpStatusCode.BadRequest)]
    [InlineData("POST", "/api/etat", true, null, "http://exemple.invalid", HttpStatusCode.Forbidden)]
    public async Task Chaque_reponse_porte_les_en_tetes_de_securite(
        string methode, string chemin, bool jeton, string? hote, string? origine, HttpStatusCode statut)
    {
        using var reponse = await Api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton, requete =>
        {
            if (hote is not null)
            {
                requete.Headers.Host = Api.Remplir(hote);
            }

            if (origine is not null)
            {
                requete.Headers.Add("Origin", origine);
            }
        });

        Assert.Equal(statut, reponse.StatusCode);
        VerifierLaPolitique(reponse);
        Assert.Equal("nosniff", Assert.Single(reponse.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(reponse.Headers.GetValues("Referrer-Policy")));
        Assert.True(reponse.Headers.CacheControl?.NoStore);
    }

    // La politique n'admet que l'origine de l'interface : ni source large, ni script en ligne ou évalué, ni adresse data: ou blob:.
    private static void VerifierLaPolitique(HttpResponseMessage reponse)
    {
        var directives = Assert.Single(reponse.Headers.GetValues("Content-Security-Policy"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' ', 2))
            .ToDictionary(parties => parties[0], parties => parties[1], StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["default-src"] = "'none'",
                ["script-src"] = "'self'",
                ["style-src"] = "'self'",
                ["img-src"] = "'self'",
                ["font-src"] = "'self'",
                ["connect-src"] = "'self'",
                ["frame-src"] = "'none'",
                ["worker-src"] = "'none'",
                ["base-uri"] = "'none'",
                ["form-action"] = "'none'",
                ["frame-ancestors"] = "'none'",
            },
            directives);
    }
}
