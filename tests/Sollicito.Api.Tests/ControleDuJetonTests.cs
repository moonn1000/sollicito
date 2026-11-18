using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Sollicito.Api.Securite;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Toute route de l'API, documentation OpenAPI et adresses inconnues comprises, exige le jeton du lancement
/// dans son en-tête dédié (ADR-0001, D9) : sans lui, ou ailleurs que dans cet en-tête, la réponse est 401.
/// </summary>
public sealed class ControleDuJetonTests(ApiLancee api) : IClassFixture<ApiLancee>
{
    // Une route connue, la documentation OpenAPI, une route inconnue de l'API, la racine, un fichier.
    public static TheoryData<string> Chemins => ["/api/etat", "/api/openapi/v1.json", "/api/inconnue", "/", "/index.html"];

    [Fact]
    public async Task Chaque_lancement_tire_un_jeton_neuf_de_256_bits()
    {
        await using var autre = await ApiLancee.LancerAsync(_ => { });

        Assert.Equal(JetonDeLancement.TailleEnOctets, Base64Url.DecodeFromChars(api.Jeton).Length);
        Assert.Equal(256, JetonDeLancement.TailleEnOctets * 8);
        Assert.NotEqual(api.Jeton, autre.Jeton);
    }

    [Fact]
    public void Le_texte_du_jeton_ne_montre_pas_sa_valeur()
    {
        var jeton = new JetonDeLancement();

        Assert.DoesNotContain(jeton.Valeur(), jeton.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Chemins))]
    public async Task Une_lecture_sans_jeton_est_refusee(string chemin)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(JetonDeLancement.EnTete, Assert.Single(reponse.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Une_ecriture_sans_jeton_est_refusee_meme_venue_de_l_interface(string methode)
    {
        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), "/api/etat", jeton: false, requete => requete.Headers.Add("Origin", api.Origine));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Le_jeton_est_exige_dans_chaque_environnement(string environnement)
    {
        await using var lancee = await ApiLancee.LancerAsync(builder => builder.UseEnvironment(environnement));

        using var sansJeton = await lancee.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: false);
        using var avecJeton = await lancee.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.OK, avecJeton.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("faux")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("{jeton}A")]
    [InlineData("A{jeton}")]
    [InlineData("{jeton}, {jeton}")]
    [InlineData("Bearer {jeton}")]
    public async Task Un_faux_jeton_est_refuse(string modele)
    {
        var faux = api.Remplir(modele);

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.TryAddWithoutValidation(JetonDeLancement.EnTete, faux));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_presque_juste_est_refuse()
    {
        // Le bon jeton, un seul caractère changé : même longueur, donc comparaison de tout le contenu.
        var jeton = api.Jeton;
        var presque = (jeton[0] == 'A' ? "B" : "A") + jeton[1..];

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add(JetonDeLancement.EnTete, presque));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_en_tete_de_jeton_repete_est_refuse()
    {
        var ligne = await api.EnvoyerBrutAsync(api.Remplir(
            "GET /api/etat HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nSollicito-Jeton: {jeton}\r\nSollicito-Jeton: {jeton}\r\nConnection: close\r\n\r\n"));

        Assert.StartsWith("HTTP/1.1 401 ", ligne, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sollicito-Jeton={jeton}")]
    [InlineData("jeton={jeton}")]
    public async Task Le_jeton_dans_un_cookie_ne_vaut_rien(string cookie)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Add("Cookie", api.Remplir(cookie)));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/etat?Sollicito-Jeton={jeton}")]
    [InlineData("/api/etat?jeton={jeton}")]
    [InlineData("/api/etat?access_token={jeton}")]
    [InlineData("/api/etat/{jeton}")]
    [InlineData("/api/{jeton}/etat")]
    public async Task Le_jeton_dans_l_adresse_ne_vaut_rien(string chemin)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, api.Remplir(chemin), jeton: false);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_jeton_dans_l_en_tete_Authorization_ne_vaut_rien()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: false, requete => requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Jeton));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Avec_le_jeton_l_api_donne_son_etat()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("""{"statut":"pret"}""", await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Avec_le_jeton_la_documentation_OpenAPI_decrit_la_route_de_l_etat()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Contains("\"/api/etat\"", await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Avec_le_jeton_une_route_inconnue_n_existe_pas()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/inconnue", jeton: true);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Aucune_reponse_ne_contient_le_jeton()
    {
        // Une réponse de chaque sorte : 200, 404, 405 et chaque refus (400, 401, 403).
        HttpResponseMessage[] reponses =
        [
            await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true),
            await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true),
            await api.EnvoyerAsync(HttpMethod.Get, "/api/inconnue", jeton: true),
            await api.EnvoyerAsync(HttpMethod.Post, "/api/etat", jeton: true, requete => requete.Headers.Add("Origin", api.Origine)),
            await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.Host = "localhost:" + api.Port),
            await api.EnvoyerAsync(HttpMethod.Get, api.Remplir("/api/etat?Sollicito-Jeton={jeton}"), jeton: false),
            await api.EnvoyerAsync(HttpMethod.Post, "/api/etat", jeton: true),
        ];

        try
        {
            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden],
                reponses.Select(reponse => reponse.StatusCode));
            foreach (var reponse in reponses)
            {
                var enTetes = string.Join('\n', reponse.Headers.Concat(reponse.Content.Headers).Select(enTete => enTete.Key + ": " + string.Join(", ", enTete.Value)));
                Assert.DoesNotContain(api.Jeton, enTetes, StringComparison.Ordinal);
                Assert.DoesNotContain(api.Jeton, await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var reponse in reponses)
            {
                reponse.Dispose();
            }
        }
    }
}
