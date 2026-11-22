using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Offres;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9 ; modèle de menaces, « Règle des journaux ») sur les offres collées (#36) :
/// journaux au niveau Trace, une donnée fictive dans le texte, l'entreprise, le poste, le lieu et l'adresse d'une
/// offre, et dans chaque corps refusé ; sur tous les chemins, refus compris. Aucune entrée ne contient la donnée, ni
/// l'empreinte de l'offre.
/// </summary>
public sealed class JournauxDesOffresTests
{
    // Donnée personnelle fictive : elle est aussi dans l'adresse, que la recherche trouve sans tenir compte de la casse.
    private const string Donnee = "Dupont-Fictif";

    private const string Adresse = "https://dupont-fictif.exemple.invalid/offre";

    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    [Fact]
    public async Task Ni_le_texte_ni_les_champs_d_une_offre_n_apparaissent_dans_un_journal()
    {
        var capture = new CaptureDesJournaux();
        string empreinte;
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            new HorlogeDeTest().Utiliser(builder);
            ToutJournaliser(builder, capture);
        }))
        {
            var texte = $"Offre de {Donnee}.\nDeuxième ligne de {Donnee}.";
            var offre = Id(await api.CollerAvecSuccesAsync(texte, $"Poste de {Donnee}", entreprise: $"Entreprise {Donnee}", lieu: $"Lieu {Donnee}", adresse: Adresse));
            empreinte = Domain.Offres.EmpreinteDUneOffre.De([texte]);

            HttpStatusCode[] statuts =
            [
                // Lue ; recollée (règle 2) ; doublon possible (règle 3), refusé puis gardé ; corrigée.
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, AdresseDeLOffre(offre), jeton: true)),
                await StatutAsync(api.CollerAsync(texte, $"Autre poste de {Donnee}")),
                await StatutAsync(api.CollerAsync($"Autre offre de {Donnee}.", $"Poste de {Donnee}", entreprise: $"Entreprise {Donnee}")),
                await StatutAsync(api.CollerAsync($"Autre offre de {Donnee}.", $"Poste de {Donnee}", entreprise: $"Entreprise {Donnee}", garderLesDeux: true)),
                await StatutAsync(api.CorrigerLOffreAsync(offre, $"Poste de {Donnee}", entreprise: $"Entreprise {Donnee}", lieu: $"Autre lieu {Donnee}", adresse: Adresse)),
                await StatutAsync(api.CorrigerLOffreAsync(offre, $"Poste corrigé de {Donnee}", entreprise: $"Entreprise {Donnee}")),

                // Refus de chaque champ : texte trop long, caractère refusé, poste, entreprise, lieu, date, adresse.
                await StatutAsync(api.CollerAsync(Donnee + new string('a', 50_000), "Comptable")),
                await StatutAsync(api.CollerAsync($"{Donnee}\u0007", "Comptable")),
                await StatutAsync(api.CollerAsync(Donnee, $"{Donnee}\n{Donnee}")),
                await StatutAsync(api.CollerAsync(Donnee, "Comptable", entreprise: $"{Donnee}\t{Donnee}")),
                await StatutAsync(api.CollerAsync(Donnee, "Comptable", lieu: $"{Donnee}\t{Donnee}")),
                await StatutAsync(api.CollerAsync(Donnee, "Comptable", publieeLe: Donnee)),
                await StatutAsync(api.CollerAsync(Donnee, "Comptable", adresse: $"javascript:{Donnee}")),
                await StatutAsync(api.CollerAsync(Donnee, "Comptable", adresse: $"https://{Donnee}:secret@exemple.invalid/")),

                // Corps illisibles : champ inconnu, champ répété, type inattendu, JSON mal formé, trop gros.
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, Route, $$"""{"texte":"{{Donnee}}","poste":"Comptable","{{Donnee}}":"x"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, Route, $$"""{"texte":"{{Donnee}}","poste":"{{Donnee}}","poste":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, Route, $$"""{"texte":"{{Donnee}}","poste":"Comptable","garderLesDeux":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, Route, $$"""{"texte":{{Donnee}}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Put, AdresseDeLOffre(offre), $$"""{"poste":"Comptable","texte":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, Route, Corps(Donnee + new string('a', 1_100_000), "Comptable"))),

                // Offre inconnue, à la lecture et à la correction ; une requête d'une autre origine.
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, AdresseDeLOffre(999), jeton: true)),
                await StatutAsync(api.CorrigerLOffreAsync(999, $"Poste de {Donnee}")),
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Post, Route, jeton: true, requete =>
                {
                    requete.Headers.Add("Origin", "http://exemple.invalid");
                    requete.Content = new StringContent(Corps(Donnee, Donnee), Encoding.UTF8, "application/json");
                })),
            ];

            // La liste (#38), puis « Postuler » : accepté, déjà postulé, offre anonyme, date refusée, offre inconnue, et
            // ses corps illisibles (champ inconnu, répété, répété à la casse près, type inattendu, trop gros).
            var anonyme = Id(await api.CollerAvecSuccesAsync($"Offre anonyme de {Donnee}.", $"Poste anonyme de {Donnee}", lieu: $"Lieu {Donnee}"));
            HttpStatusCode[] statutsDePostuler =
            [
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, Route, jeton: true)),
                await StatutAsync(api.PostulerAsync(offre, "2026-11-18T10:00")),
                await StatutAsync(api.PostulerAsync(offre, "2026-11-18T10:00")),
                await StatutAsync(api.PostulerAsync(anonyme, "2026-11-18T10:00")),
                await StatutAsync(api.PostulerAsync(999, Donnee)),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(anonyme), $$"""{"envoyeeLe":"2026-11-18T10:00","entreprise":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(anonyme), $$"""{"envoyeeLe":"{{Donnee}}","envoyeeLe":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(anonyme), $$"""{"envoyeeLe":"{{Donnee}}","EnvoyeeLe":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(anonyme), $$"""{"envoyeeLe":["{{Donnee}}"]}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(anonyme), CorpsDePostuler(Donnee + new string('a', 20_000)))),
            ];
            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.NotFound,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge,
                ],
                statutsDePostuler);

            // Une date refusée, sur une offre qui a son entreprise et pas de candidature.
            var libre = Id(await api.CollerAvecSuccesAsync($"Offre libre de {Donnee}.", $"Poste libre de {Donnee}", entreprise: $"Entreprise {Donnee}"));
            Assert.Equal(HttpStatusCode.BadRequest, await StatutAsync(api.PostulerAsync(libre, Donnee)));

            // Porte fermée, pendant une restauration : refusé par 503, pour coller comme pour la liste et « Postuler ».
            var porte = api.Services.GetRequiredService<PorteDeLaBase>();
            Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
            var pendantLaRestauration = await StatutAsync(api.CollerAsync($"Pendant de {Donnee}.", $"Poste de {Donnee}"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, await StatutAsync(api.EnvoyerAsync(HttpMethod.Get, Route, jeton: true)));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, await StatutAsync(api.PostulerAsync(libre, "2026-11-18T10:00")));
            porte.Ouvrir();

            Assert.Equal(
                [
                    HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.OK,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                    HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge,
                    HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Forbidden,
                ],
                statuts);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, pendantLaRestauration);
        }

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards, et la base a bien été écrite.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Debug) + " Microsoft.EntityFrameworkCore.Database.Transaction ", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(empreinte, StringComparison.OrdinalIgnoreCase));
        // Ni sous ses formes normalisées (suites de #36, #98) : le poste normalisé et la clé de l'entreprise, que la base
        // garde, et la donnée sans sa ponctuation, d'où elles viennent.
        foreach (var forme in FormesNormalisees)
        {
            Assert.DoesNotContain(entrees, entree => entree.Contains(forme, StringComparison.OrdinalIgnoreCase));
        }
    }

    // La donnée, telle que la normalisation du poste et la clé d'une entreprise l'écrivent.
    private static readonly string[] FormesNormalisees =
    [
        Domain.Offres.PosteNormalise.De(Donnee),
        Domain.Offres.PosteNormalise.De($"Poste de {Donnee}"),
        Domain.Entreprises.CleDEntreprise.De($"Entreprise {Donnee}"),
    ];

    [Theory]
    [InlineData("POST", "/api/offres", $$"""{"texte":"{{Donnee}}","poste":"Comptable"}""")]
    [InlineData("GET", "/api/offres", null)]
    [InlineData("GET", "/api/offres/1", null)]
    [InlineData("PUT", "/api/offres/1", $$"""{"poste":"{{Donnee}}"}""")]
    [InlineData("POST", "/api/offres/1/candidature", $$"""{"envoyeeLe":"{{Donnee}}"}""")]
    public async Task Une_exception_pendant_une_route_des_offres_n_ecrit_que_son_type(string methode, string chemin, string? corps)
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            ToutJournaliser(builder, capture);
            // « Postuler » écrit par le dépôt des candidatures (#38) : les deux sont en panne.
            builder.ConfigureTestServices(services => services
                .AddScoped<IDepotDesOffres>(_ => new DepotDesOffresEnPanne())
                .AddScoped<Application.Candidatures.IDepotDesCandidatures>(_ => new DepotEnPanne(Donnee)));
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

    // Un dépôt dont chaque appel échoue, avec un message qui cite la donnée : seul le type de l'exception doit paraître.
    private sealed class DepotDesOffresEnPanne : IDepotDesOffres
    {
        public Task<IEcritureDesOffres> CommencerUneEcritureAsync(CancellationToken annulation) =>
            throw new InvalidOperationException($"Panne simulée : {Donnee}");

        public Task<FicheDUneOffre?> LireAsync(int id, CancellationToken annulation) =>
            throw new InvalidOperationException($"Panne simulée : {Donnee}");

        public Task<IReadOnlyList<OffreListee>> ListerAsync(CancellationToken annulation) =>
            throw new InvalidOperationException($"Panne simulée : {Donnee}");
    }
}
