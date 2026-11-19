using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sollicito.Api.Securite;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Candidatures;

namespace Sollicito.Api.Tests;

/// <summary>
/// En développement, ASP.NET Core place sa page d'exception en tête du chemin des requêtes : elle afficherait les
/// en-têtes de la requête, jeton compris, et le message de l'exception. Aucune exception ne l'atteint : l'API répond
/// elle-même, par un problème sans détail, et en garde les en-têtes de sécurité. Les tests tournent en développement,
/// l'environnement par défaut des tests, puis en production.
/// </summary>
public sealed class PageDExceptionTests
{
    // Donnée fictive, reprise dans le message de l'exception que lève le dépôt en panne.
    private const string Donnee = "Dupont-Fictif";

    public static TheoryData<string, string?> EnvironnementsEtAcceptations()
    {
        var donnees = new TheoryData<string, string?>();
        foreach (var environnement in new[] { Environments.Development, Environments.Production })
        {
            foreach (var acceptation in new[] { "text/html", "application/json", "text/plain", null })
            {
                donnees.Add(environnement, acceptation);
            }
        }

        return donnees;
    }

    [Theory]
    [MemberData(nameof(EnvironnementsEtAcceptations))]
    public async Task Une_exception_d_une_route_ne_montre_ni_le_jeton_ni_les_en_tetes_ni_l_exception(string environnement, string? acceptation)
    {
        await using var api = await ApiLancee.LancerAsync(builder =>
        {
            builder.UseEnvironment(environnement);
            builder.ConfigureTestServices(services => services.AddScoped<IDepotDesCandidatures>(_ => new DepotEnPanne(Donnee)));
        });
        Assert.Equal(environnement, api.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/candidatures", jeton: true, requete => Accepter(requete, acceptation));

        Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        await VerifierQuAucunDetailNePasseAsync(api, reponse, "L'API locale a rencontré une erreur inattendue.");
    }

    [Theory]
    [MemberData(nameof(EnvironnementsEtAcceptations))]
    public async Task Un_corps_illisible_ne_montre_ni_le_jeton_ni_les_en_tetes_ni_l_exception(string environnement, string? acceptation)
    {
        await using var api = await ApiLancee.LancerAsync(builder => builder.UseEnvironment(environnement));

        // En développement, un corps illisible lève une exception (ThrowOnBadRequest) : c'est le premier chemin qui
        // mènerait à la page d'exception.
        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, "/api/candidatures", jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Content = new StringContent($$"""{"entreprise":{{Donnee}}""", System.Text.Encoding.UTF8, "application/json");
            Accepter(requete, acceptation);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        await VerifierQuAucunDetailNePasseAsync(api, reponse, "La requête est illisible.");
    }

    private static void Accepter(HttpRequestMessage requete, string? acceptation)
    {
        if (acceptation is not null)
        {
            requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptation));
        }
    }

    private static async Task VerifierQuAucunDetailNePasseAsync(ApiLancee api, HttpResponseMessage reponse, string titre)
    {
        var corps = await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Un problème sans détail, jamais la page HTML ni sa version texte.
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(titre, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.DoesNotContain(api.Jeton, corps, StringComparison.Ordinal);
        Assert.DoesNotContain(JetonDeLancement.EnTete, corps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Donnee, corps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", corps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", corps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sollicito.", corps, StringComparison.Ordinal);
        Assert.DoesNotContain(api.Jeton, reponse.Headers.ToString(), StringComparison.Ordinal);
        // La réponse garde ses en-têtes de sécurité.
        Assert.StartsWith("default-src 'none'; script-src 'self';", Assert.Single(reponse.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.True(reponse.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(reponse.Headers.GetValues("X-Content-Type-Options")));
    }
}
