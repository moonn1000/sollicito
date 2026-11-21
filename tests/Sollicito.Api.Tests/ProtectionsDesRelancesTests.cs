using System.Globalization;
using System.Net;
using System.Text;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les onze routes de #14, contacts, relances et délai par défaut, passent par les protections de #10 (ADR-0001, D9 ;
/// ADR-0003) : <c>Host</c> (400), jeton (401), <c>Origin</c> (403), et figurent dans la documentation OpenAPI. Une
/// requête refusée ne change rien : ni les fiches, ni les contacts, ni le réglage.
/// </summary>
public sealed class ProtectionsDesRelancesTests
{
    // {id} : une candidature qui a sa relance à faire, {relance} ; {autre} : une candidature sans relance ; {entreprise} :
    // l'entreprise de {id}, et {contact}, l'un de ses contacts.
    private static readonly (string Methode, string Chemin, string? Corps, HttpStatusCode Attendu)[] LesRoutes =
    [
        ("GET", "/api/relances", null, HttpStatusCode.OK),
        ("GET", "/api/reglages/relances", null, HttpStatusCode.OK),
        ("PUT", "/api/reglages/relances", """{"delaiParDefaut":10}""", HttpStatusCode.OK),
        ("GET", "/api/entreprises/{entreprise}/contacts", null, HttpStatusCode.OK),
        ("POST", "/api/entreprises/{entreprise}/contacts", """{"nom":"Paul Lambert","email":"paul@exemple.invalid"}""", HttpStatusCode.Created),
        ("PUT", "/api/contacts/{contact}", """{"nom":"Jeanne Dupont","email":"jeanne.dupont@exemple.invalid"}""", HttpStatusCode.OK),
        ("DELETE", "/api/contacts/{contact}", null, HttpStatusCode.NoContent),
        ("POST", "/api/candidatures/{autre}/relances", """{"delai":10}""", HttpStatusCode.OK),
        ("PUT", "/api/candidatures/{id}/relances/{relance}", """{"delai":12}""", HttpStatusCode.OK),
        ("POST", "/api/candidatures/{id}/relances/{relance}/faite", null, HttpStatusCode.OK),
        ("POST", "/api/candidatures/{id}/relances/{relance}/abandon", null, HttpStatusCode.OK),
    ];

    public static TheoryData<string, string, string?> Routes
    {
        get
        {
            var donnees = new TheoryData<string, string, string?>();
            foreach (var (methode, chemin, corps, _) in LesRoutes)
            {
                donnees.Add(methode, chemin, corps);
            }

            return donnees;
        }
    }

    public static TheoryData<string, string, string?, HttpStatusCode> RoutesEtReponses
    {
        get
        {
            var donnees = new TheoryData<string, string, string?, HttpStatusCode>();
            foreach (var (methode, chemin, corps, attendu) in LesRoutes)
            {
                donnees.Add(methode, chemin, corps, attendu);
            }

            return donnees;
        }
    }

