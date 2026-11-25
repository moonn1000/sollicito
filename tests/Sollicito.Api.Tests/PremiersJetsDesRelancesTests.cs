using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Relances;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.IA;
using Sollicito.Application.Relances;
using Sollicito.Application.Sauvegardes;
using Sollicito.Domain.Candidatures;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;
using static Sollicito.Api.Tests.Outils.AppelsDesPremiersJets;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// Le premier jet d'une relance par l'IA (#47, ADR-0019) : sur un geste de l'utilisateur, une demande qui ne porte que le
/// poste, l'entreprise et la date d'envoi, sous les consignes de Sollicito ; une réponse nettoyée, placée entre le salut
/// et la formule du brouillon de #14 ; chaque échec dit sans la réponse du fournisseur ; les refus sans requête ; la porte
/// de la base tenue le temps de la lecture seulement ; le texte relu, déposé pour la fenêtre, puis pris une fois. Le faux
/// fournisseur que pose <see cref="ApiLancee"/> répond à la place du réseau (ADR-0002). Données fictives.
/// </summary>
public sealed class PremiersJetsDesRelancesTests
{
    private const string Envoi = "2026-11-10T09:00";

    private const string DonneesAttendues = "Poste : Comptable\nEntreprise : Lumière Verte SA\nCandidature envoyée le : 10 novembre 2026";

    private const string Paragraphe = "Je me permets de revenir vers vous au sujet de ma candidature au poste de Comptable.";

    [Fact]
    public async Task Avec_Claude_le_premier_jet_ne_porte_que_le_poste_l_entreprise_et_la_date_et_revient_entre_le_salut_et_la_formule()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        api.FauxFournisseur.RepondreTexte(Paragraphe);

