using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Relances;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Candidatures;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9 ; modèle de menaces, « Règle des journaux ») sur les contacts et les relances
/// (#14) : journaux au niveau Trace, une donnée fictive dans le nom et l'adresse d'un contact, dans un poste et dans
/// chaque corps refusé ; sur tous les chemins, refus compris, et le brouillon préparé pour la fenêtre. Aucune entrée ne
/// contient la donnée.
/// </summary>
public sealed class JournauxDesRelancesTests
{
    // Donnée personnelle fictive : elle est aussi dans l'adresse, que la recherche trouve sans tenir compte de la casse.
    private const string Donnee = "Dupont-Fictif";

    private const string Adresse = "dupont-fictif@exemple.invalid";

    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    [Fact]
    public async Task Ni_un_contact_ni_un_brouillon_n_apparait_dans_un_journal()
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            new HorlogeDeTest().Utiliser(builder);
            ToutJournaliser(builder, capture);
        }))
        {
            var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", $"Poste de {Donnee}", "2026-11-10T09:00");
            var id = AppelsDuSuivi.IdDe(candidature);
            var entreprise = Id(candidature["entreprise"]);
            var contact = Id(await api.AjouterUnContactAvecSuccesAsync(entreprise, $"Jeanne {Donnee}", Adresse));
            var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(id, destinataire: contact)));

            HttpStatusCode[] statuts =
            [
                // Contacts : lus, modifiés, refusés par les champs et par la lecture du corps, inconnus.
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, $"/api/entreprises/{Texte(entreprise)}/contacts", jeton: true)),
                await StatutAsync(api.ModifierLeContactAsync(contact, $"Jeanne {Donnee}", Adresse)),
                await StatutAsync(api.AjouterUnContactAsync(entreprise, $"{Donnee}\n", $"{Donnee}@exemple.invalid?cc={Donnee}@exemple.invalid")),
                await StatutAsync(api.AjouterUnContactAsync(entreprise, Donnee, new string('a', 250) + Adresse)),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, $"/api/entreprises/{Texte(entreprise)}/contacts", $$"""{"nom":"{{Donnee}}","{{Donnee}}":"x"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, $"/api/entreprises/{Texte(entreprise)}/contacts", $$"""{"nom":{{Donnee}}""")),
                await StatutAsync(api.AjouterUnContactAsync(999, Donnee, Adresse)),
                await StatutAsync(api.ModifierLeContactAsync(999, Donnee, Adresse)),

                // Relances : la fiche et le tableau de bord, qui relisent le poste et le contact ; des refus de chaque sorte.
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, AppelsDuSuivi.AdresseDeLaFiche(id), jeton: true)),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, "/api/relances", jeton: true)),
                await StatutAsync(api.PlanifierAsync(id, delai: 7)),
                await StatutAsync(api.ChangerLaRelanceAsync(id, relance, delai: 0, destinataire: 999)),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDesRelances(id), $$"""{"delai":7,"{{Donnee}}":1}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDesRelances(id), $$"""{"delai":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, "/api/reglages/relances", $$"""{"delaiParDefaut":"{{Donnee}}"}""")),
                await StatutAsync(api.ChangerLeDelaiParDefautAsync(400)),
                // Le réglage lu, puis changé avec succès (#93, point 16).
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, "/api/reglages/relances", jeton: true)),
                await StatutAsync(api.ChangerLeDelaiParDefautAsync(10)),
                await StatutAsync(api.ChangerLaRelanceAsync(id, relance, delai: 10, destinataire: contact)),
                await StatutAsync(api.MarquerFaiteAsync(id, relance)),
                await StatutAsync(api.AbandonnerAsync(id, relance)),
                await StatutAsync(api.PlanifierAsync(999)),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Post, AdresseDesRelances(id), jeton: true, requete =>
                    requete.Content = new StringContent($$"""{"delai":"{{Donnee}}"}""", Encoding.UTF8, "application/json"))),

                // Le contact supprimé, puis supprimé de nouveau.
                await StatutAsync(api.SupprimerLeContactAsync(contact)),
                await StatutAsync(api.SupprimerLeContactAsync(contact)),
            ];

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.NotFound,
                    HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.OK, HttpStatusCode.OK,
                    HttpStatusCode.OK, HttpStatusCode.OK,
                    HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.Forbidden,
                    HttpStatusCode.NoContent, HttpStatusCode.NotFound,
                ],
                statuts);

            // Le brouillon que la fenêtre ferait ouvrir : il porte la donnée, le journal non.
            var autre = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(id)));
            var brouillon = await api.Services.GetRequiredService<BrouillonsDesRelances>().PreparerAsync(autre, TestContext.Current.CancellationToken);
            Assert.Contains(Donnee, brouillon!.Objet, StringComparison.Ordinal);
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards, et la base a bien été écrite.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.EntityFrameworkCore.Database.Transaction ", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("GET", "/api/relances", null)]
    [InlineData("GET", "/api/entreprises/1/contacts", null)]
    [InlineData("POST", "/api/entreprises/1/contacts", $$"""{"nom":"{{Donnee}}","email":"{{Adresse}}"}""")]
    [InlineData("PUT", "/api/contacts/1", $$"""{"nom":"{{Donnee}}","email":"{{Adresse}}"}""")]
    [InlineData("DELETE", "/api/contacts/1", null)]
    [InlineData("POST", "/api/candidatures/1/relances", """{"delai":7}""")]
    [InlineData("POST", "/api/candidatures/1/relances/1/faite", null)]
    [InlineData("PUT", "/api/candidatures/1/relances/1", """{"delai":7}""")]
    [InlineData("POST", "/api/candidatures/1/relances/1/abandon", null)]
    public async Task Une_exception_pendant_une_route_des_relances_n_ecrit_que_son_type(string methode, string chemin, string? corps)
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            ToutJournaliser(builder, capture);
            builder.ConfigureTestServices(services => services.AddScoped<IDepotDesCandidatures>(_ => new DepotEnPanne(Donnee)));
        }))
        {
            using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), chemin, jeton: true, requete =>
            {
                requete.Headers.Add("Origin", api.Origine);
                if (corps is not null)
                {
                    requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
                }
            });
            Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Erreur inattendue pendant une requête (InvalidOperationException).", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Panne simulée", StringComparison.Ordinal));
    }

    private static void ToutJournaliser(IWebHostBuilder builder, CaptureDesJournaux capture) =>
        builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LesPlusBavards))
            .ConfigureLogging(journaux => journaux.AddProvider(capture));

    private static async Task<HttpStatusCode> StatutAsync(Task<HttpResponseMessage> envoi)
    {
        using var reponse = await envoi;
        return reponse.StatusCode;
    }
}
