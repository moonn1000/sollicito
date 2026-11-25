using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDeLExtraction;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9) sur l'extraction d'une offre collée (#46, ADR-0018) : journaux au niveau Trace, une
/// marque reconnaissable dans le texte de l'offre, dans chaque valeur que l'IA propose et dans le nom d'une clé inconnue,
/// sur chaque chemin, admis comme refusé. Aucune entrée ne contient le texte, la réponse de l'IA, une valeur proposée, le
/// nom d'une clé inconnue, la clé d'API, les consignes ni l'adresse d'un fournisseur : seulement l'issue et ses motifs.
/// </summary>
public sealed class JournauxDeLExtractionTests : IDisposable
{
    private const string MarqueDuTexte = "SOLLICITO-TEXTE-46";

    private const string MarqueDeLaReponse = "SOLLICITO-REPONSE-46";

    private const string Texte = "Lumière Verte SA recrute un comptable à Liège. " + MarqueDuTexte;

    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
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
    public async Task L_extraction_n_ecrit_ni_le_texte_ni_la_reponse_ni_une_valeur_ni_la_cle_sur_aucun_chemin()
    {
        Directory.CreateDirectory(Dossier);
        var capture = new CaptureDesJournaux();
        await using (var api = await LancerSurAsync(Dossier, new HorlogeDeTest(), builder => ToutJournaliser(builder, capture)))
        {
            // Sans IA prête, puis Claude avec sa clé.
            Assert.Equal(HttpStatusCode.Conflict, await StatutAsync(api.ExtraireAsync(Texte)));
            await api.ChoisirLIAAvecSuccesAsync("Claude");
            await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

            // Proposée, avec une date hors bornes et une adresse absente du texte, écartées.
            api.FauxFournisseur.RepondreTexte(
                $$"""{"entreprise":"{{MarqueDeLaReponse}} SA","poste":"Comptable {{MarqueDeLaReponse}}","lieu":"Liège","publieeLe":"2030-01-01","adresse":"https://ailleurs.exemple/{{MarqueDeLaReponse}}"}""");
            Assert.Equal(HttpStatusCode.OK, await StatutAsync(api.ExtraireAsync(Texte)));

            // Hors schéma : une clé inconnue, qui porte la marque ; une adresse javascript:, qui la porte aussi.
            api.FauxFournisseur.RepondreTexte($$"""{"entreprise":null,"poste":null,"lieu":null,"publieeLe":null,"adresse":null,"{{MarqueDeLaReponse}}":1}""");
            Assert.Equal(HttpStatusCode.OK, await StatutAsync(api.ExtraireAsync(Texte)));
            api.FauxFournisseur.RepondreTexte(
                $$"""{"entreprise":null,"poste":null,"lieu":null,"publieeLe":null,"adresse":"javascript:alert('{{MarqueDeLaReponse}}')"}""");
            Assert.Equal(HttpStatusCode.OK, await StatutAsync(api.ExtraireAsync(Texte)));

            // Les échecs du port : une erreur dont le corps porte un message, puis un réseau absent dont l'erreur porte la marque.
            api.FauxFournisseur.RepondreErreur(HttpStatusCode.Unauthorized);
            Assert.Equal(HttpStatusCode.OK, await StatutAsync(api.ExtraireAsync(Texte)));
            api.FauxFournisseur.Repondre((_, _) => throw new HttpRequestException("Faux réseau absent " + MarqueDeLaReponse));
            Assert.Equal(HttpStatusCode.OK, await StatutAsync(api.ExtraireAsync(Texte)));

            // Les refus sans requête : un texte porteur d'un caractère refusé, un corps qui porte un champ inconnu.
            Assert.Equal(HttpStatusCode.BadRequest, await StatutAsync(api.ExtraireAsync(MarqueDuTexte + (char)0x202E)));
            Assert.Equal(
                HttpStatusCode.BadRequest,
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDeLExtraction, $$"""{"texte":"{{MarqueDuTexte}}","{{MarqueDuTexte}}":"{{CleDeClaude}}"}""")));

            // Une seconde demande pendant la première.
            var liberer = new TaskCompletionSource();
            var arrivee = new TaskCompletionSource();
            api.FauxFournisseur.Repondre(async (requete, annulation) =>
            {
                arrivee.TrySetResult();
                await liberer.Task.WaitAsync(annulation);
                return FauxFournisseurEnLigne.Json(
                    HttpStatusCode.OK,
                    Reponse(requete, """{"entreprise":null,"poste":null,"lieu":null,"publieeLe":null,"adresse":null}"""));
            });
            var premiere = api.ExtraireAsync(Texte);
            await arrivee.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, await StatutAsync(api.ExtraireAsync(Texte)));
            liberer.SetResult();
            Assert.Equal(HttpStatusCode.OK, await StatutAsync(premiere));
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        foreach (var temoin in new[]
        {
            "Extraction d'une offre refusée sans requête (aucune IA prête).",
            "Extraction d'une offre : 3 champ(s) proposé(s), 2 écarté(s).",
            "Extraction d'une offre : un champ écarté (PublieeLe, DateHorsBornes).",
            "Extraction d'une offre : un champ écarté (Adresse, AdresseAbsenteDuTexte).",
            "Extraction d'une offre : réponse hors schéma (CleInconnue, champ aucun).",
            "Extraction d'une offre : réponse hors schéma (ChampRefuse, champ Adresse).",
            "Extraction d'une offre : échec CleRefusee.",
            "Extraction d'une offre : échec Injoignable.",
            "Extraction d'une offre refusée sans requête (texte refusé).",
            "Extraction d'une offre refusée sans requête (une demande en cours).",
            "Extraction d'une offre : 0 champ(s) proposé(s), 0 écarté(s).",
            "Demande à l'IA : Claude, échec CleRefusee (statut 401) en ",
        })
        {
            Assert.Contains(entrees, entree => entree.Contains(temoin, StringComparison.Ordinal));
        }

        foreach (var interdit in new[]
        {
            MarqueDuTexte, MarqueDeLaReponse, "Lumière Verte", "Liège", "ailleurs.exemple", "javascript", PartieReconnaissable,
            "sk-ant-", FauxFournisseurEnLigne.MessageDuFournisseur, "Tu lis le texte d'une offre d'emploi", "api.anthropic.com",
            "x-api-key", "Bearer", "Compte.Fictif-Dupont", _racine, "System.Net.Http",
        })
        {
            Assert.DoesNotContain(entrees, entree => entree.Contains(interdit, StringComparison.OrdinalIgnoreCase));
        }
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
