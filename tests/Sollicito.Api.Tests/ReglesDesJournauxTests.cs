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
        // Chaque catégorie au niveau le plus bavard que la configuration permet : le test voit tout ce que
        // l'API et ASP.NET Core écriraient, sauf ce que la règle des journaux plafonne dans le code.
        var capture = new CaptureDesJournaux();
        string jeton;
        await using (var api = await ApiLancee.LancerAsync(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection([new("Logging:LogLevel:Default", "Trace")]))
            .ConfigureLogging(journaux => journaux.AddProvider(capture))))
        {
            jeton = api.Jeton;
            HttpStatusCode[] attendus =
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

                // Refusées : le jeton hors de son en-tête, la donnée en guise de jeton, de Host ou d'Origin.
                await StatutAsync(api, HttpMethod.Get, $"/api/etat?{JetonDeLancement.EnTete}={jeton}&nom={Donnee}", jeton: false),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add("Cookie", $"{JetonDeLancement.EnTete}={jeton}; nom={Donnee}")),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton)),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add(JetonDeLancement.EnTete, Donnee)),
                await StatutAsync(api, HttpMethod.Get, "/api/etat?nom=" + Donnee, jeton: true, requete => requete.Headers.Host = Donnee + ".invalid"),
                await StatutAsync(api, HttpMethod.Post, "/api/etat?nom=" + Donnee, jeton: true, requete => requete.Headers.Add("Origin", "http://" + Donnee + ".invalid")),
            ];

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.MethodNotAllowed, HttpStatusCode.OK,
                    HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                    HttpStatusCode.BadRequest, HttpStatusCode.Forbidden,
                ],
                attendus);
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
