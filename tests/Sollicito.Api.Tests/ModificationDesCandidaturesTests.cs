using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests;

/// <summary>
/// Modifier ou supprimer une candidature (#25), par les routes de l'API, sur une vraie base SQLite créée par les
/// migrations dans un dossier temporaire. Toutes les données sont fictives. L'horloge part du 18/11/2026 à 14:30, heure
/// de Bruxelles, et ne bouge que quand le test le dit.
/// </summary>
public sealed class ModificationDesCandidaturesTests
{
    private const string Refus = "La candidature n'est pas modifiée : corrigez les champs signalés.";

    // Une horloge par test : xUnit crée une instance de la classe pour chacun.
    private readonly HorlogeDeTest _horloge = new();

    [Fact]
    public async Task Modifier_corrige_l_entreprise_le_poste_et_la_date_d_envoi()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        var fiche = await api.ModifierAvecSuccesAsync(id, " Brasserie Lune Rousse ", " Brasseuse ", "2026-11-01T09:15");

        Assert.Equal(id, fiche["id"]!.GetValue<int>());
        Assert.Equal("Brasserie Lune Rousse", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("Brasseuse", fiche["poste"]!.GetValue<string>());
        Assert.Equal("2026-11-01T08:15:00Z", fiche["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Envoyee", fiche["statut"]!.GetValue<string>());
        // La fiche rendue est celle de la base, et le tableau de bord la montre à jour.
        Assert.Equal(fiche.ToJsonString(), (await api.FicheAsync(id)).ToJsonString());
        var listee = Assert.Single(await api.ListerAsync())!;
        Assert.Equal(
            ["Brasserie Lune Rousse", "Brasseuse", "2026-11-01T08:15:00Z", "Envoyee"],
            [listee["entreprise"]!["nom"]!.GetValue<string>(), listee["poste"]!.GetValue<string>(), listee["envoyeeLe"]!.GetValue<string>(), listee["statut"]!.GetValue<string>()]);
    }

    [Fact]
    public async Task Le_tableau_de_bord_range_la_candidature_modifiee_a_sa_nouvelle_date_d_envoi()
    {
        await using var api = await LancerAsync();
        var premiere = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-05T10:00");
        await api.EnregistrerAvecSuccesAsync("Les Amis du Parc ASBL", "Animateur", "2026-11-08T10:00");
        Assert.Equal(["Animateur", "Brasseuse", "Comptable"], Postes(await api.ListerAsync()));

        await api.ModifierAvecSuccesAsync(premiere, "Lumière Verte SA", "Comptable", "2026-11-10T10:00");

        Assert.Equal(["Comptable", "Animateur", "Brasseuse"], Postes(await api.ListerAsync()));
    }

    [Fact]
    public async Task Le_nom_saisi_est_reconnu_de_nouveau_par_sa_cle_et_l_entreprise_quittee_reste_en_base_sans_etre_proposee()
    {
        await using var api = await LancerAsync();
        var lumiere = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-03T10:00"));

        var fiche = await api.ModifierAvecSuccesAsync(id, "LUMIÈRE VERTE S.A.", "Brasseuse", "2026-11-03T10:00");

        // La candidature rejoint l'entreprise de même clé, qui garde le nom de sa première saisie.
        Assert.Equal(lumiere["entreprise"]!["id"]!.GetValue<int>(), fiche["entreprise"]!["id"]!.GetValue<int>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        // L'entreprise quittée n'a plus de candidature : elle reste en base, mais n'est plus proposée.
        Assert.Equal(["Lumière Verte SA"], Noms(await api.ListerLesEntreprisesAsync()));
        Assert.Equal(["Brasserie Lune Rousse", "Lumière Verte SA"], await EntreprisesEnBaseAsync(api));
    }

    [Fact]
    public async Task Une_autre_orthographe_de_la_meme_cle_ne_renomme_pas_l_entreprise()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumiere Verte", "Comptable", "2026-11-02T10:00"));

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-11-02T10:00");

        // Même clé, même entreprise : son nom affiché reste celui de la première saisie (limite écrite au modèle).
        Assert.Equal("Lumiere Verte", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal(["Lumiere Verte"], Noms(await api.ListerLesEntreprisesAsync()));
    }

    [Fact]
    public async Task Une_faute_corrigee_qui_change_la_cle_cree_une_entreprise_et_la_fautive_n_est_plus_proposee()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Vert SA", "Comptable", "2026-11-02T10:00"));
        var fautive = (await api.FicheAsync(id))["entreprise"]!["id"]!.GetValue<int>();

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-11-02T10:00");

        Assert.NotEqual(fautive, fiche["entreprise"]!["id"]!.GetValue<int>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        // L'entreprise fautive reste en base, mais aucune candidature ne la porte plus : elle n'est plus proposée.
        Assert.Equal(["Lumière Verte SA"], Noms(await api.ListerLesEntreprisesAsync()));
        Assert.Equal(["Lumière Vert SA", "Lumière Verte SA"], await EntreprisesEnBaseAsync(api));
    }

    [Theory]
    [MemberData(nameof(CandidaturesTests.SaisiesRefusees), MemberType = typeof(CandidaturesTests))]
    public async Task Une_modification_invalide_est_refusee_champ_par_champ_et_ne_change_rien(string champ, string? valeur, string message)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var avant = (await api.FicheAsync(id)).ToJsonString();
        // Les autres champs sont admis, et changeraient tout : une autre entreprise, un autre poste, une autre date.
        var saisie = new Dictionary<string, string?>
        {
            ["entreprise"] = "Brasserie Lune Rousse",
            ["poste"] = "Brasseuse",
            ["envoyeeLe"] = "2026-11-01T09:00",
        };
        saisie[champ] = valeur;

        using var reponse = await api.ModifierAsync(id, saisie["entreprise"], saisie["poste"], saisie["envoyeeLe"]);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(Refus, probleme["title"]!.GetValue<string>());
        var refus = probleme["errors"]!.AsObject();
        Assert.Equal([champ], refus.Select(paire => paire.Key));
        Assert.Equal([message], refus[champ]!.AsArray().Select(texte => texte!.GetValue<string>()));
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
        Assert.Equal(["Lumière Verte SA"], Noms(await api.ListerLesEntreprisesAsync()));
    }

    [Fact]
    public async Task Tous_les_champs_refuses_d_une_modification_sont_signales_ensemble()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));

        using var reponse = await api.EnvoyerALaModificationAsync(id, "{}");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var refus = (await AppelsDesCandidatures.LireAsync(reponse))["errors"]!.AsObject();
        Assert.Equal(["entreprise", "poste", "envoyeeLe"], refus.Select(paire => paire.Key));
    }

    [Theory]
    // Le statut n'est pas un champ : modifier une candidature ne le touche pas.
    [InlineData("""{"entreprise":"Lumière Verte SA","poste":"Comptable","envoyeeLe":"2026-11-02T10:00","statut":"Acceptee"}""")]
    [InlineData("""{"entreprise":"Lumière Verte SA","poste":"Comptable","envoyeeLe":"2026-11-02T10:00","id":42}""")]
    [InlineData("""{"entreprise":"Lumière Verte SA","poste":42,"envoyeeLe":"2026-11-02T10:00"}""")]
    [InlineData("""{"entreprise":"Lumière Verte SA",""")]
    [InlineData("pas du JSON")]
    [InlineData("")]
    public async Task Un_corps_de_modification_illisible_ou_qui_porte_un_statut_est_refuse(string corps)
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var avant = (await api.FicheAsync(id)).ToJsonString();

        using var reponse = await api.EnvoyerALaModificationAsync(id, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("La requête est illisible.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Fact]
    public async Task Un_corps_de_modification_qui_n_est_pas_du_JSON_est_refuse()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var avant = (await api.FicheAsync(id)).ToJsonString();

        using var reponse = await api.EnvoyerALaModificationAsync(id, """{"entreprise":"Brasserie Lune Rousse","poste":"Brasseuse","envoyeeLe":"2026-11-01T09:00"}""", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reponse.StatusCode);
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Fact]
    public async Task La_date_d_envoi_corrigee_ne_suit_pas_une_etape_deja_notee_mais_peut_tomber_a_son_instant()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null);
        var avant = (await api.FicheAsync(id)).ToJsonString();

        using (var refusee = await api.ModifierAsync(id, "Lumière Verte SA", "Comptable senior", "2026-11-10T10:01"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refusee.StatusCode);
            var refus = (await AppelsDesCandidatures.LireAsync(refusee))["errors"]!.AsObject();
            Assert.Equal(["envoyeeLe"], refus.Select(paire => paire.Key));
            Assert.Equal(
                "La date et l'heure d'envoi ne peuvent pas suivre le premier événement de l'historique : l'historique commence à l'envoi.",
                refus["envoyeeLe"]![0]!.GetValue<string>());
        }

        // Refusée, la modification ne change rien, pas même le poste, pourtant admis.
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable senior", "2026-11-10T10:00");
        Assert.Equal("2026-11-10T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("2026-11-10T09:00:00Z", Assert.Single(fiche["historique"]!.AsArray())!["survenuLe"]!.GetValue<string>());
    }

    [Fact]
    public async Task La_date_d_envoi_corrigee_ne_suit_pas_un_changement_de_statut_en_vigueur()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        // Le passage est daté par l'horloge, le 18/11 à 14:30.
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        _horloge.Avancer(TimeSpan.FromHours(2));

        using (var refusee = await api.ModifierAsync(id, "Lumière Verte SA", "Comptable", "2026-11-18T14:31"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refusee.StatusCode);
            var refus = (await AppelsDesCandidatures.LireAsync(refusee))["errors"]!.AsObject();
            Assert.Equal(["envoyeeLe"], refus.Select(paire => paire.Key));
        }

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-11-18T14:30");
        Assert.Equal("2026-11-18T13:30:00Z", fiche["envoyeeLe"]!.GetValue<string>());
    }

    [Fact]
    public async Task Un_changement_corrige_ne_borne_plus_la_date_d_envoi_mais_sa_correction_la_borne()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        // Le passage, le 18/11 à 14:30, puis sa correction, à 15:30 : seule la correction est en vigueur.
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        _horloge.Avancer(TimeSpan.FromHours(1));
        await api.CorrigerAvecSuccesAsync(id);
        _horloge.Avancer(TimeSpan.FromHours(1));

        Assert.Equal("2026-11-18T13:31:00Z", (await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-11-18T14:31"))["envoyeeLe"]!.GetValue<string>());
        using (var refusee = await api.ModifierAsync(id, "Lumière Verte SA", "Comptable", "2026-11-18T15:31"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refusee.StatusCode);
        }

        Assert.Equal("2026-11-18T14:30:00Z", (await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-11-18T15:30"))["envoyeeLe"]!.GetValue<string>());
    }

    [Fact]
    public async Task Une_date_d_envoi_renvoyee_inchangee_garde_ses_secondes()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-07-01T14:30:45"));

        // L'écran montre et renvoie la minute ; l'instant enregistré, à la seconde, ne change pas.
        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable senior", "2026-07-01T14:30");

        Assert.Equal("2026-07-01T12:30:45Z", fiche["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Comptable senior", fiche["poste"]!.GetValue<string>());
    }

    [Fact]
    public async Task Une_date_d_envoi_renvoyee_inchangee_dans_l_heure_qui_existe_deux_fois_garde_son_instant()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-10-25T02:30"));
        Assert.Equal("2026-10-25T01:30:00Z", (await api.FicheAsync(id))["envoyeeLe"]!.GetValue<string>());

        // À l'heure d'hiver, comme la saisie la lit : l'heure renvoyée revient au même instant.
        Assert.Equal("2026-10-25T01:30:00Z", (await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-10-25T02:30"))["envoyeeLe"]!.GetValue<string>());

        // Au premier passage, à l'heure d'été : 02:30 relu à l'heure d'hiver déplacerait l'envoi d'une heure. Aucune
        // saisie ne donne cet instant ; on l'écrit dans la base.
        await using (var portee = api.Services.CreateAsyncScope())
        {
            var heureDEte = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc);
            await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Candidatures
                .Where(candidature => candidature.Id == id)
                .ExecuteUpdateAsync(colonnes => colonnes.SetProperty(candidature => candidature.EnvoyeeLe, heureDEte), TestContext.Current.CancellationToken);
        }

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable senior", "2026-10-25T02:30");
        Assert.Equal("2026-10-25T00:30:00Z", fiche["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Comptable senior", fiche["poste"]!.GetValue<string>());
    }

    [Fact]
    public async Task Apres_une_horloge_reculee_corriger_le_seul_poste_n_est_pas_refuse_pour_sa_date()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-18T14:30"));
        // L'horloge de Windows est remise une heure en arrière : l'envoi enregistré semble alors dans le futur.
        _horloge.Avancer(TimeSpan.FromHours(-1));

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable senior", "2026-11-18T14:30");

        Assert.Equal("2026-11-18T13:30:00Z", fiche["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Comptable senior", fiche["poste"]!.GetValue<string>());
        // Une date vraiment changée repasse par les règles : la même heure plus une minute est dans le futur.
        using var refusee = await api.ModifierAsync(id, "Lumière Verte SA", "Comptable senior", "2026-11-18T14:31");
        Assert.Equal(HttpStatusCode.BadRequest, refusee.StatusCode);
    }

    [Fact]
    public async Task Modifier_ne_touche_ni_au_statut_ni_a_l_historique()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        _horloge.Avancer(TimeSpan.FromHours(1));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Proposition");
        _horloge.Avancer(TimeSpan.FromHours(1));
        await api.CorrigerAvecSuccesAsync(id);
        var avant = await api.NoterUneEtapeAvecSuccesAsync(id, "Entretien", "2026-11-10T10:00", "Premier entretien.");

        var apres = await api.ModifierAvecSuccesAsync(id, "Brasserie Lune Rousse", "Brasseuse", "2026-11-01T09:00");

        foreach (var champ in new[] { "statut", "passagesPermis", "correction", "historique" })
        {
            Assert.Equal(avant[champ]?.ToJsonString(), apres[champ]?.ToJsonString());
        }

        Assert.Equal(4, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task Une_candidature_close_se_modifie_et_se_supprime_comme_une_autre()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Refusee");

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Aide-comptable", "2026-11-02T10:00");
        Assert.Equal(("Aide-comptable", "Refusee"), (fiche["poste"]!.GetValue<string>(), fiche["statut"]!.GetValue<string>()));
        Assert.Empty(fiche["passagesPermis"]!.AsArray());

        using var suppression = await api.SupprimerAsync(id);
        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Fact]
    public async Task Une_candidature_qui_n_existe_pas_n_est_ni_modifiee_ni_supprimee()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var avant = (await api.FicheAsync(id)).ToJsonString();
        var inconnue = id + 1;

        using var modification = await api.ModifierAsync(inconnue, "Brasserie Lune Rousse", "Brasseuse", "2026-11-01T09:00");
        using var suppression = await api.SupprimerAsync(inconnue);

        foreach (var reponse in new[] { modification, suppression })
        {
            Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
            Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("Cette candidature n'existe pas.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        }

        // Rien ne s'est écrit : ni l'autre candidature, ni une entreprise.
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
        Assert.Equal(["Lumière Verte SA"], Noms(await api.ListerLesEntreprisesAsync()));
    }

    [Fact]
    public async Task Supprimer_retire_la_candidature_et_tout_son_historique_et_laisse_son_entreprise()
    {
        await using var api = await LancerAsync();
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var autre = IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-03T10:00"));
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        _horloge.Avancer(TimeSpan.FromHours(1));
        await api.CorrigerAvecSuccesAsync(id);
        await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", "Rappel.");
        await api.ChangerLeStatutAvecSuccesAsync(autre, "Refusee");
        var autreAvant = (await api.FicheAsync(autre)).ToJsonString();

        using (var suppression = await api.SupprimerAsync(id))
        {
            Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
            Assert.Empty(await suppression.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        }

        using (var lecture = await api.LireLaFicheAsync(id))
        {
            Assert.Equal(HttpStatusCode.NotFound, lecture.StatusCode);
        }

        Assert.Equal([autre], (await api.ListerAsync()).Select(candidature => candidature!["id"]!.GetValue<int>()));
        // Ses trois événements sont partis, correction et changement corrigé compris ; ceux de l'autre restent.
        Assert.Equal(1, await CompterLesEvenementsAsync(api));
        Assert.Equal(autreAvant, (await api.FicheAsync(autre)).ToJsonString());
        // Son entreprise reste en base ; sans candidature, elle n'est plus proposée.
        Assert.Equal(["Brasserie Lune Rousse"], Noms(await api.ListerLesEntreprisesAsync()));
        Assert.Equal(["Brasserie Lune Rousse", "Lumière Verte SA"], await EntreprisesEnBaseAsync(api));

        using var encore = await api.SupprimerAsync(id);
        Assert.Equal(HttpStatusCode.NotFound, encore.StatusCode);
    }

    [Fact]
    public async Task Des_modifications_simultanees_vers_un_meme_nouveau_nom_ne_creent_qu_une_entreprise()
    {
        await using var api = await LancerAsync();
        var ids = new List<int>();
        for (var numero = 0; numero < 8; numero++)
        {
            ids.Add(IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", $"Poste {numero}", "2026-11-02T10:00")));
        }

        string[] noms = ["Brasserie Lune Rousse S.R.L.", "BRASSERIE LUNE ROUSSE", "Brasserie Lune Rousse SRL", "brasserie lune rousse"];
        var reponses = await Task.WhenAll(ids.Select((id, numero) => api.ModifierAsync(id, noms[numero % noms.Length], $"Poste {numero}", "2026-11-02T10:00")));

        Assert.All(reponses, reponse => Assert.Equal(HttpStatusCode.OK, reponse.StatusCode));
        // Une seule entreprise de plus, sous le nom de la première modification passée, et toutes les candidatures y vont.
        Assert.Equal(2, (await EntreprisesEnBaseAsync(api)).Length);
        Assert.Single(await api.ListerLesEntreprisesAsync());
        var entreprises = new HashSet<int>();
        foreach (var id in ids)
        {
            entreprises.Add((await api.FicheAsync(id))["entreprise"]!["id"]!.GetValue<int>());
        }

        Assert.Single(entreprises);
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Fact]
    public async Task Une_modification_de_la_date_d_envoi_et_une_etape_simultanees_ne_laissent_jamais_une_etape_avant_l_envoi()
    {
        await using var api = await LancerAsync();
        var ids = new List<int>();
        for (var numero = 0; numero < 10; numero++)
        {
            ids.Add(IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", $"Poste {numero}", "2026-11-02T10:00")));
        }

        // Pour chaque candidature, en même temps : l'envoi repoussé au 12/11, et une étape notée au 10/11, entre les deux.
        var envois = ids.Select(id => (Id: id, Modification: api.ModifierAsync(id, "Lumière Verte SA", "Poste", "2026-11-12T10:00"), Etape: api.NoterUneEtapeAsync(id, "Appel", "2026-11-10T10:00", null))).ToList();
        await Task.WhenAll(envois.SelectMany(envoi => new[] { envoi.Modification, envoi.Etape }));

        foreach (var (id, modification, etape) in envois)
        {
            using var reponseDeLaModification = await modification;
            using var reponseDeLEtape = await etape;
            // L'une des deux passe, l'autre est refusée : jamais les deux.
            Assert.Equal(1, new[] { reponseDeLaModification, reponseDeLEtape }.Count(reponse => reponse.StatusCode == HttpStatusCode.OK));
            Assert.All(new[] { reponseDeLaModification, reponseDeLEtape }, reponse => Assert.Contains(reponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }));

            var fiche = await api.FicheAsync(id);
            var envoyeeLe = DateTime.Parse(fiche["envoyeeLe"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
            Assert.All(fiche["historique"]!.AsArray(), evenement => Assert.True(
                DateTime.Parse(evenement!["survenuLe"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal) >= envoyeeLe));
        }
    }

    // L'API, sur l'horloge du test, qu'il fait avancer.
    private Task<ApiLancee> LancerAsync() => ApiLancee.LancerAsync(_horloge.Utiliser);

    private static async Task<int> CompterLesEvenementsAsync(ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().EvenementsDeCandidature.CountAsync(TestContext.Current.CancellationToken);
    }

    // Toutes les entreprises de la base, proposées ou non, dans l'ordre de leur nom.
    private static async Task<string[]> EntreprisesEnBaseAsync(ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        var noms = await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Entreprises
            .Select(entreprise => entreprise.Nom)
            .ToListAsync(TestContext.Current.CancellationToken);
        return [.. noms.Order(StringComparer.Ordinal)];
    }

    private static string[] Postes(JsonArray candidatures) => [.. candidatures.Select(candidature => candidature!["poste"]!.GetValue<string>())];

    private static string[] Noms(JsonArray entreprises) => [.. entreprises.Select(entreprise => entreprise!["nom"]!.GetValue<string>())];
}
