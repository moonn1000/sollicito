using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Sollicito.Api.Relances;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Relances;
using Sollicito.Domain.Candidatures;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les relances d'une candidature, par l'API sur une vraie base SQLite (#14) : planifier, changer, marquer faite,
/// abandonner ; une seule à faire, gardée par la base ; une relance qui suit sa candidature (suppression, entreprise,
/// clôture, date d'envoi corrigée) ; et le tableau de bord, à horloge fixée, le 18/11/2026 à 14:30, heure de Bruxelles.
/// Toutes les données sont fictives.
/// </summary>
public sealed class RelancesTests
{
    // Envoyée le 10/11/2026 : avec le délai par défaut, l'échéance tombe le 17/11, la veille de l'horloge des tests.
    private const string Envoi = "2026-11-10T09:00";

    [Fact]
    public async Task Sans_delai_la_relance_prend_le_delai_par_defaut_et_la_fiche_la_montre()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);

        var fiche = await api.PlanifierAvecSuccesAsync(candidature);

        var relance = Assert.IsType<JsonObject>(Assert.Single(fiche["relances"]!.AsArray()));
        Assert.Equal("2026-11-17", relance["echeance"]!.GetValue<string>());
        Assert.Equal("AFaire", relance["etat"]!.GetValue<string>());
        Assert.Null(relance["destinataire"]);
        Assert.Equal(fiche.ToJsonString(), (await api.FicheAsync(candidature)).ToJsonString());
    }

    [Fact]
    public async Task Une_relance_se_planifie_avec_un_delai_et_un_contact_de_l_entreprise()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, entreprise) = await EnregistrerAvecSonEntrepriseAsync(api);
        var contact = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne.martin@exemple.invalid");

        var fiche = await api.PlanifierAvecSuccesAsync(candidature, delai: 10, destinataire: Id(contact));

        var relance = RelanceAFaire(fiche)!;
        Assert.Equal("2026-11-20", relance["echeance"]!.GetValue<string>());
        Assert.Equal(contact.ToJsonString(), relance["destinataire"]!.ToJsonString());
    }

    [Fact]
    public async Task Les_refus_des_champs_ne_planifient_rien()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        var autre = await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", Envoi);
        var etranger = await api.AjouterUnContactAvecSuccesAsync(Id(autre["entreprise"]), "Paul Lambert", "paul@exemple.invalid");

        foreach (var (delai, destinataire, champs) in new (int?, int?, string[])[]
        {
            (0, null, ["delai"]),
            (366, null, ["delai"]),
            (-7, null, ["delai"]),
            (7, Id(etranger), ["destinataire"]),
            (7, 999, ["destinataire"]),
            (7, 0, ["destinataire"]),
            (0, Id(etranger), ["delai", "destinataire"]),
        })
        {
            using var reponse = await api.PlanifierAsync(candidature, delai, destinataire);

            Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
            var corps = await AppelsDesCandidatures.LireAsync(reponse);
            Assert.Equal("La relance n'est pas planifiée : corrigez les champs signalés.", corps["title"]!.GetValue<string>());
            Assert.Equal(champs, corps["errors"]!.AsObject().Select(champ => champ.Key));
        }

        Assert.Empty((await api.FicheAsync(candidature))["relances"]!.AsArray());
    }

    [Theory]
    [InlineData("""{"delai":"7"}""")]
    [InlineData("""{"delai":7.5}""")]
    [InlineData("""{"delai":7,"echeance":"2026-12-01"}""")]
    [InlineData("""{"delai":7,"etat":"Faite"}""")]
    [InlineData("""[7]""")]
    [InlineData("pas du json")]
    public async Task Un_corps_illisible_ne_planifie_rien(string corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);

        using var reponse = await api.EcrireAsync(HttpMethod.Post, AdresseDesRelances(candidature), corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(0, await api.CompterLesRelancesAsync());
    }

    [Fact]
    public async Task Une_seconde_relance_a_faire_et_une_candidature_close_sont_refusees()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        await api.PlanifierAvecSuccesAsync(candidature);

        using (var seconde = await api.PlanifierAsync(candidature, delai: 14))
        {
            Assert.Equal(HttpStatusCode.Conflict, seconde.StatusCode);
            var corps = await AppelsDesCandidatures.LireAsync(seconde);
            Assert.Equal("La relance n'est pas planifiée.", corps["title"]!.GetValue<string>());
            Assert.Equal(Candidature.RelanceDejaAFaire, corps["detail"]!.GetValue<string>());
        }

        var close = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", Envoi));
        await api.ChangerLeStatutAvecSuccesAsync(close, "Refusee");
        using (var refusee = await api.PlanifierAsync(close))
        {
            Assert.Equal(HttpStatusCode.Conflict, refusee.StatusCode);
            Assert.Equal(Candidature.RelanceSurCandidatureClose, (await AppelsDesCandidatures.LireAsync(refusee))["detail"]!.GetValue<string>());
        }

        Assert.Equal(1, await api.CompterLesRelancesAsync());
    }

    [Fact]
    public async Task Une_candidature_inconnue_n_a_pas_de_relance()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var planifier = await api.PlanifierAsync(42);
        using var changer = await api.ChangerLaRelanceAsync(42, 1, delai: 7);
        using var faite = await api.MarquerFaiteAsync(42, 1);
        using var abandon = await api.AbandonnerAsync(42, 1);

        Assert.All(new[] { planifier, changer, faite, abandon }, reponse => Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode));
    }

    [Fact]
    public async Task La_relance_a_faire_change_de_delai_et_de_destinataire()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, entreprise) = await EnregistrerAvecSonEntrepriseAsync(api);
        var jeanne = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid");
        var paul = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Paul Lambert", "paul@exemple.invalid");
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, destinataire: Id(jeanne))));

        using var reponse = await api.ChangerLaRelanceAsync(candidature, relance, delai: 21, destinataire: Id(paul));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var changee = RelanceAFaire((await AppelsDesCandidatures.LireAsync(reponse)).AsObject())!;
        Assert.Equal(relance, Id(changee));
        Assert.Equal("2026-12-01", changee["echeance"]!.GetValue<string>());
        Assert.Equal("Paul Lambert", changee["destinataire"]!["nom"]!.GetValue<string>());

        // Sans destinataire, la relance n'en a plus ; sans délai, elle prend le délai par défaut.
        using var sansDestinataire = await api.ChangerLaRelanceAsync(candidature, relance);
        var relue = RelanceAFaire((await AppelsDesCandidatures.LireAsync(sansDestinataire)).AsObject())!;
        Assert.Null(relue["destinataire"]);
        Assert.Equal("2026-11-17", relue["echeance"]!.GetValue<string>());
    }

    [Fact]
    public async Task Une_relance_close_ou_d_une_autre_candidature_ne_change_plus()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature)));
        var autre = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", Envoi));
        var relanceDeLAutre = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(autre)));

        using (var croisee = await api.ChangerLaRelanceAsync(candidature, relanceDeLAutre, delai: 30))
        {
            Assert.Equal(HttpStatusCode.Conflict, croisee.StatusCode);
            Assert.Equal(Candidature.RelancePasAFaire, (await AppelsDesCandidatures.LireAsync(croisee))["detail"]!.GetValue<string>());
        }

        using (var faite = await api.MarquerFaiteAsync(candidature, relance))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        using var apres = await api.ChangerLaRelanceAsync(candidature, relance, delai: 30);
        Assert.Equal(HttpStatusCode.Conflict, apres.StatusCode);
        Assert.Equal("La relance n'est pas changée.", (await AppelsDesCandidatures.LireAsync(apres))["title"]!.GetValue<string>());
        Assert.Equal("2026-11-17", (await api.FicheAsync(autre))["relances"]![0]!["echeance"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("faite", "Faite")]
    [InlineData("abandon", "Abandonnee")]
    public async Task Une_relance_close_entre_dans_l_historique_pour_de_bon(string action, string etat)
    {
        var horloge = new HorlogeDeTest();
        await using var api = await ApiLancee.LancerAsync(horloge.Utiliser);
        var (candidature, entreprise) = await EnregistrerAvecSonEntrepriseAsync(api);
        var contact = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid");
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, destinataire: Id(contact))));
        horloge.Avancer(TimeSpan.FromMinutes(5));

        using var reponse = await Clore(api, action, candidature, relance);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var fiche = (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
        Assert.Null(RelanceAFaire(fiche));
        Assert.Equal(etat, fiche["relances"]![0]!["etat"]!.GetValue<string>());
        var cloture = Assert.IsType<JsonObject>(Assert.Single(fiche["historique"]!.AsArray()));
        Assert.Equal("clotureDeRelance", cloture["sorte"]!.GetValue<string>());
        Assert.Equal(relance, cloture["relance"]!.GetValue<int>());
        Assert.Equal(etat, cloture["etat"]!.GetValue<string>());
        Assert.Equal("2026-11-17", cloture["echeance"]!.GetValue<string>());
        Assert.Equal(contact.ToJsonString(), cloture["destinataire"]!.ToJsonString());
        Assert.Equal(horloge.GetUtcNow().UtcDateTime, cloture["survenuLe"]!.GetValue<DateTime>().ToUniversalTime());

        using var deuxFois = await Clore(api, action, candidature, relance);
        Assert.Equal(HttpStatusCode.Conflict, deuxFois.StatusCode);
        Assert.Equal("La relance n'est pas close.", (await AppelsDesCandidatures.LireAsync(deuxFois))["title"]!.GetValue<string>());
        Assert.Single((await api.FicheAsync(candidature))["historique"]!.AsArray());

        // La relance close laisse planifier la suivante, qui part aussi de la date d'envoi.
        var suivante = RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, delai: 14))!;
        Assert.Equal("2026-11-24", suivante["echeance"]!.GetValue<string>());
    }

    [Fact]
    public async Task Clore_la_candidature_abandonne_sa_relance_a_faire()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        await api.PlanifierAvecSuccesAsync(candidature);

        var fiche = await api.ChangerLeStatutAvecSuccesAsync(candidature, "SansReponse");

        Assert.Equal("Abandonnee", fiche["relances"]![0]!["etat"]!.GetValue<string>());
        Assert.Equal(["changementDeStatut", "clotureDeRelance"], fiche["historique"]!.AsArray().Select(evenement => evenement!["sorte"]!.GetValue<string>()));

        // Corriger le changement rouvre la candidature, pas sa relance.
        var corrigee = await api.CorrigerAvecSuccesAsync(candidature);
        Assert.Equal("Envoyee", corrigee["statut"]!.GetValue<string>());
        Assert.Null(RelanceAFaire(corrigee));
    }

    [Fact]
    public async Task Changer_d_entreprise_retire_le_destinataire_de_la_relance_a_faire_pas_des_closes()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, entreprise) = await EnregistrerAvecSonEntrepriseAsync(api);
        var contact = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid");
        var close = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, destinataire: Id(contact))));
        using (var faite = await api.MarquerFaiteAsync(candidature, close))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        await api.PlanifierAvecSuccesAsync(candidature, delai: 14, destinataire: Id(contact));

        // Le même nom ne change pas d'entreprise : le destinataire reste.
        var memeEntreprise = await api.ModifierAvecSuccesAsync(candidature, "Lumière Verte SA", "Comptable confirmée", Envoi);
        Assert.Equal(Id(contact), RelanceAFaire(memeEntreprise)!["destinataire"]!["id"]!.GetValue<int>());

        var fiche = await api.ModifierAvecSuccesAsync(candidature, "Brasserie Lune Rousse", "Comptable confirmée", Envoi);

        Assert.Null(RelanceAFaire(fiche)!["destinataire"]);
        var relanceClose = fiche["relances"]!.AsArray().Single(relance => Id(relance) == close)!;
        Assert.Equal(Id(contact), relanceClose["destinataire"]!["id"]!.GetValue<int>());
        Assert.Equal(Id(contact), fiche["historique"]![0]!["destinataire"]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Corriger_la_date_d_envoi_ne_deplace_pas_l_echeance()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        await api.PlanifierAvecSuccesAsync(candidature);

        var fiche = await api.ModifierAvecSuccesAsync(candidature, "Lumière Verte SA", "Comptable", "2026-11-03T09:00");

        Assert.Equal("2026-11-17", RelanceAFaire(fiche)!["echeance"]!.GetValue<string>());
    }

    [Fact]
    public async Task Supprimer_la_candidature_emporte_ses_relances_et_leurs_clotures()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature)));
        using (var faite = await api.MarquerFaiteAsync(candidature, relance))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        await api.PlanifierAvecSuccesAsync(candidature, delai: 14);
        var gardee = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", Envoi));
        await api.PlanifierAvecSuccesAsync(gardee);

        using var reponse = await api.SupprimerAsync(candidature);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.Equal(1, await api.CompterLesRelancesAsync());
        Assert.Equal(0, await api.CompterLesEvenementsAsync());
        Assert.Single((await api.FicheAsync(gardee))["relances"]!.AsArray());
    }

    [Fact]
    public async Task Dix_planifications_simultanees_ne_creent_qu_une_relance()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);

        var reponses = await Task.WhenAll(Enumerable.Range(1, 10).Select(delai => api.PlanifierAsync(candidature, delai)));
        try
        {
            Assert.Single(reponses, reponse => reponse.StatusCode == HttpStatusCode.OK);
            Assert.All(reponses.Where(reponse => reponse.StatusCode != HttpStatusCode.OK), reponse => Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode));
        }
        finally
        {
            foreach (var reponse in reponses)
            {
                reponse.Dispose();
            }
        }

        Assert.Equal(1, await api.CompterLesRelancesAsync());
    }

    [Fact]
    public async Task Cinq_clotures_simultanees_n_en_font_qu_une()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature)));

        var reponses = await Task.WhenAll(Enumerable.Range(0, 5).Select(rang => rang % 2 == 0 ? api.MarquerFaiteAsync(candidature, relance) : api.AbandonnerAsync(candidature, relance)));
        try
        {
            Assert.Single(reponses, reponse => reponse.StatusCode == HttpStatusCode.OK);
        }
        finally
        {
            foreach (var reponse in reponses)
            {
                reponse.Dispose();
            }
        }

        Assert.Equal(1, await api.CompterLesEvenementsAsync());
    }

    [Fact]
    public async Task La_base_elle_meme_n_admet_qu_une_relance_a_faire_par_candidature()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        await api.PlanifierAvecSuccesAsync(candidature);

        var erreur = await Assert.ThrowsAsync<SqliteException>(() => api.ExecuterAsync(
            $"INSERT INTO Relances (Echeance, Etat, DestinataireId, CandidatureId) VALUES ('2026-12-01', 'AFaire', NULL, {Texte(candidature)})"));

        Assert.Equal(19, erreur.SqliteErrorCode);
        // Une relance close de plus, elle, s'ajoute.
        await api.ExecuterAsync($"INSERT INTO Relances (Echeance, Etat, DestinataireId, CandidatureId) VALUES ('2026-12-01', 'Faite', NULL, {Texte(candidature)})");
        Assert.Equal(2, await api.CompterLesRelancesAsync());
    }

    [Fact]
    public async Task Le_tableau_de_bord_signale_les_relances_echues_et_celles_de_la_semaine()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (echueHier, entreprise) = await EnregistrerAvecSonEntrepriseAsync(api);
        var contact = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid");
        await api.PlanifierAvecSuccesAsync(echueHier, destinataire: Id(contact));
        var echueAujourdhui = await PlanifierPourAsync(api, "Échue aujourd'hui", delai: 2);
        var demain = await PlanifierPourAsync(api, "Demain", delai: 3);
        var dansSeptJours = await PlanifierPourAsync(api, "Dans sept jours", delai: 9);
        await PlanifierPourAsync(api, "Dans huit jours", delai: 10);
        var faite = await PlanifierPourAsync(api, "Faite", delai: 1);
        using (var reponse = await api.MarquerFaiteAsync(faite, Id(RelanceAFaire(await api.FicheAsync(faite)))))
        {
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        }

        var relances = await api.RelancesASignalerAsync();

        Assert.Equal("2026-11-18", relances["aujourdhui"]!.GetValue<string>());
        var echues = relances["echues"]!.AsArray();
        Assert.Equal([echueHier, echueAujourdhui], echues.Select(relance => relance!["candidature"]!["id"]!.GetValue<int>()));
        Assert.Equal(["2026-11-17", "2026-11-18"], echues.Select(relance => relance!["echeance"]!.GetValue<string>()));
        Assert.Equal("Comptable", echues[0]!["candidature"]!["poste"]!.GetValue<string>());
        Assert.Equal("Lumière Verte SA", echues[0]!["candidature"]!["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal(contact.ToJsonString(), echues[0]!["destinataire"]!.ToJsonString());
        Assert.Null(echues[1]!["destinataire"]);
        var deLaSemaine = relances["deLaSemaine"]!.AsArray();
        Assert.Equal([demain, dansSeptJours], deLaSemaine.Select(relance => relance!["candidature"]!["id"]!.GetValue<int>()));
        Assert.Equal(["2026-11-19", "2026-11-25"], deLaSemaine.Select(relance => relance!["echeance"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Le_tableau_de_bord_compte_les_jours_dans_le_fuseau_du_poste()
    {
        // Le 19/11/2026 à 00:30, heure de Bruxelles : encore le 18 en UTC. Aujourd'hui, c'est le 19.
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest(new DateTimeOffset(2026, 11, 18, 23, 30, 0, TimeSpan.Zero)).Utiliser);
        var candidature = await PlanifierPourAsync(api, "Échue ce jour", delai: 3);

        var relances = await api.RelancesASignalerAsync();

        Assert.Equal("2026-11-19", relances["aujourdhui"]!.GetValue<string>());
        Assert.Equal(candidature, Assert.Single(relances["echues"]!.AsArray())!["candidature"]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Le_brouillon_d_une_relance_a_faire_se_prepare_pour_la_fenetre_a_partir_de_la_base()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, entreprise) = await EnregistrerAvecSonEntrepriseAsync(api);
        var contact = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid");
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, destinataire: Id(contact))));
        var brouillons = (BrouillonsDesRelances)api.Services.GetService(typeof(BrouillonsDesRelances))!;

        var brouillon = await brouillons.PreparerAsync(relance, TestContext.Current.CancellationToken);

        Assert.Equal(
            new BrouillonDeRelance(
                "jeanne@exemple.invalid",
                "Candidature au poste de Comptable",
                "Bonjour Jeanne Martin,\n\nJe me permets de revenir vers vous au sujet de ma candidature au poste de Comptable, "
                + "envoyée le 10 novembre 2026. Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.\n\n"
                + "Bien cordialement,"),
            brouillon);

        // Une relance close, ou inconnue, n'a pas de brouillon.
        Assert.Null(await brouillons.PreparerAsync(relance + 100, TestContext.Current.CancellationToken));
        using (var faite = await api.MarquerFaiteAsync(candidature, relance))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        Assert.Null(await brouillons.PreparerAsync(relance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sans_contact_le_brouillon_n_a_pas_de_destinataire()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var candidature = await EnregistrerAsync(api);
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature)));
        var brouillons = (BrouillonsDesRelances)api.Services.GetService(typeof(BrouillonsDesRelances))!;

        var brouillon = await brouillons.PreparerAsync(relance, TestContext.Current.CancellationToken);

        Assert.NotNull(brouillon);
        Assert.Null(brouillon.Destinataire);
        Assert.StartsWith("Bonjour,\n", brouillon.Corps, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> Clore(ApiLancee api, string action, int candidature, int relance) =>
        action == "faite" ? api.MarquerFaiteAsync(candidature, relance) : api.AbandonnerAsync(candidature, relance);

    private static async Task<int> EnregistrerAsync(ApiLancee api) => (await EnregistrerAvecSonEntrepriseAsync(api)).Candidature;

    private static async Task<(int Candidature, int Entreprise)> EnregistrerAvecSonEntrepriseAsync(ApiLancee api)
    {
        var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", Envoi);
        return (AppelsDuSuivi.IdDe(candidature), Id(candidature["entreprise"]));
    }

    // Une candidature envoyée le 16/11/2026, et sa relance à ce délai.
    private static async Task<int> PlanifierPourAsync(ApiLancee api, string poste, int delai)
    {
        var candidature = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Atelier d'Ondine", poste, "2026-11-16T09:00"));
        await api.PlanifierAvecSuccesAsync(candidature, delai);
        return candidature;
    }
}
