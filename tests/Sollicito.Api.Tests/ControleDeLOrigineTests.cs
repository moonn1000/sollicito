using System.Net;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// Sur les routes de l'API, une écriture doit porter l'origine exacte de l'interface ; une lecture peut s'en
/// passer, mais une lecture qui porte une autre origine est refusée (ADR-0003, « Règles d'accès »). Toutes ces
/// requêtes portent le bon jeton : seul l'en-tête <c>Origin</c> décide.
/// </summary>
public sealed class ControleDeLOrigineTests(ApiLancee api) : IClassFixture<ApiLancee>
{
    public static TheoryData<string> Ecritures => ["POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    // Origines refusées : null, un autre site, le même serveur sous un autre nom, un autre port, un autre schéma.
    public static TheoryData<string> OriginesEtrangeres =>
    [
        "null",
        "http://exemple.invalid",
        "http://localhost:{port}",
        "http://127.0.0.1",
        "http://127.0.0.1:1",
        "https://127.0.0.1:{port}",
        "http://127.0.0.1:{port}/",
        "{origine}, {origine}",
        "",
    ];

    [Theory]
    [MemberData(nameof(Ecritures))]
    public async Task Une_ecriture_sans_origine_est_refusee(string methode)
    {
        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), "/api/etat", jeton: true);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Ecritures))]
    public async Task Une_ecriture_d_origine_null_est_refusee(string methode)
    {
        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), "/api/etat", jeton: true, requete => requete.Headers.Add("Origin", "null"));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OriginesEtrangeres))]
    public async Task Une_ecriture_d_une_autre_origine_est_refusee(string origine)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Post, "/api/etat", jeton: true, requete => requete.Headers.TryAddWithoutValidation("Origin", api.Remplir(origine)));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Ecritures))]
    public async Task Une_ecriture_venue_de_l_interface_passe_les_controles(string methode)
    {
        // Aucune route n'accepte encore d'écriture : 405 montre que la requête a passé jeton et origine.
        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), "/api/etat", jeton: true, requete => requete.Headers.Add("Origin", api.Origine));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_lecture_sans_origine_passe()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_lecture_venue_de_l_interface_passe()
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.Add("Origin", api.Origine));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OriginesEtrangeres))]
    public async Task Une_lecture_d_une_autre_origine_est_refusee(string origine)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/etat", jeton: true, requete => requete.Headers.TryAddWithoutValidation("Origin", api.Remplir(origine)));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_en_tete_Origin_repete_est_refuse()
    {
        var ligne = await api.EnvoyerBrutAsync(api.Remplir(
            "POST /api/etat HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nSollicito-Jeton: {jeton}\r\nOrigin: {origine}\r\nOrigin: {origine}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));

        Assert.StartsWith("HTTP/1.1 403 ", ligne, StringComparison.Ordinal);
    }
}
