using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Sollicito.Api.Securite;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9 ; absorbe #35) : ni le jeton, ni une donnée personnelle, et aucun
/// journal gardé là où d'autres comptes du poste pourraient le lire.
/// </summary>
public sealed class ReglesDesJournauxTests
{
    // Donnée personnelle fictive, envoyée partout où une requête peut porter du contenu.
    private const string Donnee = "Dupont-Fictif";

    [Fact]
    public async Task Les_journaux_ne_vont_qu_a_la_console()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        var fournisseurs = api.Services.GetServices<ILoggerProvider>().Select(fournisseur => fournisseur.GetType());

        Assert.Equal([typeof(ConsoleLoggerProvider)], fournisseurs);
    }

    [Fact]
    public async Task Ni_le_jeton_ni_une_donnee_de_la_requete_n_apparait_dans_un_journal()
    {
        // Chaque catégorie au niveau le plus bavard, y compris par des réglages qui visent les catégories que la
        // règle fait taire : un joker, et un réglage propre au fournisseur. Le test voit tout ce que l'API et
        // ASP.NET Core écriraient.
        var capture = new CaptureDesJournaux();
        KeyValuePair<string, string?>[] niveaux =
        [
            new("Logging:LogLevel:Default", "Trace"),
            new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
            new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
            new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
        ];
        string jeton;
        await using (var api = await ApiLancee.LancerAsync(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(niveaux))
            .ConfigureLogging(journaux => journaux.AddProvider(capture))))
        {
            jeton = api.Jeton;
            HttpStatusCode[] statuts =
            [
                // L'interface : le jeton dans son en-tête, la donnée dans la chaîne de requête, un en-tête, un cookie, un corps.
                await StatutAsync(api, HttpMethod.Get, "/api/etat?nom=" + Donnee, jeton: true, requete =>
                {
                    requete.Headers.Add("X-Note", Donnee);
                    requete.Headers.Add("Cookie", "nom=" + Donnee);
                }),
                await StatutAsync(api, HttpMethod.Post, "/api/etat?nom=" + Donnee, jeton: true, requete =>
                {
                    requete.Headers.Add("Origin", api.Origine);
                    requete.Content = new StringContent($$"""{"nom":"{{Donnee}}"}""", MediaTypeHeaderValue.Parse("application/json"));
                }),
                await StatutAsync(api, HttpMethod.Get, "/api/openapi/v1.json", jeton: true),

                // Acceptées, mais la donnée ou le jeton dans le chemin : une route inconnue, un document OpenAPI inconnu.
                await StatutAsync(api, HttpMethod.Get, $"/api/{Donnee}", jeton: true),
                await StatutAsync(api, HttpMethod.Get, $"/api/x/{jeton}", jeton: true),
                await StatutAsync(api, HttpMethod.Get, $"/api/openapi/{Donnee}.json", jeton: true),

                // Refusées : le jeton hors de son en-tête, la donnée en guise de jeton, de chemin, de Host ou d'Origin.
                await StatutAsync(api, HttpMethod.Get, $"/api/etat?{JetonDeLancement.EnTete}={jeton}&nom={Donnee}", jeton: false),
                await StatutAsync(api, HttpMethod.Get, $"/api/{jeton}/etat", jeton: false),
                await StatutAsync(api, HttpMethod.Get, $"/api/{Donnee}/etat", jeton: false),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add("Cookie", $"{JetonDeLancement.EnTete}={jeton}; nom={Donnee}")),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton)),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add(JetonDeLancement.EnTete, Donnee)),
                await StatutAsync(api, HttpMethod.Get, "/api/etat?nom=" + Donnee, jeton: true, requete => requete.Headers.Host = Donnee + ".invalid"),
                await StatutAsync(api, HttpMethod.Post, "/api/etat?nom=" + Donnee, jeton: true, requete => requete.Headers.Add("Origin", "http://" + Donnee + ".invalid")),
            ];

            // Mal formées, que Kestrel rejette lui-même : ligne de requête (méthode invalide), ligne d'en-tête sans
            // deux-points, Host.
            string[] lignesDEtat =
            [
                await api.EnvoyerBrutAsync(api.Remplir($"G@T /api/etat?{JetonDeLancement.EnTete}={{jeton}}&nom={Donnee} HTTP/1.1\r\nHost: 127.0.0.1:{{port}}\r\n\r\n")),
                await api.EnvoyerBrutAsync(api.Remplir($"GET /api/etat HTTP/1.1\r\nHost: 127.0.0.1:{{port}}\r\n{JetonDeLancement.EnTete} {{jeton}} {Donnee}\r\n\r\n")),
                await api.EnvoyerBrutAsync(api.Remplir($"GET /api/etat HTTP/1.1\r\nHost: 127.0.0.1:{{port}}/{{jeton}}/{Donnee}\r\n\r\n")),
            ];

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.MethodNotAllowed, HttpStatusCode.OK,
                    HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
                    HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                    HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                    HttpStatusCode.BadRequest, HttpStatusCode.Forbidden,
                ],
                statuts);
            Assert.All(lignesDEtat, ligne => Assert.StartsWith("HTTP/1.1 400 ", ligne, StringComparison.Ordinal));
        }

        // L'API est arrêtée : toutes les entrées sont écrites. La capture a bien tout reçu, jusqu'aux niveaux les plus bavards.
        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Requête refusée : jeton absent ou faux.", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.AspNetCore.", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));

        Assert.DoesNotContain(entrees, entree => entree.Contains(jeton, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<HttpStatusCode> StatutAsync(
        ApiLancee api,
        HttpMethod methode,
        string chemin,
        bool jeton,
        Action<HttpRequestMessage>? preparer = null)
    {
        using var reponse = await api.EnvoyerAsync(methode, chemin, jeton, preparer);
        return reponse.StatusCode;
    }
}
