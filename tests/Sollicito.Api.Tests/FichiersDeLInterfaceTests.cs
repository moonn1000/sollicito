using System.Net;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les fichiers de l'interface sont servis sans jeton, puisque la page les charge avant de le recevoir ; seul leur
/// en-tête <c>Host</c> est contrôlé (ADR-0003, « Règles d'accès »). Tout le reste, et tout ce qui est sous
/// <c>/api</c>, reste soumis au jeton et à l'origine, quelle que soit la façon d'écrire le chemin. Les mêmes tests
/// tournent sur les deux fournisseurs de fichiers, qui ne lisent pas les chemins de la même façon.
/// </summary>
public abstract class FichiersDeLInterfaceTests(ApiLancee api)
{
    [Theory]
    [InlineData("/", "text/html", InterfaceDeTest.Page)]
    [InlineData("/index.html", "text/html", InterfaceDeTest.Page)]
    [InlineData("/assets/app.js", "text/javascript", InterfaceDeTest.Script)]
    public async Task Les_fichiers_de_l_interface_sont_servis_sans_jeton(string chemin, string type, string contenu)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);

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
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false, requete => requete.Headers.Host = api.Remplir(hote));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Theory]
    [InlineData("/absent.js")]
    [InlineData("/assets/")]
    [InlineData("/assets/absent.js")]
    [InlineData("/candidatures")]
    public async Task Une_adresse_sans_fichier_reste_soumise_au_jeton(string chemin)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/fichier.json")]
    [InlineData("/API/fichier.json")]
    [InlineData("/api/")]
    public async Task Aucun_fichier_n_est_servi_sous_api(string chemin)
    {
        using var sansJeton = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: false);
        using var avecJeton = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: true);

        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, avecJeton.StatusCode);
        Assert.DoesNotContain("rangé sous api", await avecJeton.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    // Écrits tels quels sur la connexion : un client HTTP réécrirait l'antislash, le point final ou « .. ».
    [Theory]
    [InlineData("/api%5Cfichier.json")]
    [InlineData("/API%5cfichier.json")]
    [InlineData("/api\\fichier.json")]
    [InlineData("//api/fichier.json")]
    [InlineData("/api./fichier.json")]
    [InlineData("/api%2Ffichier.json")]
    [InlineData("/%5Capi%5Cfichier.json")]
    [InlineData("/assets/..%5Capi%5Cfichier.json")]
    [InlineData("/..%5Cindex.html")]
    [InlineData("/index.html.")]
    [InlineData("/index.html%20")]
    [InlineData("/index.html::$DATA")]
    [InlineData("/INDEX~1.HTM")]
    [InlineData("/.cache")]
    public async Task Aucune_autre_ecriture_du_chemin_ne_fait_servir_un_fichier_sans_jeton(string chemin)
    {
        var ligneDEtat = await api.EnvoyerBrutAsync(api.Remplir($"GET {chemin} HTTP/1.1\r\nHost: 127.0.0.1:{{port}}\r\nConnection: close\r\n\r\n"));

        // 401 : la requête a passé Host, aucun fichier ne l'a servie, le jeton manque. 400 : Kestrel la refuse avant.
        Assert.Matches(@"^HTTP/1\.1 (401|400) ", ligneDEtat);
    }

    [Fact]
    public async Task Le_temoin_brut_sert_bien_un_fichier()
    {
        // Sans quoi le test précédent ne prouverait rien.
        var ligneDEtat = await api.EnvoyerBrutAsync(api.Remplir("GET /index.html HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n"));

        Assert.StartsWith("HTTP/1.1 200 ", ligneDEtat, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Une_ecriture_vers_un_fichier_reste_soumise_au_jeton_et_a_l_origine(string methode)
    {
        using var sansJeton = await api.EnvoyerAsync(new HttpMethod(methode), "/index.html", jeton: false);
        using var sansOrigine = await api.EnvoyerAsync(new HttpMethod(methode), "/index.html", jeton: true);

        Assert.Equal(HttpStatusCode.Unauthorized, sansJeton.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sansOrigine.StatusCode);
    }
}

/// <summary>Les fichiers lus sur le disque.</summary>
public sealed class FichiersSurLeDisqueTests(ApiAvecInterface lancement)
    : FichiersDeLInterfaceTests(lancement.Api), IClassFixture<ApiAvecInterface>;

/// <summary>Les fichiers embarqués dans l'assembly, comme ceux de l'interface dans l'exe de la fenêtre.</summary>
public sealed class FichiersEmbarquesTests(ApiAvecInterfaceEmbarquee lancement)
    : FichiersDeLInterfaceTests(lancement.Api), IClassFixture<ApiAvecInterfaceEmbarquee>;
