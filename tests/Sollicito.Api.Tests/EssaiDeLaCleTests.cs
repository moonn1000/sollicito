using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.IA;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.IA;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'essai de la clé (#48, ADR-0017) : sur un geste de l'utilisateur seulement, une demande fixe, sans aucune de ses
/// données, au fournisseur réglé, à son adresse officielle, la clé dans le seul en-tête attendu. Le faux fournisseur que
/// pose <see cref="ApiLancee"/> répond à la place du réseau (ADR-0002) ; la réponse du fournisseur ne revient jamais.
/// </summary>
public sealed class EssaiDeLaCleTests
{
    [Fact]
    public async Task Avec_Claude_l_essai_part_une_fois_a_son_adresse_la_cle_dans_x_api_key_et_dit_que_la_cle_fonctionne()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);
        api.FauxFournisseur.RepondreReussi();

        var resultat = await api.EssayerLaCleAvecResultatAsync();

        Assert.Equal(ResultatDeLEssai(true, null, "Claude a répondu : la clé fonctionne."), resultat.ToJsonString());
        var requete = Assert.Single(api.FauxFournisseur.Recues);
        Assert.Equal("POST", requete.Methode);
        Assert.Equal("https://api.anthropic.com/v1/messages", requete.Adresse);
        Assert.Equal(CleDeClaude, Assert.Single(requete.Valeurs("x-api-key")));
        Assert.Empty(requete.Valeurs("Authorization"));
        // La demande fixe de l'essai, et rien d'autre : ni une donnée de l'utilisateur, ni la clé.
        var corps = JsonNode.Parse(requete.Texte)!;
        Assert.Equal(EssaiDeLaCle.Consignes, corps["system"]!.GetValue<string>());
        Assert.Equal(EssaiDeLaCle.Donnees, corps["messages"]![0]!["content"]!.GetValue<string>());
        Assert.DoesNotContain(PartieReconnaissable, requete.Texte, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Avec_OpenAI_l_essai_part_une_fois_a_son_adresse_la_cle_dans_Authorization_et_dit_que_la_cle_fonctionne()
    {
        await using var api = await PreparerAsync("OpenAI", CleDOpenAI);
        api.FauxFournisseur.RepondreReussi();

        var resultat = await api.EssayerLaCleAvecResultatAsync();

        Assert.Equal(ResultatDeLEssai(true, null, "OpenAI a répondu : la clé fonctionne."), resultat.ToJsonString());
        var requete = Assert.Single(api.FauxFournisseur.Recues);
        Assert.Equal("https://api.openai.com/v1/responses", requete.Adresse);
        Assert.Equal("Bearer " + CleDOpenAI, Assert.Single(requete.Valeurs("Authorization")));
        Assert.Empty(requete.Valeurs("x-api-key"));
        var corps = JsonNode.Parse(requete.Texte)!;
        Assert.Equal(EssaiDeLaCle.Consignes, corps["instructions"]!.GetValue<string>());
        Assert.Equal(EssaiDeLaCle.Donnees, corps["input"]!.GetValue<string>());
        Assert.False(corps["store"]!.GetValue<bool>());
        Assert.DoesNotContain(PartieReconnaissable, requete.Texte, StringComparison.Ordinal);
    }

    public static TheoryData<string, int, string, string> Echecs => new()
    {
        { "Claude", 401, "CleRefusee", "Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA." },
        { "OpenAI", 401, "CleRefusee", "OpenAI a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA." },
        { "Claude", 402, "QuotaAtteint", "Claude refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte chez Anthropic. Réessayez plus tard, ou vérifiez votre compte." },
        { "OpenAI", 429, "QuotaAtteint", "OpenAI refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte chez OpenAI. Réessayez plus tard, ou vérifiez votre compte." },
        { "Claude", 403, "AccesRefuse", "Claude refuse l'accès avec cette clé, par exemple depuis ce pays ou pour ce compte. Vérifiez votre compte chez Anthropic." },
        { "OpenAI", 404, "ModeleIndisponible", "OpenAI ne sert plus le modèle qu'utilise cette version de Sollicito. Installez la dernière version de Sollicito." },
        { "Claude", 529, "FournisseurEnPanne", "Claude ne répond pas pour l'instant, à cause d'une erreur de son côté. Réessayez plus tard." },
        { "OpenAI", 503, "FournisseurEnPanne", "OpenAI ne répond pas pour l'instant, à cause d'une erreur de son côté. Réessayez plus tard." },
        { "Claude", 400, "RefusDuFournisseur", "Claude a refusé la demande. Vérifiez votre compte chez Anthropic." },
        { "OpenAI", 302, "ReponseIllisible", "La réponse d'OpenAI n'a pas pu être lue. Réessayez plus tard." },
        { "Claude", 307, "ReponseIllisible", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard." },
    };

    [Theory]
    [MemberData(nameof(Echecs))]
    public async Task Un_echec_du_fournisseur_se_dit_sans_jamais_reprendre_sa_reponse(string fournisseur, int statut, string echec, string message)
    {
        await using var api = await PreparerAsync(fournisseur, fournisseur == "Claude" ? CleDeClaude : CleDOpenAI);
        api.FauxFournisseur.RepondreErreur((HttpStatusCode)statut);

        using var reponse = await api.EssayerLaCleAsync();

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var texte = await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ResultatDeLEssai(false, echec, message), System.Text.Json.Nodes.JsonNode.Parse(texte)!.ToJsonString());
        Assert.DoesNotContain(FauxFournisseurEnLigne.MessageDuFournisseur, texte, StringComparison.Ordinal);
        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Un_reseau_absent_une_reponse_mal_formee_ou_coupee_se_disent_et_l_application_continue()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);

        api.FauxFournisseur.Repondre((_, _) => throw new HttpRequestException("Faux réseau absent."));
        var absent = await api.EssayerLaCleAvecResultatAsync();
        api.FauxFournisseur.Repondre(HttpStatusCode.OK, """{"type":"message","content":[""");
        var malFormee = await api.EssayerLaCleAvecResultatAsync();
        api.FauxFournisseur.Repondre(HttpStatusCode.OK, """{"type":"message","content":[{"type":"text","text":"OK"}],"stop_reason":"max_tokens"}""");
        var coupee = await api.EssayerLaCleAvecResultatAsync();

        Assert.Equal(ResultatDeLEssai(false, "Injoignable", "Claude n'a pas pu être joint : vérifiez votre connexion à Internet, puis réessayez."), absent.ToJsonString());
        Assert.Equal(ResultatDeLEssai(false, "ReponseIllisible", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard."), malFormee.ToJsonString());
        Assert.Equal(ResultatDeLEssai(false, "ReponseTropLongue", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard."), coupee.ToJsonString());
        // L'application continue : l'état se lit, et le tableau de bord répond.
        Assert.Equal(Etat("Claude", true, "Enregistree"), (await api.EtatDeLIAAsync()).ToJsonString());
        using var tableau = await api.EnvoyerAsync(HttpMethod.Get, "/api/candidatures", jeton: true);
        Assert.Equal(HttpStatusCode.OK, tableau.StatusCode);
    }

    [Theory]
    [InlineData(null, MessagesDeLIA.SansFournisseurEnLigne)]
    [InlineData("Aucun", MessagesDeLIA.SansFournisseurEnLigne)]
    [InlineData("Ollama", MessagesDeLIA.SansFournisseurEnLigne)]
    [InlineData("Claude", MessagesDeLIA.SansCle)]
    [InlineData("OpenAI", MessagesDeLIA.SansCle)]
    public async Task Sans_cle_prete_l_essai_est_refuse_sans_requete(string? fournisseur, string titre)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        if (fournisseur is not null)
        {
            await api.ChoisirLIAAvecSuccesAsync(fournisseur);
        }

        await ConflitAsync(api, titre);
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Une_cle_illisible_est_refusee_sans_requete()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);
        await File.WriteAllTextAsync(new DossierDeLaBase(api.DossierDeDonnees).FichierDeLaCle, """{"charge":"abime"}""", TestContext.Current.CancellationToken);

        await ConflitAsync(api, MessagesDeLIA.CleIllisible);
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Une_seule_demande_a_la_fois_la_seconde_est_refusee_sans_requete()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);
        var liberer = new TaskCompletionSource();
        var arrivee = new TaskCompletionSource();
        api.FauxFournisseur.Repondre(async (requete, annulation) =>
        {
            arrivee.TrySetResult();
            await liberer.Task.WaitAsync(annulation);
            return FauxFournisseurEnLigne.Json(HttpStatusCode.OK, FauxFournisseurEnLigne.Reussie(requete));
        });

        var premier = api.EssayerLaCleAsync();
        await arrivee.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await ConflitAsync(api, MessagesDeLIA.UneDemandeEnCours);
        liberer.SetResult();

        using var reponse = await premier;
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Un_essai_avec_un_corps_est_refuse_sans_requete()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);

        using var avecCorps = await api.EcrireAsync(HttpMethod.Post, AdresseDeLEssai, "{}");
        using var parMorceaux = await api.EnvoyerAsync(HttpMethod.Post, AdresseDeLEssai, jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Headers.TransferEncodingChunked = true;
            requete.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        });

        foreach (var reponse in new[] { avecCorps, parMorceaux })
        {
            Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
            Assert.Equal(RoutesDeLIA.TitreDeLEssaiAvecUnCorps, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        }

        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Enregistrer_ou_remplacer_une_cle_ne_declenche_aucun_appel()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);

        await api.EnregistrerLaCleAvecSuccesAsync(AutreCleDeClaude);
        await api.EtatDeLIAAsync();

        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Changer_de_fournisseur_efface_toujours_la_cle_et_l_essai_ne_l_envoie_jamais_a_l_autre()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);
        api.FauxFournisseur.RepondreReussi();
        await api.EssayerLaCleAvecResultatAsync();

        Assert.Equal(Etat("OpenAI", true, "Aucune"), (await api.ChoisirLIAAvecSuccesAsync("OpenAI")).ToJsonString());
        await ConflitAsync(api, MessagesDeLIA.SansCle);
        Assert.Equal(Etat("Claude", true, "Aucune"), (await api.ChoisirLIAAvecSuccesAsync("Claude")).ToJsonString());
        await ConflitAsync(api, MessagesDeLIA.SansCle);

        var requete = Assert.Single(api.FauxFournisseur.Recues);
        Assert.Equal("https://api.anthropic.com/v1/messages", requete.Adresse);
    }

    [Fact]
    public async Task Porte_fermee_pendant_une_restauration_l_essai_repond()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);
        api.FauxFournisseur.RepondreReussi();
        var porte = api.Services.GetRequiredService<PorteDeLaBase>();
        Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        try
        {
            Assert.True((await api.EssayerLaCleAvecResultatAsync())["reussi"]!.GetValue<bool>());
        }
        finally
        {
            porte.Ouvrir();
        }
    }

    [Fact]
    public async Task La_cle_ne_parait_dans_aucune_reponse_ni_aucun_fichier_apres_des_essais()
    {
        await using var api = await PreparerAsync("Claude", CleDeClaude);
        var textes = new List<string>();
        foreach (var statut in new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests })
        {
            if (statut == HttpStatusCode.OK)
            {
                api.FauxFournisseur.RepondreReussi();
            }
            else
            {
                api.FauxFournisseur.RepondreErreur(statut);
            }

            using var reponse = await api.EssayerLaCleAsync();
            textes.Add(await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.All(textes, texte => Assert.DoesNotContain(PartieReconnaissable, texte, StringComparison.Ordinal));
        AucunFichierNeContientDeCle(api.DossierDeDonnees);
    }

    // Le fournisseur choisi, et sa clé enregistrée.
    private static async Task<ApiLancee> PreparerAsync(string fournisseur, string cle)
    {
        var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.ChoisirLIAAvecSuccesAsync(fournisseur);
        await api.EnregistrerLaCleAvecSuccesAsync(cle);
        return api;
    }

    private static async Task ConflitAsync(ApiLancee api, string titre)
    {
        using var reponse = await api.EssayerLaCleAsync();
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal(titre, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
    }
}
