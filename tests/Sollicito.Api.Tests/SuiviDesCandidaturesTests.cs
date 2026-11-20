using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests;

/// <summary>
/// Faire avancer une candidature et relire son historique (#12, absorbe #41), par les routes de l'API, sur une vraie
/// base SQLite créée par les migrations dans un dossier temporaire. Toutes les données sont fictives. L'horloge part du
/// 18/11/2026 à 14:30, heure de Bruxelles, et n'avance que quand le test le dit.
/// </summary>
public sealed class SuiviDesCandidaturesTests
{
    private const string Maintenant = "2026-11-18T13:30:00Z";

    // Une horloge par test : xUnit crée une instance de la classe pour chacun.
    private readonly HorlogeDeTest _horloge = new();

    [Fact]
    public async Task La_fiche_d_une_candidature_neuve_part_d_Envoyee_sans_historique()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        var fiche = await api.FicheAsync(id);

        Assert.Equal(id, fiche["id"]!.GetValue<int>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("Comptable", fiche["poste"]!.GetValue<string>());
        Assert.Equal("2026-11-02T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Envoyee", fiche["statut"]!.GetValue<string>());
        Assert.Equal(["Entretien", "Refusee", "Retiree", "SansReponse"], Textes(fiche["passagesPermis"]));
        Assert.Null(fiche["correction"]);
        Assert.Empty(fiche["historique"]!.AsArray());
    }

    [Fact]
    public async Task Une_candidature_inconnue_n_a_ni_fiche_ni_historique()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var inconnue = id + 1;

        using var lecture = await api.LireLaFicheAsync(inconnue);
        using var statut = await api.ChangerLeStatutAsync(inconnue, "Entretien");
        using var correction = await api.CorrigerAsync(inconnue, 1);
        using var etape = await api.NoterUneEtapeAsync(inconnue, "Appel", "2026-11-10T10:00", null);

