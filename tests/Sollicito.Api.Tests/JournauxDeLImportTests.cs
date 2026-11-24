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
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDeLImport;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La règle des journaux (ADR-0001, D9 ; modèle de menaces, « Règle des journaux ») sur l'import d'un fichier CSV (#40,
/// ADR-0015) : journaux au niveau Trace, une donnée fictive dans chaque cellule et dans chaque fichier ou corps refusé ;
/// sur tous les chemins, refus compris. Aucune entrée ne contient la donnée, ni le fichier en base64, ni l'empreinte
/// d'une ligne, ni ses formes normalisées : seulement des comptes, et la sorte d'un refus.
/// </summary>
public sealed class JournauxDeLImportTests
{
    // Donnée personnelle fictive : elle est aussi dans l'adresse, que la recherche trouve sans tenir compte de la casse.
    private const string Donnee = "Dupont-Fictif";

    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(HorlogeDeTest.Reference.UtcDateTime);

    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics*", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Server.Kestrel.BadRequests*", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    // Un fichier qui passe par chaque sort : importée, doublon possible gardé et non gardé, en double, invalide, mal
    // formée, vide ; la donnée est dans chaque cellule, refusées comprises.
    private static readonly byte[] Fichier = Csv(
        "poste;entreprise;lieu;publieeLe;adresse;texte\r\n"
        + $"Poste de {Donnee};Entreprise {Donnee};Lieu {Donnee};15/11/2026;https://{Donnee}.exemple.invalid/1;\"Texte de {Donnee}\r\nDeuxième ligne de {Donnee}\"\r\n"
        + $"Poste de {Donnee};Entreprise {Donnee} SA;Autre lieu {Donnee};;;Autre texte de {Donnee}\r\n"
        + $"Poste de {Donnee};Entreprise {Donnee} SRL;Troisième lieu {Donnee};;;\r\n"
        + $"Poste de {Donnee};Entreprise {Donnee};Lieu {Donnee};15/11/2026;https://{Donnee}.exemple.invalid/1;\"Texte de {Donnee}\r\nDeuxième ligne de {Donnee}\"\r\n"
        + $"Poste de {Donnee};Entreprise {Donnee};Lieu {Donnee};{Donnee};javascript:{Donnee};\r\n"
        + $"{Donnee}\t{Donnee};{Donnee}\r\n"
        + ";;;;;\r\n");

    [Fact]
    public async Task Ni_le_fichier_ni_ses_cellules_n_apparaissent_dans_un_journal()
    {
        var capture = new CaptureDesJournaux();
        HttpStatusCode[] statuts;
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            new HorlogeDeTest().Utiliser(builder);
            ToutJournaliser(builder, capture);
        }))
        {
            statuts =
            [
                // Aperçu, puis import qui garde la ligne 3 et pas la ligne 4, puis le même fichier réimporté (règle 2).
                await StatutAsync(api.ExaminerAsync(Fichier)),
                await StatutAsync(api.ImporterAsync(Fichier, 3)),
                await StatutAsync(api.ImporterAsync(Fichier)),

                // Fichiers refusés : colonne inconnue, cellule trop longue, guillemet mal placé, Windows-1252, trop de lignes.
                await StatutAsync(api.ImporterAsync(Csv($"poste;{Donnee}\nComptable;{Donnee}\n"))),
                await StatutAsync(api.ImporterAsync(Csv($"poste;lieu\nComptable;{Donnee}{new string('a', 200)}\n"))),
                await StatutAsync(api.ExaminerAsync(Csv($"poste;lieu\nComptable;{Donnee} \"{Donnee}\"\n"))),
                await StatutAsync(api.ExaminerAsync([.. Encoding.ASCII.GetBytes($"poste\n{Donnee}"), 0xE9, (byte)'\n'])),
                await StatutAsync(api.ExaminerAsync(Csv("poste\n" + string.Concat(Enumerable.Repeat($"{Donnee}\n", 1_001))))),

                // Lignes gardées mal désignées ; corps illisibles : champ inconnu, répété, type inattendu, base64 illisible ;
                // corps trop gros.
                await StatutAsync(api.ImporterAsync(Fichier, 99)),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, RouteDeLImport, $$"""{"contenu":"{{Convert.ToBase64String(Fichier)}}","{{Donnee}}":"{{Donnee}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, RouteDeLApercu, $$"""{"contenu":"{{Convert.ToBase64String(Fichier)}}","contenu":"{{Convert.ToBase64String(Fichier)}}"}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, RouteDeLImport, $$"""{"contenu":"{{Convert.ToBase64String(Fichier)}}","garder":["{{Donnee}}"]}""")),
                await StatutAsync(api.EcrireAsync(HttpMethod.Post, RouteDeLApercu, $$"""{"contenu":"{{Donnee}}"}""")),
                await StatutAsync(api.ImporterAsync([.. Encoding.UTF8.GetBytes(Donnee), .. new byte[2_400_000]])),

                // Une requête d'une autre origine.
                await StatutAsync(api.EnvoyerAsync(HttpMethod.Post, RouteDeLImport, jeton: true, requete =>
                {
                    requete.Headers.Add("Origin", "http://exemple.invalid");
                    requete.Content = new StringContent(Corps(Fichier), Encoding.UTF8, "application/json");
                })),
            ];

            // Porte fermée, pendant une restauration : refusé par 503.
            var porte = api.Services.GetRequiredService<PorteDeLaBase>();
            Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, await StatutAsync(api.ImporterAsync(Fichier)));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, await StatutAsync(api.ExaminerAsync(Fichier)));
            porte.Ouvrir();

            // Sans copie possible de la base avant l'import, refusé par 409 : le dossier des sauvegardes par défaut est
            // remplacé par un fichier.
            var parDefaut = api.Services.GetRequiredService<DossierDeLaBase>().DossierDesSauvegardes;
            await AppelsDesSauvegardes.DeplacerOuRetirerAsync(parDefaut, parDefaut + "-de-cote");
            await File.WriteAllBytesAsync(parDefaut, [], TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, await StatutAsync(api.ImporterAsync(Fichier)));
            await AppelsDesSauvegardes.DeplacerOuRetirerAsync(parDefaut, null);
            await AppelsDesSauvegardes.DeplacerOuRetirerAsync(parDefaut + "-de-cote", parDefaut);
        }

        Assert.Equal(
            [
                HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK,
                HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest,
                HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.Forbidden,
            ],
            statuts);

        var entrees = capture.Entrees.ToArray();
        // Témoins : la capture a reçu les niveaux les plus bavards ; l'écriture de l'import et ses refus se sont notés, en
        // comptes et en sortes seulement.
        Assert.Contains(entrees, entree => entree.StartsWith(nameof(LogLevel.Trace) + " ", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Import d'un fichier CSV : écriture commencée (5 offres reçues).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Import d'un fichier CSV : écriture validée (2 offres importées).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Import d'un fichier CSV : copie de la base faite avant lui.", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Import refusé : la copie de la base avant lui n'a pas pu se faire (IOException).", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Fichier CSV examiné : 6 lignes d'offres, 1 à importer, 2 doublons possibles, 1 écartées, 2 invalides, 1 lignes vides.", StringComparison.Ordinal));
        foreach (var sorte in new[] { "EnTete", "Borne", "Structure", "Encodage", "Lignes" })
        {
            Assert.Contains(entrees, entree => entree.Contains($"Fichier CSV refusé ({sorte}).", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Convert.ToBase64String(Fichier)[..40], StringComparison.Ordinal));
        foreach (var forme in FormesTirees())
        {
            Assert.DoesNotContain(entrees, entree => entree.Contains(forme, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("/api/offres/import/apercu")]
    [InlineData("/api/offres/import")]
    public async Task Une_exception_pendant_l_import_n_ecrit_que_son_type(string route)
    {
        var capture = new CaptureDesJournaux();
        await using (var api = await ApiLancee.LancerAsync(builder =>
        {
            ToutJournaliser(builder, capture);
            builder.ConfigureTestServices(services => services.AddScoped<IDepotDesOffres>(_ => new DepotDesOffresEnPanne()));
        }))
        {
            using var reponse = await api.EcrireAsync(HttpMethod.Post, route, Corps(Fichier));
            Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        }

        var entrees = capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Erreur inattendue pendant une requête (InvalidOperationException).", StringComparison.Ordinal));
        Assert.DoesNotContain(entrees, entree => entree.Contains(Donnee, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entrees, entree => entree.Contains("Panne simulée", StringComparison.Ordinal));
    }

    // Ce que le cœur tire des lignes admises du fichier : leur empreinte, leur poste normalisé et la clé de leur entreprise.
    private static IEnumerable<string> FormesTirees()
    {
        var fichier = Assert.IsType<LectureDuFichier.Lu>(LectureDUnFichierCsv.Lire(Fichier)).Fichier;
        foreach (var offre in new ConnecteurDuFichierCsv().Recevoir(fichier))
        {
            if (LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out var admise, out _))
            {
                yield return admise.Empreinte;
                yield return admise.PosteNormalise;
                yield return admise.CleDeLEntreprise!;
            }
        }

        yield return Domain.Offres.PosteNormalise.De(Donnee);
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
