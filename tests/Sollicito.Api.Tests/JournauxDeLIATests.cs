using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9, « Secrets et journaux ») sur le réglage de l'IA (#43) : journaux au niveau Trace,
/// une clé fictive reconnaissable sur chaque route et chaque chemin d'erreur, dans des dossiers qui imitent un compte
/// Windows. Aucune entrée ne contient la clé, ni sa partie reconnaissable, ni un chemin du dossier de données, ni le
/// message d'une erreur de DPAPI ou de Data Protection : seulement ce qui s'est passé.
/// </summary>
public sealed class JournauxDeLIATests : IDisposable
{
    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.DataProtection*", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    private readonly string _racine = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    // Un dossier de données dont le chemin imite celui d'un compte Windows.
    private string Dossier => Path.Combine(_racine, "Users", "Compte.Fictif-Dupont", "AppData", "Local", "Sollicito");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_racine, recursive: true);
        }
        catch (IOException)
        {
            // Le dossier temporaire reste : le système le videra.
        }
    }

    [Fact]
    public async Task Ni_la_cle_ni_un_chemin_n_apparaissent_dans_un_journal_sur_aucun_chemin()
    {
        Directory.CreateDirectory(Dossier);
        var capture = new CaptureDesJournaux();
        var base_ = new DossierDeLaBase(Dossier);
        await using (var api = await LancerSurAsync(Dossier, new HorlogeDeTest(), builder => ToutJournaliser(builder, capture)))
        {
            HttpStatusCode[] statuts =
            [
                await StatutAsync(api.ChoisirLIAAsync("Claude")),
                await StatutAsync(api.EnregistrerLaCleAsync(CleDeClaude)),
                await StatutAsync(api.EnregistrerLaCleAsync(AutreCleDeClaude)),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, Adresse, jeton: true)),

                // Des refus de la forme, qui portent la clé ou ce qui y ressemble.
                await StatutAsync(api.EnregistrerLaCleAsync(CleDOpenAI)),
                await StatutAsync(api.EnregistrerLaCleAsync(CleDeClaude + " " + PartieReconnaissable)),
                await StatutAsync(api.EnregistrerLaCleAsync("sk-ant-" + PartieReconnaissable + new string('a', 600))),

                // Des corps illisibles, qui portent la clé dans un champ inconnu, un type inattendu, un JSON mal formé.
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, AdresseDeLaCle, $$"""{"cle":"{{CleDeClaude}}","{{PartieReconnaissable}}":1}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, AdresseDeLaCle, $$"""{"cle":["{{CleDeClaude}}"]}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, AdresseDeLaCle, $$"""{"cle":"{{CleDeClaude}}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, Adresse, $$"""{"fournisseur":"{{CleDeClaude}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, AdresseDeLaCle, $$"""{"cle":"{{CleDeClaude}}{{new string('a', 5000)}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, AdresseDeLaCle, CleDeClaude, "text/plain")),

                // Une autre origine, qui porte la clé dans son corps.
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Put, AdresseDeLaCle, jeton: true, requete =>
                {
                    requete.Headers.Add("Origin", "http://exemple.invalid");
                    requete.Content = new StringContent($$"""{"cle":"{{CleDeClaude}}"}""", Encoding.UTF8, "application/json");
                })),
            ];

            // Une clé tenue par un autre programme : le changement et le retrait sont refusés.
            using (new FileStream(base_.FichierDeLaCle, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                statuts = [.. statuts, await StatutAsync(api.ChoisirLIAAsync("OpenAI")), await StatutAsync(api.RetirerLaCleAsync())];
            }

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.UnsupportedMediaType,
                    HttpStatusCode.Forbidden,
                    HttpStatusCode.Conflict, HttpStatusCode.Conflict,
                ],
                statuts);
        }

        // Un trousseau abîmé entre deux séances : la clé est illisible, puis le fournisseur change, ce qui l'efface.
        foreach (var fichier in Directory.GetFiles(base_.Trousseau, "*.xml"))
        {
            await File.WriteAllTextAsync(fichier, $"<key {PartieReconnaissable}", TestContext.Current.CancellationToken);
        }

        await using (var api = await LancerSurAsync(Dossier, new HorlogeDeTest(), builder => ToutJournaliser(builder, capture)))
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Conflict],
                [
                    await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, Adresse, jeton: true)),
                    await StatutAsync(api.ChoisirLIAAsync("OpenAI")),
                    await StatutAsync(api.EnregistrerLaCleAsync(CleDOpenAI)),
                    await StatutAsync(api.RetirerLaCleAsync()),
                    await StatutAsync(api.ChoisirLIAAsync("Aucun")),
                    await StatutAsync(api.EnregistrerLaCleAsync(CleDeClaude)),
                ]);
        }

        // Le trousseau où l'on ne peut pas écrire : un fichier à sa place.
        Directory.Delete(new DossierDeLaBase(Dossier).Trousseau, recursive: true);
        await File.WriteAllTextAsync(new DossierDeLaBase(Dossier).Trousseau, "pas un dossier", TestContext.Current.CancellationToken);
        await using (var api = await LancerSurAsync(Dossier, new HorlogeDeTest(), builder => ToutJournaliser(builder, capture)))
        {
            await api.ChoisirLIAAvecSuccesAsync("Claude");
            Assert.Equal(HttpStatusCode.Conflict, await StatutAsync(api.EnregistrerLaCleAsync(CleDeClaude)));
        }

        // Un fichier des réglages tenu par un autre programme : il se relit, mais le choix ne peut pas s'y écrire.
        await using (var api = await LancerSurAsync(Dossier, new HorlogeDeTest(), builder => ToutJournaliser(builder, capture)))
        {
            using (new FileStream(base_.FichierDesReglages, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(HttpStatusCode.Conflict, await StatutAsync(api.ChoisirLIAAsync("OpenAI")));
            }
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards, et chaque chemin a été parcouru.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        foreach (var temoin in new[]
        {
            "Fournisseur d'IA choisi : Claude.",
            "Clé d'IA enregistrée pour Claude.",
            "Clé d'IA remplacée pour Claude.",
            "Clé d'IA refusée (SansLePrefixeDeClaude).",
            "Clé d'IA refusée (CaracteresRefuses).",
            "Clé d'IA refusée (LongueurHorsBornes).",
            "Choix de l'IA refusé (clé non effacée).",
            "La clé d'IA n'a pas pu être retirée : son fichier n'a pas pu être effacé.",
            "Clé d'IA illisible (CryptographicException) : Sollicito continue sans IA.",
            "Clé d'IA effacée au changement de fournisseur.",
            "Clé d'IA retirée.",
            "Clé d'IA refusée (fournisseur sans clé).",
            "La clé d'IA n'a pas pu être chiffrée ni écrite (",
            "Le choix de l'IA n'a pas pu s'écrire (",
            "Requête refusée : corps illisible (400).",
        })
        {
            Assert.Contains(entrees, entree => entree.Contains(temoin, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(entrees, entree => entree.Contains(PartieReconnaissable, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("sk-ant-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("sk-proj-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Compte.Fictif-Dupont", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(_racine, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("cle-ia.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Microsoft.AspNetCore.DataProtection", StringComparison.Ordinal));
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