    public static TheoryData<string, string, string?, string?> SansLOrigineDeLInterface
    {
        get
        {
            var donnees = new TheoryData<string, string, string?, string?>();
            foreach (var (methode, chemin, corps, _) in LesRoutes)
            {
                // Une lecture peut se passer d'origine ; d'une autre origine, elle est refusée aussi.
                foreach (var origine in methode == "GET" ? new string?[] { "http://exemple.invalid" } : new string?[] { "http://exemple.invalid", "null", null })
                {
                    donnees.Add(methode, chemin, corps, origine);
                }
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sans_jeton_la_route_est_refusee(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var etat = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), etat.Remplir(chemin), jeton: false, requete => Preparer(api, requete, corps));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(etat.Avant, await InstantaneAsync(api, etat));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Sous_un_autre_Host_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var etat = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), etat.Remplir(chemin), jeton: true, requete =>
        {
            Preparer(api, requete, corps);
            requete.Headers.Host = "localhost:" + api.Port.ToString(CultureInfo.InvariantCulture);
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(etat.Avant, await InstantaneAsync(api, etat));
    }

    [Theory]
    [MemberData(nameof(SansLOrigineDeLInterface))]
    public async Task Sans_l_origine_de_l_interface_la_route_est_refusee_meme_avec_le_jeton(string methode, string chemin, string? corps, string? origine)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var etat = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), etat.Remplir(chemin), jeton: true, requete =>
        {
            if (corps is not null)
            {
                requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
            }

            if (origine is not null)
            {
                requete.Headers.Add("Origin", origine);
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Equal(etat.Avant, await InstantaneAsync(api, etat));
    }

    [Theory]
    [MemberData(nameof(RoutesEtReponses))]
    public async Task Venues_de_l_interface_les_routes_repondent(string methode, string chemin, string? corps, HttpStatusCode attendu)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var etat = await PreparerAsync(api);

        using var reponse = await api.EnvoyerAsync(new HttpMethod(methode), etat.Remplir(chemin), jeton: true, requete => Preparer(api, requete, corps));

        Assert.Equal(attendu, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_routes_figurent_dans_la_documentation_OpenAPI()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!;
        foreach (var (chemin, methode, operation, codes) in new (string, string, string, string[])[]
        {
            ("/api/relances", "get", "ListerLesRelancesASignaler", ["200"]),
            ("/api/reglages/relances", "get", "LireLeDelaiDesRelances", ["200"]),
            ("/api/reglages/relances", "put", "ChangerLeDelaiDesRelances", ["200", "400", "409"]),
            ("/api/entreprises/{id}/contacts", "get", "ListerLesContactsDUneEntreprise", ["200", "404"]),
            ("/api/entreprises/{id}/contacts", "post", "AjouterUnContact", ["201", "400", "404"]),
            ("/api/contacts/{id}", "put", "ModifierUnContact", ["200", "400", "404"]),
            ("/api/contacts/{id}", "delete", "SupprimerUnContact", ["204", "404"]),
            ("/api/candidatures/{id}/relances", "post", "PlanifierUneRelance", ["200", "400", "404", "409"]),
            ("/api/candidatures/{id}/relances/{relance}", "put", "ChangerUneRelance", ["200", "400", "404", "409"]),
            ("/api/candidatures/{id}/relances/{relance}/faite", "post", "MarquerUneRelanceFaite", ["200", "404", "409"]),
            ("/api/candidatures/{id}/relances/{relance}/abandon", "post", "AbandonnerUneRelance", ["200", "404", "409"]),
        })
        {
            var route = chemins[chemin]![methode]!.AsObject();
            Assert.Equal(operation, route["operationId"]!.GetValue<string>());
            Assert.All(codes, code => Assert.NotNull(route["responses"]![code]));
        }
    }

    private static void Preparer(ApiLancee api, HttpRequestMessage requete, string? corps)
    {
        if (requete.Method != HttpMethod.Get)
        {
            requete.Headers.Add("Origin", api.Origine);
        }

        if (corps is not null)
        {
            requete.Content = new StringContent(corps, Encoding.UTF8, "application/json");
        }
    }

    private static async Task<Etat> PreparerAsync(ApiLancee api)
    {
        var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T09:00");
        var id = AppelsDuSuivi.IdDe(candidature);
        var entreprise = Id(candidature["entreprise"]);
        var contact = Id(await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid"));
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(id, destinataire: contact)));
        var autre = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-10T09:00"));
        var etat = new Etat(id, relance, autre, entreprise, contact, "");
        return etat with { Avant = await InstantaneAsync(api, etat) };
    }

    // Ce qu'une requête refusée ne doit pas changer.
    private static async Task<string> InstantaneAsync(ApiLancee api, Etat etat) =>
        string.Join(
            '\n',
            (await api.FicheAsync(etat.Id)).ToJsonString(),
            (await api.FicheAsync(etat.Autre)).ToJsonString(),
            (await api.ContactsAsync(etat.Entreprise)).ToJsonString(),
            (await api.ReglageDesRelancesAsync()).ToJsonString());

    private sealed record Etat(int Id, int Relance, int Autre, int Entreprise, int Contact, string Avant)
    {
        public string Remplir(string chemin) => chemin
            .Replace("{id}", Texte(Id), StringComparison.Ordinal)
            .Replace("{relance}", Texte(Relance), StringComparison.Ordinal)
            .Replace("{autre}", Texte(Autre), StringComparison.Ordinal)
            .Replace("{entreprise}", Texte(Entreprise), StringComparison.Ordinal)
            .Replace("{contact}", Texte(Contact), StringComparison.Ordinal);
    }
}
