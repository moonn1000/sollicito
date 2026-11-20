using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDuSuivi;

namespace Sollicito.Api.Tests;

/// <summary>
/// Corriger une étape (#25), par la route de l'API, sur une vraie base SQLite créée par les migrations dans un dossier
/// temporaire. Toutes les données sont fictives ; l'horloge part du 18/11/2026 à 14:30, heure de Bruxelles.
/// </summary>
public sealed class CorrectionDesEtapesTests
{
    private const string PasEnVigueur = "Cette étape n'est pas la version en vigueur : elle a déjà été corrigée, ou la fiche a changé entre-temps.";

    private readonly HorlogeDeTest _horloge = new();

    [Fact]
    public async Task La_correction_s_ajoute_a_l_historique_designe_l_etape_corrigee_et_ne_change_pas_le_statut()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien");
        var avant = await api.FicheAsync(id);

        var fiche = await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Entretien", "2026-11-12T11:00", "Premier entretien.");

        Assert.Equal(fiche.ToJsonString(), (await api.FicheAsync(id)).ToJsonString());
        foreach (var champ in new[] { "statut", "passagesPermis", "correction" })
        {
            Assert.Equal(avant[champ]?.ToJsonString(), fiche[champ]?.ToJsonString());
        }

        var historique = fiche["historique"]!.AsArray();
        Assert.Equal(3, historique.Count);
        var corrigee = historique.Single(evenement => evenement!["id"]!.GetValue<int>() == etape)!;
        Assert.True(corrigee["estCorrige"]!.GetValue<bool>());
        Assert.Equal(("Appel", "2026-11-10T09:00:00Z"), (corrigee["type"]!.GetValue<string>(), corrigee["survenuLe"]!.GetValue<string>()));
        var correction = historique.Single(evenement => evenement!["corrige"]?.GetValue<int>() == etape)!;
        Assert.Equal("etape", correction["sorte"]!.GetValue<string>());
        Assert.Equal(("Entretien", "2026-11-12T10:00:00Z", "Premier entretien."), (correction["type"]!.GetValue<string>(), correction["survenuLe"]!.GetValue<string>(), correction["note"]!.GetValue<string>()));
        Assert.False(correction["estCorrige"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Une_correction_se_corrige_a_son_tour_et_une_version_deja_corrigee_est_refusee()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        var premiere = IdDeLaCorrection(await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Appel", "2026-11-11T10:00", null), etape);
        var seconde = IdDeLaCorrection(await api.CorrigerUneEtapeAvecSuccesAsync(id, premiere, "Echange", "2026-11-12T10:00", "Courriel."), premiere);
        var avant = (await api.FicheAsync(id)).ToJsonString();

        foreach (var dejaCorrigee in new[] { etape, premiere })
        {
            using var reponse = await api.CorrigerUneEtapeAsync(id, dejaCorrigee, "Autre", "2026-11-13T10:00", null);
            Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
            var probleme = await AppelsDesCandidatures.LireAsync(reponse);
            Assert.Equal("L'étape n'est pas corrigée.", probleme["title"]!.GetValue<string>());
            Assert.Equal(PasEnVigueur, probleme["detail"]!.GetValue<string>());
        }

        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
        var historique = (await api.FicheAsync(id))["historique"]!.AsArray();
        Assert.Equal([true, true, false], historique.Select(evenement => evenement!["estCorrige"]!.GetValue<bool>()));
        Assert.Equal(seconde, historique[2]!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Une_etape_inconnue_ou_un_changement_de_statut_ne_se_corrigent_pas_comme_une_etape()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        var changement = (await api.ChangerLeStatutAvecSuccesAsync(id, "Entretien"))["correction"]!["changement"]!.GetValue<int>();
        var autre = IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-03T10:00"));
        var avant = await CompterLesEvenementsAsync(api);

        // Un changement de statut, une étape inconnue, une étape d'une autre candidature : aucune n'est une étape en vigueur ici.
        foreach (var (candidature, cible) in new[] { (id, changement), (id, etape + 100), (autre, etape) })
        {
            using var reponse = await api.CorrigerUneEtapeAsync(candidature, cible, "Appel", "2026-11-12T10:00", null);
            Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
            Assert.Equal(PasEnVigueur, (await AppelsDesCandidatures.LireAsync(reponse))["detail"]!.GetValue<string>());
        }

        using var introuvable = await api.CorrigerUneEtapeAsync(autre + 100, etape, "Appel", "2026-11-12T10:00", null);
        Assert.Equal(HttpStatusCode.NotFound, introuvable.StatusCode);
        Assert.Equal("Cette candidature n'existe pas.", (await AppelsDesCandidatures.LireAsync(introuvable))["title"]!.GetValue<string>());
        Assert.Equal(avant, await CompterLesEvenementsAsync(api));
    }

    [Theory]
    [MemberData(nameof(SuiviDesCandidaturesTests.EtapesRefusees), MemberType = typeof(SuiviDesCandidaturesTests))]
    public async Task Une_correction_invalide_est_refusee_champ_par_champ_et_ne_change_rien(string champ, string? valeur, string message)
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        var avant = (await api.FicheAsync(id)).ToJsonString();
        var saisie = new Dictionary<string, string?>
        {
            ["type"] = "Entretien",
            ["survenuLe"] = "2026-11-12T10:00",
            ["note"] = "Premier entretien.",
        };
        saisie[champ] = valeur;

        using var reponse = await api.CorrigerUneEtapeAsync(id, etape, saisie["type"], saisie["survenuLe"], saisie["note"]);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("La correction de l'étape n'est pas ajoutée à l'historique : corrigez les champs signalés.", probleme["title"]!.GetValue<string>());
        var refus = probleme["errors"]!.AsObject();
        Assert.Equal([champ], refus.Select(paire => paire.Key));
        Assert.Equal([message], refus[champ]!.AsArray().Select(texte => texte!.GetValue<string>()));
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Theory]
    [InlineData("""{"type":"Appel","survenuLe":"2026-11-12T10:00","statut":"Refusee"}""")]
    [InlineData("""{"type":"Appel","survenuLe":"2026-11-12T10:00","corrige":1}""")]
    [InlineData("""{"type":2,"survenuLe":"2026-11-12T10:00"}""")]
    [InlineData("""{"type":"Appel",""")]
    [InlineData("")]
    public async Task Un_corps_de_correction_illisible_ou_etranger_est_refuse(string corps)
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        var avant = (await api.FicheAsync(id)).ToJsonString();

        using var reponse = await api.EnvoyerALaCorrectionDEtapeAsync(id, etape, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("La requête est illisible.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Equal(avant, (await api.FicheAsync(id)).ToJsonString());
    }

    [Fact]
    public async Task Corrigee_une_etape_mal_datee_ne_borne_plus_la_date_d_envoi()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        using (var bornee = await api.ModifierAsync(id, "Lumière Verte SA", "Comptable", "2026-11-11T10:00"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, bornee.StatusCode);
        }

        await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Appel", "2026-11-15T10:00", null);

        var fiche = await api.ModifierAvecSuccesAsync(id, "Lumière Verte SA", "Comptable", "2026-11-11T10:00");
        Assert.Equal("2026-11-11T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
        // L'étape corrigée reste dans l'historique, plus ancienne que l'envoi : elle n'est plus en vigueur.
        var historique = fiche["historique"]!.AsArray();
        Assert.Equal("2026-11-10T09:00:00Z", historique[0]!["survenuLe"]!.GetValue<string>());
        Assert.True(historique[0]!["estCorrige"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Une_candidature_close_corrige_ses_etapes()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        await api.ChangerLeStatutAvecSuccesAsync(id, "SansReponse");

        var fiche = await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Appel", "2026-11-11T10:00", null);

        Assert.Equal("SansReponse", fiche["statut"]!.GetValue<string>());
        Assert.Empty(fiche["passagesPermis"]!.AsArray());
    }

    [Fact]
    public async Task Des_corrections_simultanees_d_une_meme_etape_n_en_gardent_qu_une()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);

        var reponses = await Task.WhenAll(Enumerable.Range(0, 6).Select(numero =>
            api.CorrigerUneEtapeAsync(id, etape, "Appel", $"2026-11-1{numero}T10:00", null)));

        Assert.Equal(1, reponses.Count(reponse => reponse.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, reponses.Count(reponse => reponse.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, await CompterLesEvenementsAsync(api));
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    [Fact]
    public async Task Supprimer_la_candidature_emporte_toute_la_chaine_des_corrections()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        var premiere = IdDeLaCorrection(await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Appel", "2026-11-11T10:00", null), etape);
        await api.CorrigerUneEtapeAvecSuccesAsync(id, premiere, "Appel", "2026-11-12T10:00", null);
        Assert.Equal(3, await CompterLesEvenementsAsync(api));

        using var suppression = await api.SupprimerAsync(id);

        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
        Assert.Equal(0, await CompterLesEvenementsAsync(api));
    }

    [Fact]
    public async Task La_base_refuse_qu_une_etape_soit_corrigee_deux_fois_ou_qu_une_correction_vise_un_evenement_absent()
    {
        await using var api = await LancerAsync();
        var (id, etape) = await CandidatureAvecUneEtapeAsync(api);
        await api.CorrigerUneEtapeAvecSuccesAsync(id, etape, "Appel", "2026-11-11T10:00", null);

        await using var portee = api.Services.CreateAsyncScope();
        var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
        var deuxFois = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => baseDeSollicito.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO EvenementsDeCandidature (CandidatureId, SurvenuLe, Sorte, Type, EtapeCorrigeeId)
            VALUES ({id}, '2026-11-12 09:00:00', 'Etape', 'Appel', {etape})
            """,
            TestContext.Current.CancellationToken));
        var absent = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => baseDeSollicito.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO EvenementsDeCandidature (CandidatureId, SurvenuLe, Sorte, Type, EtapeCorrigeeId)
            VALUES ({id}, '2026-11-12 09:00:00', 'Etape', 'Appel', {etape + 1000})
            """,
            TestContext.Current.CancellationToken));

        Assert.Contains("UNIQUE", deuxFois.Message, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY", absent.Message, StringComparison.Ordinal);
    }

    // L'API, sur l'horloge du test.
    private Task<ApiLancee> LancerAsync() => ApiLancee.LancerAsync(_horloge.Utiliser);

    // Une candidature envoyée le 02/11 à 10:00, et un appel noté le 10/11 à 10:00 : leurs identifiants.
    private static async Task<(int Id, int Etape)> CandidatureAvecUneEtapeAsync(ApiLancee api)
    {
        var id = IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00"));
        var fiche = await api.NoterUneEtapeAvecSuccesAsync(id, "Appel", "2026-11-10T10:00", null);
        return (id, Assert.Single(fiche["historique"]!.AsArray())!["id"]!.GetValue<int>());
    }

    // L'identifiant de la correction qui désigne l'étape, dans la fiche rendue.
    private static int IdDeLaCorrection(JsonObject fiche, int etape) =>
        fiche["historique"]!.AsArray().Single(evenement => evenement!["corrige"]?.GetValue<int>() == etape)!["id"]!.GetValue<int>();

    private static async Task<int> CompterLesEvenementsAsync(ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().EvenementsDeCandidature.CountAsync(TestContext.Current.CancellationToken);
    }
}
