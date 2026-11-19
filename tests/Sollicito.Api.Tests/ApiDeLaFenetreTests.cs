using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sollicito.Api.Securite;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'API que construit la fenêtre hôte (<see cref="ApiLocale.ConstruirePourLaFenetre"/>) : elle sert l'interface,
/// garde ses contrôles, et rien de ce qui entoure le lancement ne la fait passer en développement, où la page
/// d'exception afficherait les en-têtes d'une requête, jeton compris.
/// </summary>
[Collection(nameof(VariablesDEnvironnement))]
public sealed class ApiDeLaFenetreTests
{
    [Fact]
    public async Task Elle_sert_l_interface_et_garde_ses_controles()
    {
        using var interfaceDeTest = new InterfaceDeTest();
        var dossier = Directory.CreateTempSubdirectory("sollicito-donnees-").FullName;
        try
        {
            await using var app = ApiLocale.ConstruirePourLaFenetre(interfaceDeTest.Fichiers, dossier);
            await app.StartAsync(TestContext.Current.CancellationToken);
            var adresse = app.Services.GetRequiredService<AdresseDEcoute>();
            var jeton = app.Services.GetRequiredService<JetonDeLancement>().Valeur();
            using var client = new HttpClient { BaseAddress = new Uri(adresse.Origine) };
            var annulation = TestContext.Current.CancellationToken;

            using var page = await client.GetAsync(new Uri("/", UriKind.Relative), annulation);
            using var sansJeton = await client.GetAsync(new Uri("/api/etat", UriKind.Relative), annulation);
            using var requete = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/etat", UriKind.Relative));
            requete.Headers.Add(JetonDeLancement.EnTete, jeton);
            using var avecJeton = await client.SendAsync(requete, annulation);
            using var autreHote = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
            autreHote.Headers.Host = "localhost:" + new Uri(adresse.Origine).Port;
            using var refusee = await client.SendAsync(autreHote, annulation);

            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.BadRequest],
                [page.StatusCode, sansJeton.StatusCode, avecJeton.StatusCode, refusee.StatusCode]);
            Assert.Equal(InterfaceDeTest.Page, await page.Content.ReadAsStringAsync(annulation));
            Assert.Equal(dossier, app.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Ni_l_environnement_du_poste_ni_un_fichier_de_configuration_ne_la_reglent()
    {
        using var interfaceDeTest = new InterfaceDeTest();
        var dossier = Directory.CreateTempSubdirectory("sollicito-donnees-").FullName;
        foreach (var fichier in new[] { "appsettings.json", "appsettings.Development.json", "appsettings.Production.json" })
        {
            await File.WriteAllTextAsync(Path.Combine(dossier, fichier), """{"SondeDuTest":"lue"}""", TestContext.Current.CancellationToken);
        }

        string?[] avant = [Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")];
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
        try
        {
            await using var app = ApiLocale.ConstruirePourLaFenetre(interfaceDeTest.Fichiers, dossier);

            Assert.Equal(Environments.Production, app.Environment.EnvironmentName);
            Assert.Null(app.Configuration["SondeDuTest"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", avant[0]);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", avant[1]);
            Directory.Delete(dossier, recursive: true);
        }
    }
}

/// <summary>Les tests qui changent les variables d'environnement du processus ne tournent jamais en même temps qu'un autre.</summary>
[CollectionDefinition(nameof(VariablesDEnvironnement), DisableParallelization = true)]
public sealed class VariablesDEnvironnement;
