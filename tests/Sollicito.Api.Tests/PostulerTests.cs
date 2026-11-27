using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La liste des offres et « Postuler » (#38, ADR-0014), par l'API sur une vraie base SQLite : la liste sans texte, les
/// plus récentes d'abord, avec leur candidature ; la candidature qui reprend l'entreprise et le poste de l'offre ; au plus
/// une candidature par offre, même sous dix « Postuler » simultanés ; l'offre sans entreprise ; les refus, qui ne créent
/// rien ; le lien qui survit aux corrections et disparaît avec la candidature. Toutes les données sont fictives ;
/// l'horloge est arrêtée le 18/11/2026 à 14:30, heure de Bruxelles.
/// </summary>
public sealed class PostulerTests
{
    // Les titres des réponses, mot pour mot.
    private const string TitreDejaPostulee = "Cette offre a déjà sa candidature : rien n'a été ajouté.";
    private const string TitreDeLEntrepriseManquante = "Indiquez d'abord l'entreprise de l'offre : la candidature la reprend.";
    private const string TitreDuPosteARevoir = "Le poste de l'offre ne peut pas être repris tel quel : corrigez l'offre, puis postulez.";
    private const string TitreDuRefus = "La candidature n'est pas enregistrée : corrigez les champs signalés.";
    private const string TitreDeLOffreIntrouvable = "Cette offre n'existe pas.";

    [Fact]
    public async Task La_liste_rend_toutes_les_offres_sans_leur_texte_les_plus_recentes_d_abord_avec_leur_candidature()
    {
        var horloge = new HorlogeDeTest();
        await using var api = await ApiLancee.LancerAsync(horloge.Utiliser);
        var premiere = Id(await api.CollerAvecSuccesAsync("Première offre fictive.", "Comptable", entreprise: "Lumière Verte SA", lieu: "Liège", publieeLe: "2026-11-17"));
        horloge.Avancer(TimeSpan.FromMinutes(5));
        var anonyme = Id(await api.CollerAvecSuccesAsync("Offre anonyme fictive.", "Chauffeur"));
        // Deux offres au même instant : la dernière enregistrée vient d'abord.
        var memeInstant = Id(await api.CollerAvecSuccesAsync("Offre fictive du même instant.", "Brasseuse", entreprise: "Brasserie Lune Rousse"));
        var candidature = Id(await api.PostulerAvecSuccesAsync(premiere));

        var liste = await api.ListerLesOffresAsync();

        Assert.Equal([memeInstant, anonyme, premiere], Ids(liste));
        var postulee = liste[2]!.AsObject();
        Assert.Equal(
            ["id", "source", "entreprise", "poste", "lieu", "publieeLe", "ajouteeLe", "candidature"],
            postulee.Select(champ => champ.Key));
        Assert.Equal("TexteColle", postulee["source"]!.GetValue<string>());
        Assert.Equal("Lumière Verte SA", postulee["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("Comptable", postulee["poste"]!.GetValue<string>());
        Assert.Equal("Liège", postulee["lieu"]!.GetValue<string>());
        Assert.Equal("2026-11-17", postulee["publieeLe"]!.GetValue<string>());
        Assert.Equal("2026-11-18T13:30:00Z", postulee["ajouteeLe"]!.GetValue<string>());
        Assert.Equal(candidature, Id(postulee["candidature"]));
        Assert.Equal("Envoyee", postulee["candidature"]!["statut"]!.GetValue<string>());
        Assert.Equal("2026-11-18T09:00:00Z", postulee["candidature"]!["envoyeeLe"]!.GetValue<string>());
        Assert.Null(liste[1]!["entreprise"]);
        Assert.Null(liste[1]!["candidature"]);
        Assert.All(liste, offre => Assert.False(offre!.AsObject().ContainsKey("texte")));
    }

    [Fact]
    public async Task Sans_offre_la_liste_est_vide()
    {
        await using var api = await LancerAsync();

        Assert.Empty(await api.ListerLesOffresAsync());
    }

    [Fact]
    public async Task Postuler_cree_une_candidature_envoyee_qui_reprend_l_entreprise_et_le_poste_de_l_offre_et_la_designe()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Développeur .NET (H/F)", entreprise: "Lumière Verte SA", lieu: "Liège"));

        var candidature = await api.PostulerAvecSuccesAsync(offre, "2026-11-18T10:00");

        Assert.Equal("Lumière Verte SA", candidature["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("Développeur .NET (H/F)", candidature["poste"]!.GetValue<string>());
        Assert.Equal("2026-11-18T09:00:00Z", candidature["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Envoyee", candidature["statut"]!.GetValue<string>());
        // L'entreprise est celle de l'offre : aucune autre ne se crée.
        Assert.Equal(["Lumière Verte SA"], await api.EntreprisesEnBaseAsync());

        var fiche = await api.FicheAsync(Id(candidature));
        Assert.Equal(offre, fiche["offre"]!.GetValue<int>());
        // Comme toute candidature, elle part d'Envoyée sans événement : l'envoi n'en est pas un.
        Assert.Empty(fiche["historique"]!.AsArray());
        var ficheDeLOffre = await api.LireLOffreAsync(offre);
        Assert.Equal(Id(candidature), Id(ficheDeLOffre["candidature"]));
        Assert.Equal("Envoyee", ficheDeLOffre["candidature"]!["statut"]!.GetValue<string>());
        Assert.Equal([Id(candidature)], (await api.ListerAsync()).Select(listee => Id(listee)));
    }

    [Fact]
    public async Task Une_candidature_saisie_sans_offre_ne_designe_aucune_offre()
    {
        await using var api = await LancerAsync();

        var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-18T10:00");

        Assert.Null((await api.FicheAsync(Id(candidature)))["offre"]);
    }

    [Fact]
    public async Task Une_offre_qui_a_sa_candidature_n_en_recoit_pas_une_seconde_meme_avec_une_date_refusee()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        var premiere = Id(await api.PostulerAvecSuccesAsync(offre));

        foreach (var envoyeeLe in new[] { "2026-11-18T11:00", "illisible" })
        {
            using var reponse = await api.PostulerAsync(offre, envoyeeLe);

            Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
            var probleme = await AppelsDesCandidatures.LireAsync(reponse);
            Assert.Equal(TitreDejaPostulee, probleme["title"]!.GetValue<string>());
            Assert.Equal("dejaPostulee", probleme["motif"]!.GetValue<string>());
            Assert.Equal(premiere, probleme["candidature"]!.GetValue<int>());
        }

        Assert.Single(await api.ListerAsync());
    }

    [Fact]
    public async Task Dix_Postuler_simultanes_sur_une_meme_offre_n_en_creent_qu_une()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));

        var statuts = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var reponse = await api.PostulerAsync(offre, "2026-11-18T10:00");
            return reponse.StatusCode;
        }));

        Assert.Equal(1, statuts.Count(statut => statut == HttpStatusCode.Created));
        Assert.Equal(9, statuts.Count(statut => statut == HttpStatusCode.Conflict));
        Assert.Single(await api.ListerAsync());
    }

    [Fact]
    public async Task Une_offre_sans_entreprise_en_demande_une_avant_par_sa_correction()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre anonyme fictive.", "Chauffeur"));

        using (var refus = await api.PostulerAsync(offre, "2026-11-18T10:00"))
        {
            Assert.Equal(HttpStatusCode.Conflict, refus.StatusCode);
            var probleme = await AppelsDesCandidatures.LireAsync(refus);
            Assert.Equal(TitreDeLEntrepriseManquante, probleme["title"]!.GetValue<string>());
            Assert.Equal("entrepriseManquante", probleme["motif"]!.GetValue<string>());
        }

        Assert.Empty(await api.ListerAsync());
        Assert.Empty(await api.EntreprisesEnBaseAsync());

        // Sa correction lui donne une entreprise, reconnue par sa clé ou créée (#36) : « Postuler » la reprend.
        using (var correction = await api.CorrigerLOffreAsync(offre, "Chauffeur", entreprise: "Brasserie Lune Rousse"))
        {
            Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        }

        var candidature = await api.PostulerAvecSuccesAsync(offre);
        Assert.Equal("Brasserie Lune Rousse", candidature["entreprise"]!["nom"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null, "Indiquez la date et l'heure d'envoi.")]
    [InlineData("  ", "Indiquez la date et l'heure d'envoi.")]
    [InlineData("18/11/2026 10:00", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30.")]
    [InlineData("2026-11-18T10:00Z", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30.")]
    [InlineData("2026-11-18T15:00", "La date et l'heure d'envoi ne peuvent pas être dans le futur.")]
    [InlineData("1999-12-31T23:59", "La date d'envoi ne peut pas être antérieure au 1er janvier 2000.")]
    [InlineData("2026-03-29T02:30", "Cette heure n'existe pas : ce jour-là, au passage à l'heure d'été, l'horloge avance d'une heure.")]
    public async Task Une_date_d_envoi_refusee_ne_cree_ni_candidature_ni_entreprise(string? envoyeeLe, string message)
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));

        using var reponse = await api.PostulerAsync(offre, envoyeeLe);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(TitreDuRefus, probleme["title"]!.GetValue<string>());
        Assert.Equal([message], probleme["errors"]!["envoyeeLe"]!.AsArray().Select(refus => refus!.GetValue<string>()));
        Assert.Equal(["envoyeeLe"], probleme["errors"]!.AsObject().Select(champ => champ.Key));
        Assert.Empty(await api.ListerAsync());
        Assert.Equal(["Lumière Verte SA"], await api.EntreprisesEnBaseAsync());
        Assert.Null((await api.LireLOffreAsync(offre))["candidature"]);
    }

    [Fact]
    public async Task Une_offre_inconnue_est_introuvable_meme_avec_une_date_refusee()
    {
        await using var api = await LancerAsync();

        foreach (var envoyeeLe in new[] { "2026-11-18T10:00", "illisible" })
        {
            using var reponse = await api.PostulerAsync(999, envoyeeLe);

            Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
            Assert.Equal(TitreDeLOffreIntrouvable, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        }

        Assert.Empty(await api.ListerAsync());
        Assert.Empty(await api.EntreprisesEnBaseAsync());
    }

    [Theory]
    [InlineData("Comptable\u0007")]
    [InlineData("Comptable‮")]
    public async Task Un_poste_d_offre_que_la_candidature_ne_peut_pas_reprendre_est_refuse_sans_erreur(string poste)
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        // Une base restaurée, retouchée hors de Sollicito, peut porter n'importe quel poste : la règle de l'écran ne la garde plus.
        await api.ExecuterAsync($"UPDATE Offres SET Poste = '{poste}' WHERE Id = {offre}");

        using var reponse = await api.PostulerAsync(offre, "2026-11-18T10:00");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(TitreDuPosteARevoir, probleme["title"]!.GetValue<string>());
        Assert.Equal("posteARevoir", probleme["motif"]!.GetValue<string>());
        Assert.Empty(await api.ListerAsync());
    }

    [Fact]
    public async Task La_base_refuse_seule_une_seconde_candidature_de_la_meme_offre()
    {
        // Suites de #38 (#98, point 21) : sans passer par l'API, une copie de la candidature se heurte à l'index unique.
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        var candidature = Id(await api.PostulerAvecSuccesAsync(offre));

        var refus = await Assert.ThrowsAsync<SqliteException>(() => api.ExecuterAsync(
            $"CREATE TEMP TABLE Copie AS SELECT * FROM Candidatures WHERE Id = {candidature}; UPDATE Copie SET Id = NULL; "
            + "INSERT INTO Candidatures SELECT * FROM Copie"));

        Assert.Equal(19, refus.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: Candidatures.OffreId", refus.Message, StringComparison.Ordinal);
        Assert.Single(await api.ListerAsync());
    }

    [Fact]
    public async Task Un_poste_d_offre_trop_long_pour_une_candidature_est_refuse_sans_erreur()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        await api.ExecuterAsync($"UPDATE Offres SET Poste = '{new string('a', 201)}' WHERE Id = {offre}");

        using var reponse = await api.PostulerAsync(offre, "2026-11-18T10:00");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal("posteARevoir", (await AppelsDesCandidatures.LireAsync(reponse))["motif"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"envoyeeLe":"2026-11-18T10:00","entreprise":"Autre entreprise"}""")]
    [InlineData("""{"envoyeeLe":"2026-11-18T10:00","poste":"Autre poste"}""")]
    [InlineData("""{"envoyeeLe":"2026-11-18T10:00","statut":"Entretien"}""")]
    [InlineData("""{"envoyeeLe":"2026-11-18T10:00","offre":2}""")]
    [InlineData("""{"envoyeeLe":202611181000}""")]
    [InlineData("""{"envoyeeLe":["2026-11-18T10:00"]}""")]
    [InlineData("""["2026-11-18T10:00"]""")]
    [InlineData("""{"envoyeeLe":"2026-11-18T10:00" """)]
    public async Task Un_corps_qui_porte_autre_chose_que_la_date_est_illisible_et_rien_ne_s_ecrit(string corps)
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));

        using var reponse = await api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(offre), corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Empty(await api.ListerAsync());
        Assert.Equal(["Lumière Verte SA"], await api.EntreprisesEnBaseAsync());
    }

    [Fact]
    public async Task Un_corps_de_plus_de_16_Kio_est_refuse_avant_d_etre_lu()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));

        // Juste sous la borne, le corps se lit (et sa date, trop longue, est refusée) ; au-delà, 413.
        using var sousLaBorne = await api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(offre), CorpsDePostuler(new string('a', (16 * 1024) - 20)));
        using var auDela = await api.EcrireAsync(HttpMethod.Post, AdresseDePostuler(offre), CorpsDePostuler(new string('a', 16 * 1024)));

        Assert.Equal(HttpStatusCode.BadRequest, sousLaBorne.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, auDela.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Fact]
    public async Task L_offre_et_sa_candidature_vivent_chacune_leur_vie_sans_perdre_leur_lien()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        var candidature = Id(await api.PostulerAvecSuccesAsync(offre));

        // Corriger l'offre ne change pas sa candidature.
        using (var correction = await api.CorrigerLOffreAsync(offre, "Comptable confirmée", entreprise: "Brasserie Lune Rousse"))
        {
            Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        }

        var fiche = await api.FicheAsync(candidature);
        Assert.Equal("Comptable", fiche["poste"]!.GetValue<string>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal(offre, fiche["offre"]!.GetValue<int>());

        // Modifier la candidature ne change pas son offre.
        await api.ModifierAvecSuccesAsync(candidature, "Atelier d'Ondine", "Aide-comptable", "2026-11-18T09:00");
        var ficheDeLOffre = await api.LireLOffreAsync(offre);
        Assert.Equal("Comptable confirmée", ficheDeLOffre["poste"]!.GetValue<string>());
        Assert.Equal("Brasserie Lune Rousse", ficheDeLOffre["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal(candidature, Id(ficheDeLOffre["candidature"]));
        Assert.Equal(offre, (await api.FicheAsync(candidature))["offre"]!.GetValue<int>());
    }

    [Fact]
    public async Task Supprimer_la_candidature_laisse_l_offre_qui_peut_en_recevoir_une_nouvelle_et_son_entreprise_reste_proposee()
    {
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Brasseuse", entreprise: "Brasserie Lune Rousse"));
        var candidature = Id(await api.PostulerAvecSuccesAsync(offre));

        using (var suppression = await api.SupprimerAsync(candidature))
        {
            Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
        }

        var ficheDeLOffre = await api.LireLOffreAsync(offre);
        Assert.Null(ficheDeLOffre["candidature"]);
        Assert.Null((await api.ListerLesOffresAsync())[0]!["candidature"]);
        // L'offre seule désigne encore l'entreprise : elle reste proposée.
        Assert.Equal(["Brasserie Lune Rousse"], (await api.ListerLesEntreprisesAsync()).Select(entreprise => entreprise!["nom"]!.GetValue<string>()));

        var nouvelle = await api.PostulerAvecSuccesAsync(offre, "2026-11-18T11:00");
        Assert.NotEqual(candidature, Id(nouvelle));
        Assert.Equal(Id(nouvelle), Id((await api.LireLOffreAsync(offre))["candidature"]));
    }

    [Fact]
    public async Task Un_doublon_possible_deja_postule_se_montre_dans_la_fiche_de_l_offre()
    {
        await using var api = await LancerAsync();
        var premiere = Id(await api.CollerAvecSuccesAsync("Première offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        var candidature = Id(await api.PostulerAvecSuccesAsync(premiere));
        var seconde = Id(await api.CollerAvecSuccesAsync("Seconde offre fictive.", "Comptable", entreprise: "Lumière Verte SA", garderLesDeux: true));

        var doublon = Assert.Single((await api.LireLOffreAsync(seconde))["doublonsPossibles"]!.AsArray())!;

        Assert.Equal(premiere, Id(doublon));
        Assert.Equal(candidature, Id(doublon["candidature"]));
        Assert.Equal("Envoyee", doublon["candidature"]!["statut"]!.GetValue<string>());
        // Et dans l'autre sens : la première montre la seconde, sans candidature.
        Assert.Null(Assert.Single((await api.LireLOffreAsync(premiere))["doublonsPossibles"]!.AsArray())!["candidature"]);
    }

    [Fact]
    public async Task Le_renommage_d_une_entreprise_compte_aussi_les_offres_qui_la_designent()
    {
        await using var api = await LancerAsync();
        var candidature = Id(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-18T10:00"));
        await api.CollerAvecSuccesAsync("Première offre fictive.", "Chauffeur", entreprise: "Lumière Verte SA");
        await api.CollerAvecSuccesAsync("Seconde offre fictive.", "Brasseuse", entreprise: "LUMIERE VERTE");
        await api.CollerAvecSuccesAsync("Offre fictive d'ailleurs.", "Brasseuse", entreprise: "Brasserie Lune Rousse");

        using var reponse = await api.ModifierAsync(candidature, "Lumière Verte S.A.", "Comptable", "2026-11-18T10:00");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(1, probleme["candidatures"]!.GetValue<int>());
        Assert.Equal(2, probleme["offres"]!.GetValue<int>());
    }

    [Fact]
    public async Task Le_renommage_d_une_entreprise_qu_aucune_offre_ne_designe_compte_zero_offre()
    {
        await using var api = await LancerAsync();
        var candidature = Id(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-18T10:00"));

        using var reponse = await api.ModifierAsync(candidature, "Lumière Verte S.A.", "Comptable", "2026-11-18T10:00");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal(0, (await AppelsDesCandidatures.LireAsync(reponse))["offres"]!.GetValue<int>());
    }

    private static Task<ApiLancee> LancerAsync() => ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
}
