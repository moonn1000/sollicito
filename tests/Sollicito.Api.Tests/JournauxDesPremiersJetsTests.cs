using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Relances;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Candidatures;
using Sollicito.Application.Relances;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;
using static Sollicito.Api.Tests.Outils.AppelsDesPremiersJets;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9 ; modèle de menaces, « Règle des journaux ») sur le premier jet d'une relance (#47) :
/// journaux au niveau Trace, une marque fictive dans le poste, l'entreprise, le nom et l'adresse du contact, la réponse de
/// l'IA et le texte relu, et la clé fictive ; sur tous les chemins, réussite, échecs et refus compris, puis le brouillon
/// relu pris par la fenêtre. Aucune entrée ne contient une marque ni la clé, et aucune réponse d'erreur non plus.
/// </summary>
public sealed class JournauxDesPremiersJetsTests
{
    private const string Marque = "Marque-Fictive-47";

    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    [Fact]
    public async Task Ni_la_cle_ni_les_donnees_ni_la_reponse_ni_le_texte_relu_n_apparaissent_dans_un_journal_ou_une_erreur()
    {
        var capture = new CaptureDesJournaux();
        var erreurs = new List<string>();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            new HorlogeDeTest().Utiliser(builder);
            ToutJournaliser(builder, capture);
        }))
        {
            var candidature = await api.EnregistrerAvecSuccesAsync($"Entreprise {Marque}", $"Poste {Marque}", "2026-11-10T09:00");
            var id = AppelsDuSuivi.IdDe(candidature);
            var contact = Id(await api.AjouterUnContactAvecSuccesAsync(Id(candidature["entreprise"]), $"Contact {Marque}", "marque-fictive-47@exemple.invalid"));
            var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(id, destinataire: contact)));

            // Sans IA prête, puis sans clé : refusés sans requête.
            await ErreurAsync(erreurs, api.DemanderUnPremierJetAsync(id, relance), HttpStatusCode.Conflict);
            await api.ChoisirLIAAvecSuccesAsync("Claude");
            await ErreurAsync(erreurs, api.DemanderUnPremierJetAsync(id, relance), HttpStatusCode.Conflict);
            await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

            // Réussi, avec une réponse marquée ; puis vide une fois nettoyée ; puis chaque sorte d'échec.
            api.FauxFournisseur.RepondreTexte($"Réponse de l'IA {Marque}.");
            var reussi = await api.PremierJetAsync(id, relance);
            Assert.True(reussi["reussi"]!.GetValue<bool>());
            api.FauxFournisseur.RepondreTexte(char.ConvertFromUtf32(0x200B));
            await api.PremierJetAsync(id, relance);
            api.FauxFournisseur.RepondreErreur(HttpStatusCode.Unauthorized);
            await api.PremierJetAsync(id, relance);
            api.FauxFournisseur.Repondre((_, _) => throw new HttpRequestException($"Faux réseau absent {Marque}."));
            await api.PremierJetAsync(id, relance);
            api.FauxFournisseur.RepondreTexte(new string('a', 1_501) + Marque);
            await api.PremierJetAsync(id, relance);

            // Les refus du premier jet : un corps marqué, une candidature inconnue, une autre relance.
            await ErreurAsync(erreurs, api.EcrireAsync(HttpMethod.Post, AdresseDuPremierJet(id, relance), $$"""{"{{Marque}}":"{{CleDeClaude}}"}"""), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.DemanderUnPremierJetAsync(id + 1_000, relance), HttpStatusCode.NotFound);
            await ErreurAsync(erreurs, api.DemanderUnPremierJetAsync(id, relance + 1_000), HttpStatusCode.Conflict);

            // Porte fermée, comme pendant une restauration : les deux routes répondent 503, sans requête.
            var porte = api.Services.GetRequiredService<PorteDeLaBase>();
            Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
            try
            {
                await ErreurAsync(erreurs, api.DemanderUnPremierJetAsync(id, relance), HttpStatusCode.ServiceUnavailable);
                await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id, relance, $"Texte relu {Marque}."), HttpStatusCode.ServiceUnavailable);
            }
            finally
            {
                porte.Ouvrir();
            }

            // Une demande en cours : une seconde est refusée sans requête.
            var entree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var relache = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            api.FauxFournisseur.Repondre(async (_, annulation) =>
            {
                entree.TrySetResult();
                await relache.Task.WaitAsync(annulation);
                throw new HttpRequestException($"Faux réseau absent {Marque}.");
            });
            var premiere = api.DemanderUnPremierJetAsync(id, relance);
            await entree.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await ErreurAsync(erreurs, api.DemanderUnPremierJetAsync(id, relance), HttpStatusCode.Conflict);
            relache.TrySetResult();
            (await premiere).Dispose();

            // Le dépôt : admis, puis refusé par sa règle, par la lecture du corps, par la relance.
            using (var depose = await api.DeposerLeBrouillonReluAsync(id, relance, $"Texte relu {Marque}."))
            {
                Assert.Equal(HttpStatusCode.NoContent, depose.StatusCode);
            }

            await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id, relance, $"Texte relu {Marque}" + (char)0x07), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id, relance, Marque + new string('a', 2_000)), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id, relance, new string('é', 1_900) + Marque), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id, relance, Marque + new string('a', 17 * 1_024)), HttpStatusCode.RequestEntityTooLarge);
            await ErreurAsync(erreurs, api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(id, relance), $$"""{"texte":"{{Marque}}","{{Marque}}":1}"""), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(id, relance), $$"""{"texte":["{{Marque}}"]}"""), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(id, relance), $$"""{"texte":"{{Marque}}"""), HttpStatusCode.BadRequest);
            await ErreurAsync(erreurs, api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(id, relance), $"Texte relu {Marque}", "text/plain"), HttpStatusCode.UnsupportedMediaType);
            await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id + 1_000, relance, $"Texte relu {Marque}."), HttpStatusCode.NotFound);
            await ErreurAsync(erreurs, api.DeposerLeBrouillonReluAsync(id, relance + 1_000, $"Texte relu {Marque}."), HttpStatusCode.Conflict);

            // La fenêtre prend le brouillon relu.
            var brouillon = await api.Services.GetRequiredService<BrouillonsDesRelances>().PreparerLeBrouillonReluAsync(relance, TestContext.Current.CancellationToken);
            Assert.Contains(Marque, brouillon!.Corps, StringComparison.Ordinal);
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu le niveau le plus bavard, et l'adaptateur a noté ses demandes.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Demande à l'IA : Claude a répondu", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Premier jet d'une relance refusé (aucun fournisseur en ligne).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Texte relu d'un brouillon refusé (lien trop long).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Premier jet d'une relance refusé (restauration en cours).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Premier jet d'une relance refusé (une demande en cours).", StringComparison.Ordinal));
        // Les consignes de Sollicito, que le fournisseur reçoit, ne s'écrivent dans aucun journal ni aucune erreur.
        var consignes = PremierJetDeRelance.Consignes[..40];
        Assert.DoesNotContain(entrees, entree => entree.Contains(consignes, StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Marque, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(PartieReconnaissable, StringComparison.Ordinal));
        Assert.All(erreurs, erreur =>
        {
            Assert.DoesNotContain(consignes, erreur, StringComparison.Ordinal);
            Assert.DoesNotContain(Marque, erreur, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PartieReconnaissable, erreur, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("POST", "premier-jet", null)]
    [InlineData("PUT", "brouillon-relu", """{"texte":"Texte relu."}""")]
    public async Task Une_exception_pendant_une_route_du_premier_jet_n_ecrit_que_son_type(string methode, string fin, string? corps)
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            ToutJournaliser(builder, capture);
            builder.ConfigureTestServices(services => services.AddScoped<IDepotDesCandidatures>(_ => new DepotEnPanne(Marque)));
        }))
        {
            await api.ChoisirLIAAvecSuccesAsync("Claude");
            await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

            using var reponse = await api.EcrireAsync(new HttpMethod(methode), $"/api/candidatures/1/relances/1/{fin}", corps);
            Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
            Assert.DoesNotContain(Marque, await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.OrdinalIgnoreCase);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Erreur inattendue pendant une requête (InvalidOperationException).", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Marque, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Panne simulée", StringComparison.Ordinal));
    }

    private static void ToutJournaliser(IWebHostBuilder builder, CaptureDesJournaux capture) =>
        builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LesPlusBavards))
            .ConfigureLogging(journaux => journaux.AddProvider(capture));

    // Une réponse d'erreur attendue, dont le corps est gardé pour y chercher les marques.
    private static async Task ErreurAsync(List<string> erreurs, Task<HttpResponseMessage> envoi, HttpStatusCode attendu)
    {
        using var reponse = await envoi;
        Assert.Equal(attendu, reponse.StatusCode);
        erreurs.Add(await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken) + reponse.Headers + reponse.Content.Headers);
    }
}