        foreach (var reponse in new[] { lecture, statut, correction, etape })
        {
            Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
            Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("Cette candidature n'existe pas.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        }

        // Rien ne s'est écrit, ni dans l'autre candidature, ni ailleurs.
        Assert.Empty((await api.FicheAsync(id))["historique"]!.AsArray());
        Assert.Equal(0, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task Un_passage_permis_change_le_statut_et_s_inscrit_dans_l_historique_date_par_l_horloge()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        var fiche = await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");

        Assert.Equal("Entretien", fiche["statut"]!.GetValue<string>());
        Assert.Equal(["Proposition", "Refusee", "Retiree", "SansReponse"], Textes(fiche["passagesPermis"]));
        var changement = Assert.Single(fiche["historique"]!.AsArray())!.AsObject();
        Assert.Equal("changementDeStatut", changement["sorte"]!.GetValue<string>());
        Assert.Equal(Maintenant, changement["survenuLe"]!.GetValue<string>());
        Assert.Equal("Envoyee", changement["ancien"]!.GetValue<string>());
        Assert.Equal("Entretien", changement["nouveau"]!.GetValue<string>());
        Assert.Null(changement["corrige"]);
        Assert.False(changement["estCorrige"]!.GetValue<bool>());
        var correction = fiche["correction"]!.AsObject();
        Assert.Equal(changement["id"]!.GetValue<int>(), correction["changement"]!.GetValue<int>());
        Assert.Equal("Entretien", correction["de"]!.GetValue<string>());
        Assert.Equal("Envoyee", correction["retourA"]!.GetValue<string>());
        // La fiche relue est celle que l'action a rendue, et le tableau de bord montre le statut à jour.
        Assert.Equal(fiche.ToJsonString(), (await api.FicheAsync(id)).ToJsonString());
        Assert.Equal("Entretien", Assert.Single(await api.ListerAsync())!["statut"]!.GetValue<string>());
    }

    [Fact]
    public async Task Le_parcours_jusqu_a_Acceptee_s_inscrit_du_plus_ancien_au_plus_recent_puis_la_candidature_est_close()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        _horloge.Avancer(TimeSpan.FromDays(3));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Proposition");
        _horloge.Avancer(TimeSpan.FromHours(26));
        var fiche = await api.ChangerLeStatutAvecSuccesAsync(id, "Acceptee");

        Assert.Equal("Acceptee", fiche["statut"]!.GetValue<string>());
        Assert.Empty(fiche["passagesPermis"]!.AsArray());
        var historique = fiche["historique"]!.AsArray();
        Assert.Equal(["2026-11-18T13:30:00Z", "2026-11-21T13:30:00Z", "2026-11-22T15:30:00Z"], historique.Select(e => e!["survenuLe"]!.GetValue<string>()));
        Assert.Equal(["Envoyee>Entretien", "Entretien>Proposition", "Proposition>Acceptee"], historique.Select(Passage));
        Assert.Equal("Acceptee", Assert.Single(await api.ListerAsync())!["statut"]!.GetValue<string>());
    }

    public static TheoryData<string[], string, string> PassagesRefuses => new()
    {
        { [], "Envoyee", "La candidature est déjà au statut Envoyée." },
        { [], "Proposition", "Une candidature ne passe pas du statut Envoyée au statut Proposition. Depuis Envoyée, elle peut passer à Entretien, Refusée, Retirée ou Sans réponse." },
        { [], "Acceptee", "Une candidature ne passe pas du statut Envoyée au statut Acceptée. Depuis Envoyée, elle peut passer à Entretien, Refusée, Retirée ou Sans réponse." },
        { ["Entretien"], "Envoyee", "Une candidature ne passe pas du statut Entretien au statut Envoyée. Depuis Entretien, elle peut passer à Proposition, Refusée, Retirée ou Sans réponse." },
        { ["Entretien"], "Entretien", "La candidature est déjà au statut Entretien." },
        { ["Entretien", "Proposition"], "Entretien", "Une candidature ne passe pas du statut Proposition au statut Entretien. Depuis Proposition, elle peut passer à Acceptée, Refusée, Retirée ou Sans réponse." },
        { ["Refusee"], "Entretien", "La candidature est close, au statut Refusée : son statut ne change plus. Si ce statut est une erreur, corrigez le dernier changement." },
        { ["SansReponse"], "SansReponse", "La candidature est déjà au statut Sans réponse." },
        { ["Entretien", "Retiree"], "Envoyee", "La candidature est close, au statut Retirée : son statut ne change plus. Si ce statut est une erreur, corrigez le dernier changement." },
    };

    [Theory]
    [MemberData(nameof(PassagesRefuses))]
    public async Task Un_passage_que_le_diagramme_ne_permet_pas_est_refuse_avec_un_message_lisible(string[] chemin, string demande, string message)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        foreach (var statut in chemin)
        {
            await api.ChangerLeStatutAvecSuccesAsync(id, statut);
        }

        var avant = (await api.FicheAsync(id)).ToJsonString();

        using var reponse = await api.ChangerLeStatutAsync(id, demande);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("Le statut n'a pas changé.", probleme["title"]!.GetValue<string>());
        Assert.Equal(message, probleme["detail"]!.GetValue<string>());
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [InlineData(null, "Indiquez le nouveau statut.")]
    [InlineData("", "Indiquez le nouveau statut.")]
    [InlineData("Embauchee", "Ce statut n'existe pas.")]
    [InlineData("entretien", "Ce statut n'existe pas.")]
    [InlineData(" Entretien", "Ce statut n'existe pas.")]
    [InlineData("1", "Ce statut n'existe pas.")]
    [InlineData("Envoyee, Entretien", "Ce statut n'existe pas.")]
    public async Task Un_statut_absent_ou_inconnu_est_refuse_comme_un_champ(string? statut, string message)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var reponse = await api.ChangerLeStatutAsync(id, statut);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("Le statut n'a pas changé.", probleme["title"]!.GetValue<string>());
        var refus = probleme["errors"]!.AsObject();
        Assert.Equal(["statut"], refus.Select(paire => paire.Key));
        Assert.Equal([message], Textes(refus["statut"]));
        Assert.Equal("Envoyee", (await api.FicheAsync(id))["statut"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"statut":1}""")]
    [InlineData("""{"statut":99}""")]
    [InlineData("""{"statut":"Entretien","survenuLe":"2026-11-10T10:00"}""")]
    [InlineData("""{"statut":["Entretien"]}""")]
    [InlineData("""{"statut":"Entretien""")]
    [InlineData("pas du JSON")]
    [InlineData("")]
    public async Task Un_corps_de_statut_illisible_ou_etranger_est_refuse(string corps)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var reponse = await api.EnvoyerAuStatutAsync(id, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("La requête est illisible.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Equal(0, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task Un_corps_qui_n_est_pas_du_JSON_est_refuse_sur_le_statut_comme_sur_les_etapes()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var statut = await api.EnvoyerAuStatutAsync(id, """{"statut":"Entretien"}""", "text/plain");
        using var etape = await api.EnvoyerALEtapeAsync(id, """{"type":"Appel","survenuLe":"2026-11-10T10:00"}""", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, statut.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, etape.StatusCode);
        Assert.Equal(0, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task Des_changements_simultanes_vers_un_meme_statut_n_en_gardent_qu_un()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        var reponses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.ChangerLeStatutAsync(id, "Refusee")));

        Assert.Single(reponses, reponse => reponse.StatusCode == HttpStatusCode.OK);
        Assert.Equal(9, reponses.Count(reponse => reponse.StatusCode == HttpStatusCode.Conflict));
        var fiche = await api.FicheAsync(id);
        Assert.Equal("Refusee", fiche["statut"]!.GetValue<string>());
        Assert.Single(fiche["historique"]!.AsArray());
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Fact]
    public async Task Corriger_le_dernier_changement_ramene_au_statut_d_avant_et_le_garde_marque_comme_corrige()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        _horloge.Avancer(TimeSpan.FromHours(1));
        var apresLeRefus = await api.ChangerLeStatutAvecSuccesAsync(id, "Refusee");
        var refus = apresLeRefus["historique"]!.AsArray()[1]!["id"]!.GetValue<int>();
        Assert.Equal(refus, apresLeRefus["correction"]!["changement"]!.GetValue<int>());
        Assert.Equal("Entretien", apresLeRefus["correction"]!["retourA"]!.GetValue<string>());
        _horloge.Avancer(TimeSpan.FromMinutes(5));

        using var reponse = await api.CorrigerAsync(id, refus);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var fiche = (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
        Assert.Equal("Entretien", fiche["statut"]!.GetValue<string>());
        Assert.Equal(["Proposition", "Refusee", "Retiree", "SansReponse"], Textes(fiche["passagesPermis"]));
        var historique = fiche["historique"]!.AsArray();
        Assert.Equal(["Envoyee>Entretien", "Entretien>Refusee", "Refusee>Entretien"], historique.Select(Passage));
        Assert.Equal(["2026-11-18T13:30:00Z", "2026-11-18T14:30:00Z", "2026-11-18T14:35:00Z"], historique.Select(e => e!["survenuLe"]!.GetValue<string>()));
        // Le refus reste, tel qu'il était, marqué comme corrigé ; la correction le désigne.
        Assert.Equal([false, true, false], historique.Select(e => e!["estCorrige"]!.GetValue<bool>()));
        Assert.Equal(refus, historique[1]!["id"]!.GetValue<int>());
        Assert.Null(historique[1]!["corrige"]);
        Assert.Equal(refus, historique[2]!["corrige"]!.GetValue<int>());
        // La prochaine correction viserait l'entretien.
        Assert.Equal(historique[0]!["id"]!.GetValue<int>(), fiche["correction"]!["changement"]!.GetValue<int>());
        Assert.Equal("Envoyee", fiche["correction"]!["retourA"]!.GetValue<string>());
        Assert.Equal("Entretien", Assert.Single(await api.ListerAsync())!["statut"]!.GetValue<string>());
    }

    [Fact]
    public async Task Repetee_la_correction_remonte_jusqu_a_Envoyee_puis_est_refusee()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        await api.ChangerLeStatutAvecSuccesAsync(id, "Proposition");

        Assert.Equal("Entretien", (await api.CorrigerAvecSuccesAsync(id))["statut"]!.GetValue<string>());
        var fiche = await api.CorrigerAvecSuccesAsync(id);
        Assert.Equal("Envoyee", fiche["statut"]!.GetValue<string>());
        Assert.Null(fiche["correction"]);

        var dernier = fiche["historique"]!.AsArray()[^1]!["id"]!.GetValue<int>();
        using var reponse = await api.CorrigerAsync(id, dernier);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("Le changement de statut n'est pas corrigé.", probleme["title"]!.GetValue<string>());
        Assert.Equal("Aucun changement de statut n'est à corriger : la candidature est à son statut de départ.", probleme["detail"]!.GetValue<string>());
        Assert.Equal(4, (await api.FicheAsync(id))["historique"]!.AsArray().Count);
    }

    [Fact]
    public async Task Une_correction_ne_se_corrige_pas_mais_un_passage_refait_se_corrige()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        var corrigee = await api.CorrigerAvecSuccesAsync(id);
        var correction = corrigee["historique"]!.AsArray()[^1]!["id"]!.GetValue<int>();

        using var refusee = await api.CorrigerAsync(id, correction);
        Assert.Equal(HttpStatusCode.Conflict, refusee.StatusCode);

        var refaite = await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        var passage = refaite["historique"]!.AsArray()[^1]!["id"]!.GetValue<int>();
        Assert.Equal(passage, refaite["correction"]!["changement"]!.GetValue<int>());
        using var acceptee = await api.CorrigerAsync(id, passage);
        Assert.Equal(HttpStatusCode.OK, acceptee.StatusCode);
        Assert.Equal("Envoyee", (await api.FicheAsync(id))["statut"]!.GetValue<string>());
    }

    [Fact]
    public async Task La_correction_reprend_une_candidature_close()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        await api.ChangerLeStatutAvecSuccesAsync(id, "SansReponse");

        var fiche = await api.CorrigerAvecSuccesAsync(id);

        Assert.Equal("Entretien", fiche["statut"]!.GetValue<string>());
        Assert.Equal("Proposition", (await api.ChangerLeStatutAvecSuccesAsync(id, "Proposition"))["statut"]!.GetValue<string>());
    }

    [Fact]
    public async Task Une_correction_qui_ne_vise_pas_le_dernier_changement_est_refusee()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var premier = (await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien"))["historique"]!.AsArray()[0]!["id"]!.GetValue<int>();
        await api.ChangerLeStatutAvecSuccesAsync(id, "Proposition");
        var avant = (await api.FicheAsync(id)).ToJsonString();

        using var ancien = await api.CorrigerAsync(id, premier);
        using var inconnu = await api.CorrigerAsync(id, premier + 1000);

        foreach (var reponse in new[] { ancien, inconnu })
        {
            Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
            Assert.Equal(
                "Ce changement de statut n'est pas le dernier à corriger : la fiche a changé entre-temps.",
                (await AppelsDesCandidatures.LireAsync(reponse))["detail"]!.GetValue<string>());
        }

        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Fact]
    public async Task Une_correction_demandee_deux_fois_ne_corrige_qu_un_changement()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        var aCorriger = (await api.ChangerLeStatutAvecSuccesAsync(id, "Refusee"))["correction"]!["changement"]!.GetValue<int>();

        var reponses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.CorrigerAsync(id, aCorriger)));

        Assert.Single(reponses, reponse => reponse.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, reponses.Count(reponse => reponse.StatusCode == HttpStatusCode.Conflict));
        var fiche = await api.FicheAsync(id);
        Assert.Equal("Entretien", fiche["statut"]!.GetValue<string>());
        Assert.Equal(3, fiche["historique"]!.AsArray().Count);
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Fact]
    public async Task Une_etape_s_ajoute_a_sa_place_dans_le_temps_sans_changer_le_statut()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var apresLePassage = await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");

        // Un entretien passé, noté après coup : il se range avant le changement de statut, daté du jour de la saisie.
        var fiche = await api.NoterUneEtapeAvecSuccesAsync(id, "Entretien", "2026-11-10T14:00", "  Premier entretien\r\navec la responsable.  ");

        Assert.Equal("Entretien", fiche["statut"]!.GetValue<string>());
        Assert.Equal(Textes(apresLePassage["passagesPermis"]), Textes(fiche["passagesPermis"]));
        Assert.Equal(apresLePassage["correction"]!.ToJsonString(), fiche["correction"]!.ToJsonString());
        var historique = fiche["historique"]!.AsArray();
        Assert.Equal(["etape", "changementDeStatut"], historique.Select(e => e!["sorte"]!.GetValue<string>()));
        var etape = historique[0]!.AsObject();
        Assert.Equal("Entretien", etape["type"]!.GetValue<string>());
        Assert.Equal("2026-11-10T13:00:00Z", etape["survenuLe"]!.GetValue<string>());
        Assert.Equal("Premier entretien\navec la responsable.", etape["note"]!.GetValue<string>());
        Assert.Equal(fiche.ToJsonString(), (await api.FicheAsync(id)).ToJsonString());
        Assert.Equal("Entretien", Assert.Single(await api.ListerAsync())!["statut"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_egalite_d_instant_l_historique_suit_l_ordre_d_enregistrement_et_une_etape_a_venir_vient_en_dernier()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        // 14:30 à Bruxelles, l'instant de l'horloge, celui du changement de statut.
        await api.NoterUneEtapeAvecSuccesAsync(id, "Echange", "2026-12-01T09:30", "à venir");
        await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-18T14:30", "avant le passage");
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        var fiche = await api.NoterUneEtapeAvecSuccesAsync(id, "Autre", "2026-11-18T14:30", "après le passage");

        var historique = fiche["historique"]!.AsArray();
        Assert.Equal(
            ["Appel", "changementDeStatut", "Autre", "Echange"],
            historique.Select(e => e!["sorte"]!.GetValue<string>() == "etape" ? e["type"]!.GetValue<string>() : "changementDeStatut"));
        Assert.Equal(
            [Maintenant, Maintenant, Maintenant, "2026-12-01T08:30:00Z"],
            historique.Select(e => e!["survenuLe"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public async Task Une_note_absente_ou_blanche_ne_fait_aucune_note(string? note)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        var fiche = await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", note);

        Assert.Null(Assert.Single(fiche["historique"]!.AsArray())!["note"]);
    }

    [Fact]
    public async Task Une_note_sans_le_champ_note_est_admise()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var reponse = await api.EnvoyerALEtapeAsync(id, """{"type":"Appel","survenuLe":"2026-11-10T10:00"}""");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Null(Assert.Single((await AppelsDesCandidatures.LireAsync(reponse))["historique"]!.AsArray())!["note"]);
    }

    [Theory]
    // Les deux bornes : l'envoi lui-même, et un an après l'instant présent, en heure locale.
    [InlineData("2026-11-02T10:00", "2026-11-02T09:00:00Z")]
    [InlineData("2027-11-18T14:30", "2027-11-18T13:30:00Z")]
    [InlineData("2027-07-01T14:30:45", "2027-07-01T12:30:45Z")]
    public async Task L_heure_d_une_etape_se_lit_dans_le_fuseau_du_poste(string saisie, string attendu)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        var fiche = await api.NoterUneEtapeAvecSuccesAsync(id, "Entretien", saisie, null);

        Assert.Equal(attendu, Assert.Single(fiche["historique"]!.AsArray())!["survenuLe"]!.GetValue<string>());
    }

    [Fact]
    public async Task Une_heure_qui_existe_deux_fois_se_lit_a_l_heure_d_hiver()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-10-01T10:00"));

        var fiche = await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-10-25T02:30", null);

        Assert.Equal("2026-10-25T01:30:00Z", Assert.Single(fiche["historique"]!.AsArray())!["survenuLe"]!.GetValue<string>());
    }

    public static TheoryData<string, string?, string> EtapesRefusees => new()
    {
        { "type", null, "Choisissez le type de l'étape : appel, entretien, échange ou autre." },
        { "type", "", "Choisissez le type de l'étape : appel, entretien, échange ou autre." },
        { "type", "Relance", "Choisissez le type de l'étape : appel, entretien, échange ou autre." },
        { "type", "entretien", "Choisissez le type de l'étape : appel, entretien, échange ou autre." },
        { "type", "2", "Choisissez le type de l'étape : appel, entretien, échange ou autre." },
        { "survenuLe", null, "Indiquez la date et l'heure." },
        { "survenuLe", " ", "Indiquez la date et l'heure." },
        { "survenuLe", "10/11/2026 10:00", "La date et l'heure s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-11-20T10:00." },
        { "survenuLe", "2026-11-10", "La date et l'heure s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-11-20T10:00." },
        { "survenuLe", "2026-11-10T10:00Z", "La date et l'heure s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-11-20T10:00." },
        { "survenuLe", "2026-11-31T10:00", "La date et l'heure s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-11-20T10:00." },
        { "survenuLe", "2026-11-02T09:59", "La date et l'heure ne peuvent pas précéder l'envoi de la candidature." },
        { "survenuLe", "1999-12-31T23:59", "La date et l'heure ne peuvent pas précéder l'envoi de la candidature." },
        { "survenuLe", "0001-01-01T00:00", "La date et l'heure ne peuvent pas précéder l'envoi de la candidature." },
        { "survenuLe", "2027-11-18T14:31", "La date et l'heure ne peuvent pas être à plus d'un an dans le futur." },
        { "survenuLe", "9999-12-31T23:59", "La date et l'heure ne peuvent pas être à plus d'un an dans le futur." },
        { "survenuLe", "2027-03-28T02:30", "Cette heure n'existe pas : ce jour-là, au passage à l'heure d'été, l'horloge avance d'une heure." },
        { "note", new string('n', 2001), "La note ne doit pas dépasser 2000 caractères." },
        { "note", "Appel\u0007reçu", "La note ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide." },
        { "note", "Appel" + (char)0x202E + "uçer", "La note ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide." },
        { "note", "Appel" + (char)0xFFFE + "reçu", "La note ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide." },
    };

    [Theory]
    [MemberData(nameof(EtapesRefusees))]
    public async Task Une_etape_invalide_est_refusee_champ_par_champ_avec_un_message_lisible(string champ, string? valeur, string message)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var saisie = new Dictionary<string, string?>
        {
            ["type"] = "Entretien",
            ["survenuLe"] = "2026-11-10T10:00",
            ["note"] = "Premier entretien.",
        };
        saisie[champ] = valeur;

        using var reponse = await api.NoterUneEtapeAsync(id, saisie["type"], saisie["survenuLe"], saisie["note"]);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("L'étape n'est pas ajoutée à l'historique : corrigez les champs signalés.", probleme["title"]!.GetValue<string>());
        var refus = probleme["errors"]!.AsObject();
        Assert.Equal([champ], refus.Select(paire => paire.Key));
        Assert.Equal([message], Textes(refus[champ]));
        Assert.Equal(0, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task Tous_les_champs_refuses_d_une_etape_sont_signales_ensemble()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var reponse = await api.NoterUneEtapeAsync(id, "Relance", "hier", new string('n', 2001));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var refus = (await AppelsDesCandidatures.LireAsync(reponse))["errors"]!.AsObject();
        Assert.Equal(["type", "survenuLe", "note"], refus.Select(paire => paire.Key));
    }

    [Theory]
    [InlineData("""{"type":"Appel","survenuLe":"2026-11-10T10:00","statut":"Refusee"}""")]
    [InlineData("""{"type":"Appel","survenuLe":"2026-11-10T10:00","id":4}""")]
    [InlineData("""{"type":2,"survenuLe":"2026-11-10T10:00"}""")]
    [InlineData("""{"type":"Appel","survenuLe":"2026-11-10T10:00","note":42}""")]
    [InlineData("""{"type":"Appel",""")]
    [InlineData("")]
    public async Task Un_corps_d_etape_illisible_ou_etranger_est_refuse(string corps)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var reponse = await api.EnvoyerALEtapeAsync(id, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("La requête est illisible.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Equal(0, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task Une_note_qui_ressemble_a_du_HTML_est_gardee_et_rendue_telle_quelle()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        const string Note = "<img src=x onerror=alert(1)> & <script>alert(2)</script>";

        await api.NoterUneEtapeAvecSuccesAsync(id, "Echange", "2026-11-10T10:00", Note);

        Assert.Equal(Note, Assert.Single((await api.FicheAsync(id))["historique"]!.AsArray())!["note"]!.GetValue<string>());
    }

    [Fact]
    public async Task L_historique_et_ses_corrections_se_retrouvent_au_lancement_suivant()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            void SurLeMemeDossier(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                _horloge.Utiliser(builder);
                builder.ConfigureTestServices(services => services.AddSingleton(new DossierDeLaBase(dossier)));
            }

            string attendue;
            int id;
            await using (var premier = await ApiLancee.LancerAsync(SurLeMemeDossier))
            {
                id = IdDe(await premier.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
                await premier.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
                _horloge.Avancer(TimeSpan.FromHours(1));
                await premier.ChangerLeStatutAvecSuccesAsync(id, "Refusee");
                _horloge.Avancer(TimeSpan.FromHours(1));
                await premier.CorrigerAvecSuccesAsync(id);
                attendue = (await premier.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-12T11:15", "Rappel\nde la responsable")).ToJsonString();
            }

            await using var second = await ApiLancee.LancerAsync(SurLeMemeDossier);
            var fiche = await second.FicheAsync(id);
            Assert.Equal(attendue, fiche.ToJsonString());
            // Relue de la base, la correction retrouve le changement qu'elle corrige.
            var historique = fiche["historique"]!.AsArray();
            Assert.Equal([false, false, true, false], historique.Select(e => e!["estCorrige"]?.GetValue<bool>() ?? false));
            Assert.Equal(historique[2]!["id"]!.GetValue<int>(), historique[3]!["corrige"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Supprimer_une_candidature_de_la_base_emporte_son_historique_corrections_comprises()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var autre = IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-03T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        await api.CorrigerAvecSuccesAsync(id);
        await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null);
        await api.ChangerLeStatutAvecSuccesAsync(autre, "Refusee");

        // La suppression d'une candidature (#25) n'a pas encore de route : on supprime la ligne dans la base.
        await using (var portee = api.Services.CreateAsyncScope())
        {
            var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
            Assert.Equal(1, await baseDeSollicito.Candidatures.Where(c => c.Id == id).ExecuteDeleteAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, await CompterLesEvenementsAsync(api));
        Assert.Single((await api.FicheAsync(autre))["historique"]!.AsArray());
    }

    [Fact]
    public async Task La_base_refuse_qu_un_changement_soit_corrige_deux_fois()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        var corrige = (await api.CorrigerAvecSuccesAsync(id))["historique"]!.AsArray()[0]!["id"]!.GetValue<int>();

        await using var portee = api.Services.CreateAsyncScope();
        var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
        var erreur = await Assert.ThrowsAsync<SqliteException>(() => baseDeSollicito.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO EvenementsDeCandidature (CandidatureId, SurvenuLe, Sorte, Ancien, Nouveau, CorrigeId)
            VALUES ({id}, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Entretien', 'Envoyee', {corrige})
            """,
            TestContext.Current.CancellationToken));

        Assert.Contains("UNIQUE", erreur.Message, StringComparison.Ordinal);
    }

    // L'API, sur l'horloge du test, qu'il fait avancer.
    private Task<ApiLancee> LancerAsync() => ApiLancee.LancerAsync(_horloge.Utiliser);

    private static async Task<int> CompterLesEvenementsAsync(ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().EvenementsDeCandidature.CountAsync(TestContext.Current.CancellationToken);
    }

    private static string[] Textes(JsonNode? tableau) => [.. tableau!.AsArray().Select(texte => texte!.GetValue<string>())];

    private static string Passage(JsonNode? changement) =>
        changement!["ancien"]!.GetValue<string>() + ">" + changement["nouveau"]!.GetValue<string>();
}