        var resultat = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(PremierJet(true, $"Bonjour Jeanne Martin,\n\n{Paragraphe}\n\nBien cordialement,", null, null), resultat.ToJsonString());
        var requete = Assert.Single(api.FauxFournisseur.Recues);
        Assert.Equal("POST", requete.Methode);
        Assert.Equal("https://api.anthropic.com/v1/messages", requete.Adresse);
        Assert.Equal(CleDeClaude, Assert.Single(requete.Valeurs("x-api-key")));
        var corps = JsonNode.Parse(requete.Texte)!.AsObject();
        // Rien d'autre que le modèle, la borne, les consignes, le message des données et le raisonnement coupé : aucun outil.
        Assert.Equal(["max_tokens", "messages", "model", "system", "thinking"], corps.Select(propriete => propriete.Key).Order(StringComparer.Ordinal));
        Assert.Equal(PremierJetDeRelance.Consignes, corps["system"]!.GetValue<string>());
        Assert.Equal(750, corps["max_tokens"]!.GetValue<int>());
        var message = Assert.Single(corps["messages"]!.AsArray())!.AsObject();
        Assert.Equal("user", message["role"]!.GetValue<string>());
        Assert.Equal(DonneesAttendues, message["content"]!.GetValue<string>());
        // Le contact ne part jamais chez le fournisseur.
        Assert.DoesNotContain("Jeanne", requete.Texte, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exemple.invalid", requete.Texte, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Avec_OpenAI_le_premier_jet_part_dans_instructions_et_input_sans_garde_chez_le_fournisseur()
    {
        var preparation = await PreparerAsync("OpenAI");
        await using var api = preparation.Api;
        api.FauxFournisseur.RepondreTexte(Paragraphe);

        var resultat = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(PremierJet(true, $"Bonjour Jeanne Martin,\n\n{Paragraphe}\n\nBien cordialement,", null, null), resultat.ToJsonString());
        var requete = Assert.Single(api.FauxFournisseur.Recues);
        Assert.Equal("https://api.openai.com/v1/responses", requete.Adresse);
        Assert.Equal("Bearer " + CleDOpenAI, Assert.Single(requete.Valeurs("Authorization")));
        var corps = JsonNode.Parse(requete.Texte)!.AsObject();
        Assert.Equal(
            ["input", "instructions", "max_output_tokens", "model", "reasoning", "store"],
            corps.Select(propriete => propriete.Key).Order(StringComparer.Ordinal));
        Assert.Equal(PremierJetDeRelance.Consignes, corps["instructions"]!.GetValue<string>());
        Assert.Equal(DonneesAttendues, corps["input"]!.GetValue<string>());
        Assert.False(corps["store"]!.GetValue<bool>());
        Assert.DoesNotContain("Jeanne", requete.Texte, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sans_destinataire_le_premier_jet_commence_par_Bonjour()
    {
        var preparation = await PreparerAsync("Claude", avecContact: false);
        await using var api = preparation.Api;
        api.FauxFournisseur.RepondreTexte(Paragraphe);

        var resultat = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(PremierJet(true, $"Bonjour,\n\n{Paragraphe}\n\nBien cordialement,", null, null), resultat.ToJsonString());
    }

    [Fact]
    public async Task Une_reponse_piegee_revient_nettoyee_et_son_HTML_reste_du_texte()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var piege = "<script>alert(1)</script><img src=x onerror=\"alert(2)\">" + (char)0x07 + (char)0x1B + "[31mRouge"
            + char.ConvertFromUtf32(0x202E) + "envers" + char.ConvertFromUtf32(0x2066) + char.ConvertFromUtf32(0x200B)
            + char.ConvertFromUtf32(0xE0041) + char.ConvertFromUtf32(0xFE0F) + char.ConvertFromUtf32(0xE000) + "  \r\n\r\n\r\n\r\nfin.";
        api.FauxFournisseur.RepondreTexte(piege);

        var resultat = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(
            PremierJet(true, "Bonjour Jeanne Martin,\n\n<script>alert(1)</script><img src=x onerror=\"alert(2)\">[31mRougeenvers\n\nfin.\n\nBien cordialement,", null, null),
            resultat.ToJsonString());
    }

    [Fact]
    public async Task Une_reponse_vide_une_fois_nettoyee_est_illisible()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        api.FauxFournisseur.RepondreTexte(char.ConvertFromUtf32(0x200B) + char.ConvertFromUtf32(0xE0041) + " \n\t\n " + char.ConvertFromUtf32(0x202E));

        var resultat = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(PremierJet(false, null, "ReponseIllisible", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard."), resultat.ToJsonString());
    }

    [Fact]
    public async Task Au_dela_de_1500_caracteres_aucun_texte_ne_revient_rien_n_est_tronque()
    {
        var preparation = await PreparerAsync("OpenAI");
        await using var api = preparation.Api;

        api.FauxFournisseur.RepondreTexte(new string('a', 1_500));
        var aLaBorne = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);
        api.FauxFournisseur.RepondreTexte(new string('a', 1_501));
        var auDela = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.True(aLaBorne["reussi"]!.GetValue<bool>());
        Assert.Equal(PremierJet(false, null, "ReponseTropLongue", "La réponse d'OpenAI n'a pas pu être lue. Réessayez plus tard."), auDela.ToJsonString());
    }

    public static TheoryData<string, int, string, string> Echecs => new()
    {
        { "Claude", 401, "CleRefusee", "Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA." },
        { "OpenAI", 402, "QuotaAtteint", "OpenAI refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte chez OpenAI. Réessayez plus tard, ou vérifiez votre compte." },
        { "Claude", 429, "QuotaAtteint", "Claude refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte chez Anthropic. Réessayez plus tard, ou vérifiez votre compte." },
        { "OpenAI", 403, "AccesRefuse", "OpenAI refuse l'accès avec cette clé, par exemple depuis ce pays ou pour ce compte. Vérifiez votre compte chez OpenAI." },
        { "Claude", 404, "ModeleIndisponible", "Claude ne sert plus le modèle qu'utilise cette version de Sollicito. Installez la dernière version de Sollicito." },
        { "OpenAI", 500, "FournisseurEnPanne", "OpenAI ne répond pas pour l'instant, à cause d'une erreur de son côté. Réessayez plus tard." },
        { "Claude", 529, "FournisseurEnPanne", "Claude ne répond pas pour l'instant, à cause d'une erreur de son côté. Réessayez plus tard." },
        { "OpenAI", 400, "RefusDuFournisseur", "OpenAI a refusé la demande. Vérifiez votre compte chez OpenAI." },
        { "Claude", 302, "ReponseIllisible", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard." },
    };

    [Theory]
    [MemberData(nameof(Echecs))]
    public async Task Un_echec_du_fournisseur_se_dit_sans_jamais_reprendre_sa_reponse(string fournisseur, int statut, string echec, string message)
    {
        var preparation = await PreparerAsync(fournisseur);
        await using var api = preparation.Api;
        api.FauxFournisseur.RepondreErreur((HttpStatusCode)statut);

        using var reponse = await api.DemanderUnPremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var texte = await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PremierJet(false, null, echec, message), JsonNode.Parse(texte)!.ToJsonString());
        Assert.DoesNotContain(FauxFournisseurEnLigne.MessageDuFournisseur, texte, StringComparison.Ordinal);
        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Un_reseau_absent_une_reponse_mal_formee_ou_coupee_se_disent_et_la_relance_reste_a_faire()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;

        api.FauxFournisseur.Repondre((_, _) => throw new HttpRequestException("Faux réseau absent."));
        var absent = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);
        api.FauxFournisseur.Repondre(HttpStatusCode.OK, """{"type":"message","content":[""");
        var malFormee = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);
        api.FauxFournisseur.Repondre(HttpStatusCode.OK, """{"type":"message","content":[{"type":"text","text":"Coupé"}],"stop_reason":"max_tokens"}""");
        var coupee = await api.PremierJetAsync(preparation.Candidature, preparation.Relance);

        Assert.Equal(PremierJet(false, null, "Injoignable", "Claude n'a pas pu être joint : vérifiez votre connexion à Internet, puis réessayez."), absent.ToJsonString());
        Assert.Equal(PremierJet(false, null, "ReponseIllisible", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard."), malFormee.ToJsonString());
        Assert.Equal(PremierJet(false, null, "ReponseTropLongue", "La réponse de Claude n'a pas pu être lue. Réessayez plus tard."), coupee.ToJsonString());
        // La relance à la main reste possible : le brouillon de #14 se prépare toujours.
        Assert.NotNull(await Brouillons(api).PreparerAsync(preparation.Relance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sans_IA_prete_le_premier_jet_est_refuse_sans_requete()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, relance) = await PlanifierAsync(api, avecContact: true);

        // Aucun choix fait, puis « Aucune IA », puis le modèle local, pas encore branché.
        await RefuseAsync(api, candidature, relance, HttpStatusCode.Conflict, MessagesDeLIA.SansFournisseurEnLigne);
        await api.ChoisirLIAAvecSuccesAsync("Aucun");
        await RefuseAsync(api, candidature, relance, HttpStatusCode.Conflict, MessagesDeLIA.SansFournisseurEnLigne);
        await api.ChoisirLIAAvecSuccesAsync("Ollama");
        await RefuseAsync(api, candidature, relance, HttpStatusCode.Conflict, MessagesDeLIA.SansFournisseurEnLigne);
        // Claude, sans clé.
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await RefuseAsync(api, candidature, relance, HttpStatusCode.Conflict, MessagesDeLIA.SansCle);

        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Une_relance_close_inconnue_ou_d_une_autre_candidature_est_refusee_sans_requete()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var (autre, relanceDeLAutre) = await PlanifierAsync(api, avecContact: false, entreprise: "Brasserie Lune Rousse");

        await RefuseAsync(api, preparation.Candidature + 1_000, preparation.Relance, HttpStatusCode.NotFound, null, "Cette candidature n'existe pas.");
        await RefuseAsync(api, preparation.Candidature, relanceDeLAutre, HttpStatusCode.Conflict, Candidature.RelancePasAFaire);
        await RefuseAsync(api, autre, preparation.Relance, HttpStatusCode.Conflict, Candidature.RelancePasAFaire);
        await RefuseAsync(api, preparation.Candidature, preparation.Relance + 1_000, HttpStatusCode.Conflict, Candidature.RelancePasAFaire);
        using (var faite = await api.MarquerFaiteAsync(preparation.Candidature, preparation.Relance))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        await RefuseAsync(api, preparation.Candidature, preparation.Relance, HttpStatusCode.Conflict, Candidature.RelancePasAFaire);
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Une_demande_avec_un_corps_est_refusee_sans_etre_lue()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;

        using var reponse = await api.EcrireAsync(HttpMethod.Post, AdresseDuPremierJet(preparation.Candidature, preparation.Relance), """{"donnees":"autre chose"}""");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("La demande d'un premier jet ne prend aucun corps.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Une_seconde_demande_pendant_la_premiere_est_refusee_sans_requete()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var (entree, relache) = Retenir(api);

        var premiere = api.DemanderUnPremierJetAsync(preparation.Candidature, preparation.Relance);
        await entree.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await RefuseAsync(api, preparation.Candidature, preparation.Relance, HttpStatusCode.Conflict, MessagesDeLIA.UneDemandeEnCours);
        relache.TrySetResult();

        using (var reponse = await premiere)
        {
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        }

        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Une_restauration_n_attend_pas_l_IA_et_porte_fermee_le_premier_jet_n_entre_pas()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var porte = api.Services.GetRequiredService<PorteDeLaBase>();
        var (entree, relache) = Retenir(api);
        var premiere = api.DemanderUnPremierJetAsync(preparation.Candidature, preparation.Relance);
        await entree.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Le premier jet attend l'IA hors de la porte : elle se ferme aussitôt, comme pour une restauration.
        var chrono = Stopwatch.StartNew();
        Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        Assert.True(chrono.Elapsed < TimeSpan.FromSeconds(5), $"La porte a attendu {chrono.Elapsed}.");
        try
        {
            using var refusee = await api.DemanderUnPremierJetAsync(preparation.Candidature, preparation.Relance);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refusee.StatusCode);
            Assert.Equal("1", Assert.Single(refusee.Headers.RetryAfter!.ToString().Split(',')));
            Assert.Equal(PorteDeLaBase.TitreDuRefus, (await AppelsDesCandidatures.LireAsync(refusee))["title"]!.GetValue<string>());
        }
        finally
        {
            porte.Ouvrir();
        }

        relache.TrySetResult();
        using (var reponse = await premiere)
        {
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        }

        Assert.Single(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Le_texte_relu_se_depose_puis_la_fenetre_le_prend_une_fois_avec_l_objet_et_le_destinataire_de_la_base()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var brouillons = Brouillons(api);

        using (var depot = await api.DeposerLeBrouillonReluAsync(preparation.Candidature, preparation.Relance, "  Bonjour Jeanne,\r\n\r\nTexte relu, corrigé.  "))
        {
            Assert.Equal(HttpStatusCode.NoContent, depot.StatusCode);
        }

        // Le brouillon de #14 ne change pas : il reste le gabarit, et le dépôt attend le sien.
        var gabarit = await brouillons.PreparerAsync(preparation.Relance, TestContext.Current.CancellationToken);
        Assert.StartsWith("Bonjour Jeanne Martin,\n\nJe me permets de revenir vers vous", gabarit!.Corps, StringComparison.Ordinal);
        // Une autre relance ne prend rien.
        Assert.Null(await brouillons.PreparerLeBrouillonReluAsync(preparation.Relance + 1_000, TestContext.Current.CancellationToken));

        var relu = await brouillons.PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken);

        Assert.Equal(new BrouillonDeRelance("jeanne@exemple.invalid", "Candidature au poste de Comptable", "Bonjour Jeanne,\n\nTexte relu, corrigé."), relu);
        // Pris une fois : la fenêtre ne le rouvre pas.
        Assert.Null(await brouillons.PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken));
        Assert.Empty(api.FauxFournisseur.Recues);
    }

    [Fact]
    public async Task Un_nouveau_depot_remplace_le_precedent_et_une_relance_close_n_ouvre_rien()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var brouillons = Brouillons(api);

        await DeposerAvecSuccesAsync(api, preparation.Candidature, preparation.Relance, "Premier texte.");
        await DeposerAvecSuccesAsync(api, preparation.Candidature, preparation.Relance, "Second texte.");
        Assert.Equal("Second texte.", (await brouillons.PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken))!.Corps);

        await DeposerAvecSuccesAsync(api, preparation.Candidature, preparation.Relance, "Texte d'une relance bientôt close.");
        using (var faite = await api.MarquerFaiteAsync(preparation.Candidature, preparation.Relance))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        Assert.Null(await brouillons.PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken));
    }

    public static TheoryData<string, string> TextesRefuses => new()
    {
        { """{"texte":"  "}""", "Écrivez le texte du brouillon." },
        { """{"texte":null}""", "Écrivez le texte du brouillon." },
        { """{}""", "Écrivez le texte du brouillon." },
        { $$"""{"texte":"{{new string('a', 2_001)}}"}""", "Le brouillon ne doit pas dépasser 2000 caractères." },
        { new JsonObject { ["texte"] = "a" + (char)0x07 + "b" }.ToJsonString(), "Le brouillon ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide." },
        { new JsonObject { ["texte"] = "a" + char.ConvertFromUtf32(0x202E) + "b" }.ToJsonString(), "Le brouillon ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide." },
        { $$"""{"texte":"{{new string('é', 2_000)}}"}""", "Le brouillon est trop long pour s'ouvrir dans votre messagerie : raccourcissez-le." },
    };

    [Theory]
    [MemberData(nameof(TextesRefuses))]
    public async Task Un_texte_relu_refuse_par_sa_regle_n_est_pas_depose(string corps, string motif)
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;

        using var reponse = await api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(preparation.Candidature, preparation.Relance), corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("Le brouillon n'est pas prêt : corrigez le champ signalé.", probleme["title"]!.GetValue<string>());
        Assert.Equal(motif, Assert.Single(probleme["errors"]!["texte"]!.AsArray())!.GetValue<string>());
        Assert.Null(await Brouillons(api).PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"texte":"Bonjour.","objet":"Autre objet"}""")]
    [InlineData("""{"texte":"Bonjour.","texte":"Autre texte."}""")]
    [InlineData("""{"texte":["Bonjour."]}""")]
    [InlineData("""{"texte":"Bonjour.""")]
    public async Task Un_corps_illisible_est_refuse_et_rien_n_est_depose(string corps)
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;

        using var reponse = await api.EcrireAsync(HttpMethod.Put, AdresseDuBrouillonRelu(preparation.Candidature, preparation.Relance), corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Null(await Brouillons(api).PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Un_corps_de_plus_de_16_Kio_est_refuse()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;

        using var reponse = await api.DeposerLeBrouillonReluAsync(preparation.Candidature, preparation.Relance, new string('a', 17 * 1024));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_texte_relu_d_une_relance_close_inconnue_ou_d_une_autre_candidature_n_est_pas_depose()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;
        var (autre, relanceDeLAutre) = await PlanifierAsync(api, avecContact: false, entreprise: "Brasserie Lune Rousse");

        await DepotRefuseAsync(api, preparation.Candidature + 1_000, preparation.Relance, HttpStatusCode.NotFound, "Cette candidature n'existe pas.", null);
        await DepotRefuseAsync(api, autre, preparation.Relance, HttpStatusCode.Conflict, "Le brouillon n'est pas prêt.", Candidature.RelancePasAFaire);
        await DepotRefuseAsync(api, preparation.Candidature, relanceDeLAutre, HttpStatusCode.Conflict, "Le brouillon n'est pas prêt.", Candidature.RelancePasAFaire);

        Assert.Null(await Brouillons(api).PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken));
        Assert.Null(await Brouillons(api).PreparerLeBrouillonReluAsync(relanceDeLAutre, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Une_restauration_vide_le_depot_le_texte_relu_designait_une_relance_de_la_base_d_avant()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await using var api = await AppelsDesSauvegardes.LancerSurAsync(dossier, new HorlogeDeTest());
            await api.AttendreLaPremiereVerificationAsync();
            var (candidature, relance) = await PlanifierAsync(api, avecContact: true);
            await api.SauvegarderMaintenantAsync();
            await DeposerAvecSuccesAsync(api, candidature, relance, "Texte relu avant la restauration.");

            Assert.Equal(
                IssueDeLaRestauration.Restauree,
                await api.RestaurerAsync(api.Copie(api.CopieDuJour(DateOnly.FromDateTime(HorlogeDeTest.Reference.UtcDateTime)))));

            // La même relance existe dans la copie restaurée, mais le texte déposé avant ne s'ouvre plus.
            Assert.Null(await Brouillons(api).PreparerLeBrouillonReluAsync(relance, TestContext.Current.CancellationToken));
            await DeposerAvecSuccesAsync(api, candidature, relance, "Texte relu après la restauration.");
            Assert.Equal(
                "Texte relu après la restauration.",
                (await Brouillons(api).PreparerLeBrouillonReluAsync(relance, TestContext.Current.CancellationToken))!.Corps);
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    [Fact]
    public async Task Aucune_demande_ne_part_a_l_affichage_a_la_planification_ni_au_brouillon_de_14()
    {
        var preparation = await PreparerAsync("Claude");
        await using var api = preparation.Api;

        await api.FicheAsync(preparation.Candidature);
        await api.ListerAsync();
        await api.RelancesASignalerAsync();
        await api.EtatDeLIAAsync();
        await PlanifierAsync(api, avecContact: true, entreprise: "Brasserie Lune Rousse");
        await Brouillons(api).PreparerAsync(preparation.Relance, TestContext.Current.CancellationToken);
        await DeposerAvecSuccesAsync(api, preparation.Candidature, preparation.Relance, "Texte relu.");
        await Brouillons(api).PreparerLeBrouillonReluAsync(preparation.Relance, TestContext.Current.CancellationToken);

        Assert.Empty(api.FauxFournisseur.Recues);
        Assert.Equal(0, api.FauxFournisseur.AppelsSansReponsePrevue);
    }

    private static BrouillonsDesRelances Brouillons(ApiLancee api) => api.Services.GetRequiredService<BrouillonsDesRelances>();

    // Une API dont l'IA est prête (le fournisseur choisi, sa clé fictive), et une candidature avec sa relance à faire.
    private static async Task<Preparation> PreparerAsync(string fournisseur, bool avecContact = true)
    {
        var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.ChoisirLIAAvecSuccesAsync(fournisseur);
        await api.EnregistrerLaCleAvecSuccesAsync(fournisseur == "Claude" ? CleDeClaude : CleDOpenAI);
        var (candidature, relance) = await PlanifierAsync(api, avecContact);
        return new Preparation(api, candidature, relance);
    }

    private static async Task<(int Candidature, int Relance)> PlanifierAsync(ApiLancee api, bool avecContact, string entreprise = "Lumière Verte SA")
    {
        var candidature = await api.EnregistrerAvecSuccesAsync(entreprise, "Comptable", Envoi);
        var id = AppelsDuSuivi.IdDe(candidature);
        int? contact = avecContact
            ? Id(await api.AjouterUnContactAvecSuccesAsync(Id(candidature["entreprise"]), "Jeanne Martin", "jeanne@exemple.invalid"))
            : null;
        return (id, Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(id, destinataire: contact))));
    }

    // Le faux fournisseur retient chaque demande jusqu'à ce que le test la relâche, puis répond par un texte.
    private static (TaskCompletionSource Entree, TaskCompletionSource Relache) Retenir(ApiLancee api)
    {
        var entree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var relache = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.FauxFournisseur.Repondre(async (requete, annulation) =>
        {
            entree.TrySetResult();
            await relache.Task.WaitAsync(annulation);
            return FauxFournisseurEnLigne.Json(HttpStatusCode.OK, Reponse(requete, Paragraphe));
        });
        return (entree, relache);
    }

    private static async Task RefuseAsync(ApiLancee api, int candidature, int relance, HttpStatusCode statut, string? detail, string? titre = null)
    {
        using var reponse = await api.DemanderUnPremierJetAsync(candidature, relance);
        Assert.Equal(statut, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(titre ?? PremiersJetsDesRelances.TitreDuRefus, probleme["title"]!.GetValue<string>());
        Assert.Equal(detail, probleme["detail"]?.GetValue<string>());
    }

    private static async Task DeposerAvecSuccesAsync(ApiLancee api, int candidature, int relance, string texte)
    {
        using var reponse = await api.DeposerLeBrouillonReluAsync(candidature, relance, texte);
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private static async Task DepotRefuseAsync(ApiLancee api, int candidature, int relance, HttpStatusCode statut, string titre, string? detail)
    {
        using var reponse = await api.DeposerLeBrouillonReluAsync(candidature, relance, "Texte relu.");
        Assert.Equal(statut, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(titre, probleme["title"]!.GetValue<string>());
        Assert.Equal(detail, probleme["detail"]?.GetValue<string>());
    }

    private sealed record Preparation(ApiLancee Api, int Candidature, int Relance);
}
