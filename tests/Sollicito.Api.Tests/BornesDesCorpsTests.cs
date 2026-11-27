using System.Net;
using System.Text.Json.Nodes;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDeLExtraction;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;
using static Sollicito.Api.Tests.Outils.AppelsDeLImport;

namespace Sollicito.Api.Tests;

/// <summary>
/// La borne du corps d'une requête (#98, point 3) : 256 Kio faute d'une borne propre à la route. Les routes qui lisent un
/// long texte gardent la leur : 1 Mio pour un collage et une extraction, 3 Mio pour l'aperçu et l'import d'un fichier CSV.
/// Aucune saisie de l'interface n'atteint la borne par défaut : ses champs s'arrêtent à 10 000 caractères.
/// </summary>
public sealed class BornesDesCorpsTests
{
    private const int BorneParDefaut = 256 * 1024;

    // La longueur au plus d'un champ de texte de l'interface (« maxLength »), hors texte d'une offre et texte relu.
    private const int ChampDeLInterfaceAuPlus = 10_000;

    [Theory]
    [InlineData("POST", "/api/candidatures")]
    [InlineData("PUT", "/api/candidatures/1")]
    [InlineData("POST", "/api/candidatures/1/etapes")]
    [InlineData("POST", "/api/candidatures/1/etapes/1/correction")]
    [InlineData("POST", "/api/entreprises/1/contacts")]
    [InlineData("PUT", "/api/contacts/1")]
    [InlineData("PUT", "/api/offres/1")]
    [InlineData("PUT", "/api/reglages/relances")]
    public async Task Sans_borne_propre_un_corps_de_plus_de_256_Kio_est_refuse_par_413(string methode, string chemin)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var admis = await api.EcrireAsync(new HttpMethod(methode), chemin, Etendu("{}", BorneParDefaut));
        using var tropGros = await api.EcrireAsync(new HttpMethod(methode), chemin, Etendu("{}", BorneParDefaut + 1));

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, admis.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tropGros.StatusCode);
    }

    [Fact]
    public async Task Un_collage_de_1_Mio_passe_toujours()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var corps = new JsonObject { ["texte"] = "Offre fictive.", ["poste"] = "Comptable" }.ToJsonString();

        using var reponse = await api.EcrireAsync(HttpMethod.Post, "/api/offres", Etendu(corps, 1024 * 1024));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_extraction_de_1_Mio_passe_toujours()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);
        api.FauxFournisseur.RepondreTexte("""{"entreprise":null,"poste":"Comptable","lieu":null,"publieeLe":null,"adresse":null}""");
        var corps = new JsonObject { ["texte"] = "Offre fictive d'un poste de comptable." }.ToJsonString();

        using var reponse = await api.EcrireAsync(HttpMethod.Post, AdresseDeLExtraction, Etendu(corps, 1024 * 1024));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Theory]
    [InlineData(RouteDeLApercu)]
    [InlineData(RouteDeLImport)]
    public async Task L_apercu_et_l_import_d_un_fichier_de_3_Mio_de_corps_passent_toujours(string route)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        var corps = Corps(Csv("poste\nComptable\n"), route == RouteDeLImport ? [] : null);

        using var reponse = await api.EcrireAsync(HttpMethod.Post, route, Etendu(corps, 3 * 1024 * 1024));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/candidatures", new[] { "entreprise", "poste" })]
    [InlineData("POST", "/api/entreprises/1/contacts", new[] { "nom", "email" })]
    [InlineData("PUT", "/api/offres/1", new[] { "entreprise", "poste", "lieu", "adresse" })]
    public async Task La_plus_longue_saisie_de_l_interface_reste_sous_la_borne_et_recoit_un_refus_de_ses_champs(string methode, string chemin, string[] champs)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        // Le pire d'une saisie : chaque champ plein de caractères que le JSON écrit sur six octets.
        var corps = new JsonObject();
        foreach (var champ in champs)
        {
            corps[champ] = new string((char)1, ChampDeLInterfaceAuPlus);
        }

        var texte = corps.ToJsonString();
        Assert.True(texte.Length < BorneParDefaut, $"{texte.Length} octets");

        using var reponse = await api.EcrireAsync(new HttpMethod(methode), chemin, texte);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    // Un corps JSON complété par des blancs, que le format admet, jusqu'à la taille voulue : en octets, le corps étant en ASCII.
    private static string Etendu(string json, int taille) => json[..^1] + new string(' ', taille - json.Length) + "}";
}
