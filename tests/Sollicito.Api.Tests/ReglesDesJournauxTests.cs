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
            // La candidature enregistrée, la donnée dans son entreprise et son poste : les listes la relisent, la fiche la
            // lit et l'écrit. Les identifiants viennent des réponses : une candidature de plus en tête du test, ou une
            // ligne amorcée par une migration, ne les décale pas (#82, point 13).
            var id = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Poste de {Donnee}", "2026-11-02T10:00"));
            var fiche = AppelsDuSuivi.AdresseDeLaFiche(id);
            var inconnue = AppelsDuSuivi.AdresseDeLaFiche(id + 1000);
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

                // Les candidatures : la donnée dans l'entreprise et le poste enregistrés plus haut, relus par les deux listes.
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

                // La fiche de la candidature enregistrée (#12) : son statut change, puis la donnée part dans la note d'une
                // étape admise, relue par la fiche, et dans un statut, une étape et des corps refusés ; une correction
                // refusée, puis admise ; une candidature inconnue.
                await StatutAsync(api, HttpMethod.Get, fiche, jeton: true),
                await EcrireAsync(api, fiche + "/statut", """{"statut":"Entretien"}"""),
                await EcrireAsync(api, fiche + "/statut", $$"""{"statut":"{{Donnee}}"}"""),
                await EcrireAsync(api, fiche + "/statut", """{"statut":"Envoyee"}"""),
                await EcrireAsync(api, fiche + "/etapes", $$"""{"type":"Entretien","survenuLe":"2026-11-10T10:00","note":"Entretien avec {{Donnee}}"}"""),
                await EcrireAsync(api, fiche + "/etapes", $$"""{"type":"{{Donnee}}","survenuLe":"{{Donnee}}","note":"{{new string('x', 2001)}}{{Donnee}}"}"""),
                await EcrireAsync(api, fiche + "/etapes", $$"""{"note":{{Donnee}}"""),
                await EcrireAsync(api, fiche + "/etapes", $$"""{"{{Donnee}}":"x"}"""),
                await StatutAsync(api, HttpMethod.Get, fiche, jeton: true),
                await EcrireAsync(api, fiche + "/changements/" + (await ChangementACorrigerAsync(api, id) + 1000) + "/correction", corps: null),
                await EcrireAsync(api, fiche + "/changements/" + await ChangementACorrigerAsync(api, id) + "/correction", corps: null),
                await EcrireAsync(api, inconnue + "/statut", """{"statut":"Entretien"}"""),
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
                    HttpStatusCode.OK, HttpStatusCode.OK,
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
    public async Task Modifier_supprimer_ou_corriger_une_etape_n_ecrit_ni_l_entreprise_ni_le_poste_ni_la_note_meme_en_erreur()
    {
        // Les trois routes de #25, sur tous leurs chemins : admis, refusés par la validation, par l'état de l'étape
        // (409), par la lecture du corps, par son type ou par l'origine, candidature introuvable. Les identifiants
        // viennent des réponses.
        var capture = new CaptureDesJournaux();
        string jeton;
        string racineDuContenu;
        string dossierDeDonnees;
        await using (var api = await ApiLancee.LancerAsync(builder => ToutJournaliser(builder, capture)))
        {
            jeton = api.Jeton;
            racineDuContenu = api.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath.TrimEnd(Path.DirectorySeparatorChar);
            dossierDeDonnees = api.DossierDeDonnees;
            var id = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Poste de {Donnee}", "2026-11-02T10:00"));
            var etape = Assert.Single((await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null))["historique"]!.AsArray())!["id"]!.GetValue<int>();
            var supprimee = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Autre poste de {Donnee}", "2026-11-03T10:00"));
            var inconnue = supprimee + 1000;

            HttpStatusCode[] statuts =
            [
                await StatutAsync(api.ModifierAsync(id, $"{Donnee} Belgique", $"Poste de {Donnee} senior", "2026-11-02T10:00")),
                await StatutAsync(api.ModifierAsync(id, $"{Donnee}\t", new string('x', 201) + Donnee, Donnee)),
                await StatutAsync(api.ModifierAsync(id, $"{Donnee} SA", $"Poste de {Donnee}", "2026-11-10T10:01")),
                // Un nom de même clé qu'une entreprise enregistrée, écrit autrement, sans l'accord de le corriger (409).
                await StatutAsync(api.ModifierAsync(id, $"{Donnee.ToUpperInvariant()} S.A.", $"Poste de {Donnee}", "2026-11-02T10:00")),
                // Puis avec l'accord : l'entreprise est renommée, la donnée dans l'ancien nom comme dans le nouveau.
                await StatutAsync(api.ModifierAsync(id, $"{Donnee.ToUpperInvariant()} S.A.", $"Poste de {Donnee}", "2026-11-02T10:00", corrigerLeNom: true)),
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
                    HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.OK,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.UnsupportedMediaType, HttpStatusCode.Forbidden, HttpStatusCode.NotFound,
                    HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.NoContent, HttpStatusCode.NotFound,
                ],
                statuts);

            // La correction d'une étape : admise, la note porte la donnée ; puis, sur l'étape corrigée et sur sa
            // correction, en vigueur, chacun de ses refus.
            var corrigee = await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Entretien", "2026-11-10T10:00", $"Entretien avec {Donnee}");
            var enVigueur = corrigee["historique"]!.AsArray().Single(evenement => evenement!["corrige"]?.GetValue<int>() == etape)!["id"]!.GetValue<int>();
            HttpStatusCode[] statutsDesCorrections =
            [
                await StatutAsync(api.CorrigerUneEtapeAsync(id, etape, "Entretien", "2026-11-10T10:00", $"Encore {Donnee}")),
                await StatutAsync(api.CorrigerUneEtapeAsync(id, enVigueur, Donnee, Donnee, new string('x', 2001) + Donnee)),
                await StatutAsync(api.EnvoyerALaCorrectionDEtapeAsync(id, enVigueur, $$"""{"note":{{Donnee}}""")),
                await StatutAsync(api.EnvoyerALaCorrectionDEtapeAsync(id, enVigueur, $$"""{"{{Donnee}}":"x"}""")),
                await StatutAsync(api.EnvoyerALaCorrectionDEtapeAsync(id, enVigueur, $$"""{"note":"{{Donnee}}"}""", "text/" + Donnee)),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Post, AppelsDeLaModification.AdresseDeLaCorrection(id, enVigueur), jeton: true, requete =>
                    requete.Content = new StringContent($$"""{"note":"{{Donnee}}"}""", System.Text.Encoding.UTF8, "application/json"))),
                await StatutAsync(api.CorrigerUneEtapeAsync(inconnue, enVigueur, "Appel", "2026-11-10T10:00", $"Note de {Donnee}")),
            ];

            Assert.Equal(
                [
                    HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.UnsupportedMediaType, HttpStatusCode.Forbidden, HttpStatusCode.NotFound,
                ],
                statutsDesCorrections);
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards, et la base a bien été écrite.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.EntityFrameworkCore.Database.Transaction ", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(jeton, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        // Aucun chemin du poste, comme pour les routes d'enregistrement : la racine du contenu, et le dossier de la base,
        // que Entity Framework Core écrirait si on lui rendait la parole (#85, point 4).
        Assert.DoesNotContain(entrees, entree => entree.Contains(racineDuContenu, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(dossierDeDonnees, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("GET", "/api/candidatures/1")]
    [InlineData("POST", "/api/candidatures/1/statut")]
    [InlineData("POST", "/api/candidatures/1/etapes")]
    [InlineData("POST", "/api/candidatures/1/changements/1/correction")]
    [InlineData("PUT", "/api/candidatures/1")]
    [InlineData("DELETE", "/api/candidatures/1")]
    [InlineData("POST", "/api/candidatures/1/etapes/1/correction")]
    public async Task Une_exception_pendant_une_lecture_une_ecriture_ou_une_suppression_de_la_fiche_n_ecrit_que_son_type(string methode, string chemin)
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            ToutJournaliser(builder, capture);
            builder.ConfigureTestServices(services => services.AddScoped<IDepotDesCandidatures>(_ => new DepotEnPanne(Donnee)));
        }))
        {
            // Le dépôt échoue dès son premier appel, la lecture de la fiche ou le début de l'écriture : l'identifiant ne
            // désigne aucune candidature de la base.
            using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
            {
                requete.Headers.Add("Origin", api.Origine);
                var corps = (methode, chemin) switch
                {
                    ("PUT", _) => $$"""{"entreprise":"{{Donnee}} SA","poste":"Poste de {{Donnee}}","envoyeeLe":"2026-11-02T10:00"}""",
                    ("POST", _) when chemin.EndsWith("/statut", StringComparison.Ordinal) => """{"statut":"Entretien"}""",
                    ("POST", _) when chemin.EndsWith("/etapes", StringComparison.Ordinal) || chemin.Contains("/etapes/", StringComparison.Ordinal) =>
                        $$"""{"type":"Appel","survenuLe":"2026-11-10T10:00","note":"Note de {{Donnee}}"}""",
                    _ => null,
                };
                if (corps is not null)
                {
                    requete.Content = new StringContent(corps, System.Text.Encoding.UTF8, "application/json");
                }
            });
            Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Erreur inattendue pendant une requête (InvalidOperationException).", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Panne simulée", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Les_refus_des_routes_de_la_fiche_n_ecrivent_ni_la_donnee_ni_le_jeton_ni_un_chemin()
    {
        var capture = new CaptureDesJournaux();
        string jeton;
        string racineDuContenu;
        string dossierDeDonnees;
        await using (var api = await ApiLancee.LancerAsync(builder => ToutJournaliser(builder, capture)))
        {
            jeton = api.Jeton;
            racineDuContenu = api.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath.TrimEnd(Path.DirectorySeparatorChar);
            dossierDeDonnees = api.DossierDeDonnees;
            var id = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Poste de {Donnee}", "2026-11-02T10:00"));
            await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
            var changement = await ChangementACorrigerAsync(api, id);
            var fiche = AppelsDuSuivi.AdresseDeLaFiche(id);
            var inconnue = AppelsDuSuivi.AdresseDeLaFiche(id + 1000);
            var etape = """{"type":"Appel","survenuLe":"2026-11-10T10:00"}""";
            var origineRefusee = "http://" + Donnee + ".invalid";

            // Sur les routes de la fiche (#82, point 13) : une origine refusée, un type de corps qui porte la donnée, un
            // type inattendu dans un champ, une candidature inconnue.
            HttpStatusCode[] statuts =
            [
                await EcrireDepuisAsync(api, fiche + "/statut", """{"statut":"Refusee"}""", origineRefusee),
                await EcrireDepuisAsync(api, fiche + "/etapes", etape, origineRefusee),
                await EcrireDepuisAsync(api, fiche + "/changements/" + changement + "/correction", corps: null, origineRefusee),
                await EcrireDepuisAsync(api, fiche + "/statut", """{"statut":"Refusee"}""", api.Origine, "text/" + Donnee),
                await EcrireDepuisAsync(api, fiche + "/etapes", etape, api.Origine, "text/" + Donnee),
                await EcrireDepuisAsync(api, fiche + "/statut", $$"""{"statut":["{{Donnee}}"]}""", api.Origine),
                await EcrireDepuisAsync(api, fiche + "/etapes", $$"""{"type":["{{Donnee}}"]}""", api.Origine),
                await StatutAsync(api, HttpMethod.Get, inconnue, jeton: true),
                await EcrireDepuisAsync(api, inconnue + "/statut", """{"statut":"Refusee"}""", api.Origine),
                await EcrireDepuisAsync(api, inconnue + "/etapes", $$"""{"type":"Appel","survenuLe":"2026-11-10T10:00","note":"Note de {{Donnee}}"}""", api.Origine),
                await EcrireDepuisAsync(api, inconnue + "/changements/" + changement + "/correction", corps: null, api.Origine),
            ];

            Assert.Equal(
                [
                    HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden,
                    HttpStatusCode.UnsupportedMediaType, HttpStatusCode.UnsupportedMediaType,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
                ],
                statuts);
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards, et le refus de l'origine a bien été écrit.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Requête refusée : en-tête Origin absent ou étranger.", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(jeton, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(racineDuContenu, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(dossierDeDonnees, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Un_echec_de_la_base_pendant_une_ecriture_de_la_fiche_n_ecrit_que_le_type_de_l_erreur()
    {
        // Le message que la base donne à ses refus : ni une donnée de la requête, ni un chemin. L'API n'en écrit rien.
        const string Panne = "Panne-simulee-de-la-base";
        const string ErreurInattendue = "Error Sollicito.Api.Erreurs.ErreursInattendues : Erreur inattendue pendant une requête";
        var capture = new CaptureDesJournaux();
        string jeton;
        string racineDuContenu;
        string dossierDeDonnees;
        await using (var api = await ApiLancee.LancerAsync(builder => ToutJournaliser(builder, capture)))
        {
            jeton = api.Jeton;
            racineDuContenu = api.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath.TrimEnd(Path.DirectorySeparatorChar);
            dossierDeDonnees = api.DossierDeDonnees;
            var id = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync($"{Donnee} SA", $"Poste de {Donnee}", "2026-11-02T10:00"));
            await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
            var historique = (await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null))["historique"]!.AsArray();
            var etape = Assert.Single(historique, evenement => evenement!["sorte"]!.GetValue<string>() == "etape")!["id"]!.GetValue<int>();
            var changement = await ChangementACorrigerAsync(api, id);

            // Une insertion, une modification et une suppression que la base refuse : l'échec de `SaveChangesAsync`
            // (statut, étape, corrections, modification) et celui de `ExecuteDeleteAsync` (suppression).
            await api.ExecuterAsync($"CREATE TRIGGER panne_des_evenements BEFORE INSERT ON EvenementsDeCandidature BEGIN SELECT RAISE(ABORT, '{Panne}'); END");
            await api.ExecuterAsync($"CREATE TRIGGER panne_des_modifications BEFORE UPDATE ON Candidatures BEGIN SELECT RAISE(ABORT, '{Panne}'); END");
            await api.ExecuterAsync($"CREATE TRIGGER panne_des_suppressions BEFORE DELETE ON Candidatures BEGIN SELECT RAISE(ABORT, '{Panne}'); END");
            HttpStatusCode[] statuts;
            try
            {
                statuts =
                [
                    await StatutAsync(api.ChangerLeStatutAsync(id, "Refusee")),
                    await StatutAsync(api.NoterUneEtapeAsync(id, "Appel", "2026-11-12T10:00", $"Note de {Donnee}")),
                    await StatutAsync(api.CorrigerAsync(id, changement)),
                    await StatutAsync(api.CorrigerUneEtapeAsync(id, etape, "Entretien", "2026-11-10T10:00", $"Note de {Donnee}")),
                    await StatutAsync(api.ModifierAsync(id, $"{Donnee} Belgique", $"Poste de {Donnee} senior", "2026-11-02T10:00")),
                    await StatutAsync(api.SupprimerAsync(id)),
                ];
            }
            finally
            {
                // La base de test ne garde aucun déclencheur, même si une requête lève.
                await api.ExecuterAsync("DROP TRIGGER panne_des_evenements");
                await api.ExecuterAsync("DROP TRIGGER panne_des_modifications");
                await api.ExecuterAsync("DROP TRIGGER panne_des_suppressions");
            }

            Assert.All(statuts, statut => Assert.Equal(HttpStatusCode.InternalServerError, statut));
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : les écritures ont bien échoué dans la base, sur les deux chemins, et la base a écrit son message.
        Assert.Contains(entrees, entree => entree.StartsWith(ErreurInattendue + " (DbUpdateException).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(ErreurInattendue + " (SqliteException).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith("Error Microsoft.EntityFrameworkCore.Update : ", StringComparison.Ordinal) && entree.Contains(Panne, StringComparison.Ordinal));
        // L'API n'écrit que le type de l'erreur : jamais son message, que la base a pu rédiger avec ce qu'elle refuse.
        Assert.DoesNotContain(entrees, entree => entree.StartsWith("Error Sollicito.Api.Erreurs.ErreursInattendues", StringComparison.Ordinal) && entree.Contains(Panne, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(jeton, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(racineDuContenu, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(dossierDeDonnees, StringComparison.OrdinalIgnoreCase));
    }

    // Le changement de statut que la fiche propose de corriger : son identifiant vient de la réponse, jamais d'une base neuve.
    private static async Task<int> ChangementACorrigerAsync(ApiLancee api, int id) =>
        (await api.FicheAsync(id))["correction"]!["changement"]!.GetValue<int>();

    // Une écriture de la fiche, venue de l'origine donnée, avec le type de corps donné.
    private static Task<HttpStatusCode> EcrireDepuisAsync(ApiLancee api, string chemin, string? corps, string origine, string typeDuCorps = "application/json") =>
        StatutAsync(api, HttpMethod.Post, chemin, jeton: true, requete =>
        {
            requete.Headers.Add("Origin", origine);
            if (corps is not null)
            {
                requete.Content = new StringContent(corps, System.Text.Encoding.UTF8, typeDuCorps);
            }
        });

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
