using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Sollicito.Api.Securite;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Candidatures;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9 ; absorbe #35) : ni le jeton, ni une donnée personnelle, et aucun
/// journal gardé là où d'autres comptes du poste pourraient le lire.
/// </summary>
public sealed class ReglesDesJournauxTests
{
    // Donnée personnelle fictive, envoyée partout où une requête peut porter du contenu.
    private const string Donnee = "Dupont-Fictif";

    // Chaque catégorie au niveau le plus bavard, y compris par des réglages qui visent les catégories que la règle fait
    // taire : un joker, et un réglage propre au fournisseur. Le test voit tout ce que l'API et ASP.NET Core écriraient.
    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.StaticFiles*", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

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
        var capture = new CaptureDesJournaux();
        string jeton;
        string racineDuContenu;
        string dossierDeDonnees;
        using var interfaceDeTest = new InterfaceDeTest();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            interfaceDeTest.Servir(builder);
            ToutJournaliser(builder, capture);
        }))
        {
            jeton = api.Jeton;
            racineDuContenu = api.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath.TrimEnd(Path.DirectorySeparatorChar);
            dossierDeDonnees = api.DossierDeDonnees;
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

                // Les fichiers de l'interface, servis sans jeton : la donnée et le jeton dans la chaîne de requête.
                await StatutAsync(api, HttpMethod.Get, $"/index.html?nom={Donnee}&{JetonDeLancement.EnTete}={jeton}", jeton: false),
                await StatutAsync(api, HttpMethod.Get, $"/assets/app.js?nom={Donnee}", jeton: false),

                // Acceptées, mais la donnée ou le jeton dans le chemin : une route inconnue, un document OpenAPI inconnu.
                await StatutAsync(api, HttpMethod.Get, $"/api/{Donnee}", jeton: true),
                await StatutAsync(api, HttpMethod.Get, $"/api/x/{jeton}", jeton: true),
                await StatutAsync(api, HttpMethod.Get, $"/api/openapi/{Donnee}.json", jeton: true),

                // Cherchés parmi les fichiers de l'interface, absents, puis refusés : la donnée ou le jeton dans le chemin.
                await StatutAsync(api, HttpMethod.Get, $"/{Donnee}.js", jeton: false),
                await StatutAsync(api, HttpMethod.Get, $"/assets/{jeton}.js", jeton: false),

                // Refusées : le jeton hors de son en-tête, la donnée en guise de jeton, de chemin, de Host ou d'Origin.
                await StatutAsync(api, HttpMethod.Get, $"/api/etat?{JetonDeLancement.EnTete}={jeton}&nom={Donnee}", jeton: false),
                await StatutAsync(api, HttpMethod.Get, $"/api/{jeton}/etat", jeton: false),
                await StatutAsync(api, HttpMethod.Get, $"/api/{Donnee}/etat", jeton: false),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add("Cookie", $"{JetonDeLancement.EnTete}={jeton}; nom={Donnee}")),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jeton)),
                await StatutAsync(api, HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add(JetonDeLancement.EnTete, Donnee)),
                await StatutAsync(api, HttpMethod.Get, "/api/etat?nom=" + Donnee, jeton: true, requete => requete.Headers.Host = Donnee + ".invalid"),
                await StatutAsync(api, HttpMethod.Post, "/api/etat?nom=" + Donnee, jeton: true, requete => requete.Headers.Add("Origin", "http://" + Donnee + ".invalid")),

                // Les candidatures : la donnée dans l'entreprise et le poste enregistrés, puis relus par les deux listes.
                await EnregistrerAsync(api, $$"""{"entreprise":"{{Donnee}} SA","poste":"Poste de {{Donnee}}","envoyeeLe":"2026-11-02T10:00"}"""),
                await StatutAsync(api, HttpMethod.Get, "/api/candidatures", jeton: true),
                await StatutAsync(api, HttpMethod.Get, "/api/entreprises", jeton: true),
                // Refusées par la validation, par la lecture du corps (JSON mal formé, champ inconnu, type inattendu),
                // par le type du corps, et par le contrôle de l'origine.
                await EnregistrerAsync(api, $$"""{"entreprise":"{{Donnee}}\t","poste":"{{new string('x', 201)}}{{Donnee}}","envoyeeLe":"{{Donnee}}"}"""),
                await EnregistrerAsync(api, $$"""{"entreprise":{{Donnee}}"""),
                await EnregistrerAsync(api, $$"""{"{{Donnee}}":"x"}"""),
                await EnregistrerAsync(api, $$"""{"poste":["{{Donnee}}"]}"""),
                await EnregistrerAsync(api, $$"""{"poste":"{{Donnee}}"}""", "text/" + Donnee),
                await StatutAsync(api, HttpMethod.Post, "/api/candidatures", jeton: true, requete =>
                    requete.Content = new StringContent($$"""{"poste":"{{Donnee}}"}""", System.Text.Encoding.UTF8, "application/json")),

                // La fiche de la candidature enregistrée (#12), la première de la base : son statut change, puis la
                // donnée part dans la note d'une étape admise, relue par la fiche, et dans un statut, une étape et
                // des corps refusés ; une correction refusée, puis admise ; une candidature inconnue.
                await StatutAsync(api, HttpMethod.Get, "/api/candidatures/1", jeton: true),
                await EcrireAsync(api, "/api/candidatures/1/statut", """{"statut":"Entretien"}"""),
                await EcrireAsync(api, "/api/candidatures/1/statut", $$"""{"statut":"{{Donnee}}"}"""),
                await EcrireAsync(api, "/api/candidatures/1/statut", """{"statut":"Envoyee"}"""),
                await EcrireAsync(api, "/api/candidatures/1/etapes", $$"""{"type":"Entretien","survenuLe":"2026-11-10T10:00","note":"Entretien avec {{Donnee}}"}"""),
                await EcrireAsync(api, "/api/candidatures/1/etapes", $$"""{"type":"{{Donnee}}","survenuLe":"{{Donnee}}","note":"{{new string('x', 2001)}}{{Donnee}}"}"""),
                await EcrireAsync(api, "/api/candidatures/1/etapes", $$"""{"note":{{Donnee}}"""),
                await EcrireAsync(api, "/api/candidatures/1/etapes", $$"""{"{{Donnee}}":"x"}"""),
                await StatutAsync(api, HttpMethod.Get, "/api/candidatures/1", jeton: true),
                await EcrireAsync(api, "/api/candidatures/1/changements/999/correction", corps: null),
                await EcrireAsync(api, "/api/candidatures/1/changements/1/correction", corps: null),
                await EcrireAsync(api, "/api/candidatures/999/statut", """{"statut":"Entretien"}"""),
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
                    HttpStatusCode.OK, HttpStatusCode.OK,
                    HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
                    HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                    HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                    HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                    HttpStatusCode.BadRequest, HttpStatusCode.Forbidden,
                    HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.UnsupportedMediaType, HttpStatusCode.Forbidden,
                    HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict,
                    HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.NotFound,
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
        // Aucun chemin du poste : « Content root path » écrirait celui du projet, Entity Framework Core celui de la
        // base, donc tous deux le nom du compte Windows.
        Assert.DoesNotContain(entrees, entree => entree.Contains(racineDuContenu, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(dossierDeDonnees, StringComparison.OrdinalIgnoreCase));
        // Témoin : la base a bien été lue et écrite pendant le test, et Entity Framework Core écrit dans la capture.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.EntityFrameworkCore.Database.Transaction ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_exception_pendant_une_requete_n_ecrit_que_son_type()
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder => builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection([new("Logging:LogLevel:Default", "Trace")]))
            .ConfigureLogging(journaux => journaux.AddProvider(capture))
            .ConfigureTestServices(services => services.AddScoped<IDepotDesCandidatures>(_ => new DepotEnPanne(Donnee)))))
        {
            using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/candidatures", jeton: true);
            Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Erreur inattendue pendant une requête (InvalidOperationException).", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Panne simulée", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Modifier_ou_supprimer_une_candidature_n_ecrit_ni_l_entreprise_ni_le_poste_meme_en_erreur()
    {
        // Les deux routes de #25, sur tous leurs chemins : admis, refusés par la validation, par la lecture du corps,
        // par son type ou par l'origine, candidature introuvable. Les identifiants viennent des réponses.
        var capture = new CaptureDesJournaux();
        string jeton;
        await using (var api = await ApiLancee.LancerAsync(builder => ToutJournaliser(builder, capture)))
        {
            jeton = api.Jeton;
            var id = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Poste de {Donnee}", "2026-11-02T10:00"));
            await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null);
            var supprimee = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Autre poste de {Donnee}", "2026-11-03T10:00"));
            var inconnue = supprimee + 1000;

            HttpStatusCode[] statuts =
            [
                await StatutAsync(api.ModifierAsync(id, $"{Donnee} Belgique", $"Poste de {Donnee} senior", "2026-11-02T10:00")),
                await StatutAsync(api.ModifierAsync(id, $"{Donnee}\t", new string('x', 201) + Donnee, Donnee)),
                await StatutAsync(api.ModifierAsync(id, $"{Donnee} SA", $"Poste de {Donnee}", "2026-11-10T10:01")),
                await StatutAsync(api.EnvoyerALaModificationAsync(id, $$"""{"entreprise":{{Donnee}}""")),
                await StatutAsync(api.EnvoyerALaModificationAsync(id, $$"""{"{{Donnee}}":"x"}""")),
                await StatutAsync(api.EnvoyerALaModificationAsync(id, $$"""{"entreprise":"{{Donnee}}","poste":"{{Donnee}}","envoyeeLe":"2026-11-02T10:00","statut":"{{Donnee}}"}""")),
                await StatutAsync(api.EnvoyerALaModificationAsync(id, $$"""{"poste":["{{Donnee}}"]}""")),
                await StatutAsync(api.EnvoyerALaModificationAsync(id, $$"""{"poste":"{{Donnee}}"}""", "text/" + Donnee)),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Put, AppelsDuSuivi.AdresseDeLaFiche(id), jeton: true, requete =>
                    requete.Content = new StringContent($$"""{"poste":"{{Donnee}}"}""", System.Text.Encoding.UTF8, "application/json"))),
                await StatutAsync(api.ModifierAsync(inconnue, $"{Donnee} SA", $"Poste de {Donnee}", "2026-11-02T10:00")),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Delete, AppelsDuSuivi.AdresseDeLaFiche(supprimee), jeton: true)),
                await StatutAsync(api.SupprimerAsync(inconnue)),
                await StatutAsync(api.SupprimerAsync(supprimee)),
                await StatutAsync(api.SupprimerAsync(supprimee)),
            ];

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.UnsupportedMediaType, HttpStatusCode.Forbidden, HttpStatusCode.NotFound,
                    HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.NotFound,
                ],
                statuts);
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards, et la base a bien été écrite.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.EntityFrameworkCore.Database.Transaction ", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(jeton, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Une_exception_pendant_une_modification_ou_une_suppression_n_ecrit_que_son_type(string methode)
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            ToutJournaliser(builder, capture);
            builder.ConfigureTestServices(services => services.AddScoped<IDepotDesCandidatures>(_ => new DepotEnPanne(Donnee)));
        }))
        {
            // Le dépôt échoue dès le début de l'écriture : l'identifiant ne désigne aucune candidature de la base.
            using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), "/api/candidatures/1", jeton: true, requete =>
            {
                requete.Headers.Add("Origin", api.Origine);
                if (methode == "PUT")
                {
                    requete.Content = new StringContent(
                        $$"""{"entreprise":"{{Donnee}} SA","poste":"Poste de {{Donnee}}","envoyeeLe":"2026-11-02T10:00"}""",
                        System.Text.Encoding.UTF8,
                        "application/json");
                }
            });
            Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Erreur inattendue pendant une requête (InvalidOperationException).", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Panne simulée", StringComparison.Ordinal));
    }

    // Chaque catégorie au niveau le plus bavard, réglages des catégories que la règle fait taire compris, vers la capture.
    private static void ToutJournaliser(IWebHostBuilder builder, CaptureDesJournaux capture) =>
        builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LesPlusBavards))
            .ConfigureLogging(journaux => journaux.AddProvider(capture));

    private static async Task<HttpStatusCode> StatutAsync(Task<HttpResponseMessage> envoi)
    {
        using var reponse = await envoi;
        return reponse.StatusCode;
    }

    // Un enregistrement de candidature, venu de l'interface, avec le corps donné.
    private static async Task<HttpStatusCode> EnregistrerAsync(ApiLancee api, string corps, string typeDuCorps = "application/json")
    {
        using var reponse = await api.EnvoyerCorpsAsync(corps, typeDuCorps);
        return reponse.StatusCode;
    }

    // Une écriture de la fiche, venue de l'interface, avec le corps donné ou sans corps.
    private static Task<HttpStatusCode> EcrireAsync(ApiLancee api, string chemin, string? corps) =>
        StatutAsync(api, HttpMethod.Post, chemin, jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            if (corps is not null)
            {
                requete.Content = new StringContent(corps, System.Text.Encoding.UTF8, "application/json");
            }
        });

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
