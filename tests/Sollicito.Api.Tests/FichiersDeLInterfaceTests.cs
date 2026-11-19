using System.Net;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les fichiers de l'interface sont servis sans jeton, puisque la page les charge avant de le recevoir ; seul leur
/// en-tête <c>Host</c> est contrôlé (ADR-0003, « Règles d'accès »). Tout le reste, et tout ce qui est sous
/// <c>/api</c>, reste soumis au jeton et à l'origine.
/// </summary>
public sealed class FichiersDeLInterfaceTests(ApiAvecInterface lancement) : IClassFixture<ApiAvecInterface>
{
    private ApiLancee Api => lancement.Api;

    [Theory]
    [InlineData("/", "text/html", InterfaceDeTest.Page)]
    [InlineData("/index.html", "text/html", InterfaceDeTest.Page)]
    [InlineData("/assets/app.js", "text/javascript", InterfaceDeTest.Script)]
    public async Task Les_fichiers_de_l_interface_sont_servis_sans_jeton(string chemin, string type, string contenu)
    {
        using var reponse = await Api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(type, reponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(contenu, await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("localhost:{port}", "/")]
    [InlineData("localhost:{port}", "/assets/app.js")]
    [InlineData("exemple.invalid", "/index.html")]
    [InlineData("127.0.0.2:{port}", "/")]
    public async Task Un_fichier_n_echappe_pas_au_controle_de_Host(string hote, string chemin)
    {
        using var reponse = await Api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false, requete => requete.Headers.Host = Api.Remplir(hote));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Theory]
    [InlineData("/absent.js")]
    [InlineData("/assets/")]
    [InlineData("/assets/absent.js")]
    [InlineData("/candidatures")]
    public async Task Une_adresse_sans_fichier_reste_soumise_au_jeton(string chemin)
    {
        using var reponse = await Api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/fichier.json")]
    [InlineData("/API/fichier.json")]
    [InlineData("/api/")]
    public async Task Aucun_fichier_n_est_servi_sous_api(string chemin)
    {
        using var sansJeton = await Api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);
        using var avecJeton = await Api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: true);

        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, avecJeton.StatusCode);
        Assert.DoesNotContain("rangé sous api", await avecJeton.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Une_ecriture_vers_un_fichier_reste_soumise_au_jeton_et_a_l_origine(string methode)
    {
        using var sansJeton = await Api.EnvoyerAsync(new HttpMethod(methode), "/index.html", jeton: false);
        using var sansOrigine = await Api.EnvoyerAsync(new HttpMethod(methode), "/index.html", jeton: true);

        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sansOrigine.StatusCode);
    }
}
