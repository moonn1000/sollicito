using System.Net;
using System.Text.Json.Nodes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Le dossier des sauvegardes (#26, ADR-0008) : choisi par le seul dialogue de la fenêtre hôte, il ne le devient qu'après
/// une copie réussie ; un dossier qui manque n'est jamais recréé ni remplacé ; le réglage vit hors de la base. Les tests
/// appellent le service comme la fenêtre, avec le chemin que rendrait le dialogue. Données fictives.
/// </summary>
public sealed class DossierDesSauvegardesTests : IAsyncLifetime
{
    private static readonly DateOnly Le18 = new(2026, 11, 18);

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
    private readonly string _ailleurs = Directory.CreateTempSubdirectory("sollicito-tests-cle-").FullName;

    private DossierDeLaBase DossierDeLaBase => new(_dossier);

    private string ParDefaut => DossierDeLaBase.DossierDesSauvegardes;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await SupprimerAsync(_dossier);
        await SupprimerAsync(_ailleurs);
    }

    [Fact]
    public async Task Un_dossier_choisi_recoit_aussitot_une_copie_et_devient_celui_des_sauvegardes()
    {
        string copie;
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
            await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
            copie = api.CopieDuJour(Le18);

            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));

            Assert.Equal([copie], Fichiers(_ailleurs));
            Assert.Equal(["Comptable"], PostesDe(Path.Combine(_ailleurs, copie)));
            var etat = await api.LireLesSauvegardesAsync();
            Assert.Equal(_ailleurs, etat["dossier"]!.GetValue<string>());
            Assert.False(etat["parDefaut"]!.GetValue<bool>());
            Assert.Equal(ParDefaut, etat["dossierParDefaut"]!.GetValue<string>());
            // Les copies de l'ancien dossier y restent.
            Assert.Equal([copie], Fichiers(ParDefaut));
        }

        // Le réglage vit dans le dossier de données, hors de la base : ni la base ni la copie n'en portent rien.
        var reglages = JsonNode.Parse(Texte(DossierDeLaBase.FichierDesReglages))!;
        Assert.Equal(_ailleurs, reglages["dossierDesSauvegardes"]!.GetValue<string>());
        var nomDuDossier = System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(_ailleurs));
        Assert.Equal(-1, Octets(DossierDeLaBase.Fichier).AsSpan().IndexOf(nomDuDossier));
        Assert.Equal(-1, Octets(Path.Combine(_ailleurs, copie)).AsSpan().IndexOf(nomDuDossier));

        // Relancé, Sollicito copie dans le dossier choisi ; le dossier par défaut ne reçoit plus de copie du jour.
        var avant = File.GetLastWriteTimeUtc(Path.Combine(ParDefaut, copie));
        await using (var relancee = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            var etat = await relancee.AttendreLaPremiereVerificationAsync();
            Assert.Equal((_ailleurs, false), (etat.Dossier, etat.ParDefaut));
        }

        Assert.Equal(avant, File.GetLastWriteTimeUtc(Path.Combine(ParDefaut, copie)));
        Assert.True(EstSaine(Path.Combine(_ailleurs, copie)));
    }

    [Fact]
    public async Task Un_dossier_choisi_absent_est_refuse_et_l_ancien_reste()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        var absent = Path.Combine(_ailleurs, "dossier-absent");

        Assert.Equal(IssueDuChoix.Refuse, await api.Sauvegardes().ChoisirLeDossierAsync(absent, TestContext.Current.CancellationToken));
        Assert.Equal(IssueDuChoix.Refuse, await api.Sauvegardes().ChoisirLeDossierAsync(@"relatif\dossier", TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(absent));
        Assert.False(File.Exists(DossierDeLaBase.FichierDesReglages));
        Assert.Equal((ParDefaut, true), (api.Sauvegardes().Etat.Dossier, api.Sauvegardes().Etat.ParDefaut));
    }

    [Fact]
    public async Task Un_dossier_choisi_qui_manque_n_est_ni_recree_ni_remplace_et_l_alerte_part_a_son_retour()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));
        var derniere = api.Sauvegardes().Etat.DerniereCopie;
        var parDefautAvant = Fichiers(ParDefaut);

        // La clé est retirée.
        await SupprimerAsync(_ailleurs);
        var etat = await api.SauvegarderMaintenantAsync();

        Assert.Equal(nameof(MotifDEchec.DossierIntrouvable), etat["echec"]!["motif"]!.GetValue<string>());
        Assert.False(Directory.Exists(_ailleurs));
        Assert.Equal(parDefautAvant, Fichiers(ParDefaut));
        Assert.Equal(_ailleurs, etat["dossier"]!.GetValue<string>());
        var depuis = etat["echec"]!["depuis"]!.GetValue<DateTime>();

        // L'essai de chaque heure échoue de même, sans déplacer le début de la série.
        await api.Sauvegardes().FaireLaCopieDuJourSiBesoinAsync(TestContext.Current.CancellationToken);
        Assert.Equal(depuis, api.Sauvegardes().Etat.Echec!.Depuis);
        Assert.Equal(derniere, api.Sauvegardes().Etat.DerniereCopie);

        // La clé revient : l'essai suivant refait la copie, et l'alerte part.
        Directory.CreateDirectory(_ailleurs);
        await api.Sauvegardes().FaireLaCopieDuJourSiBesoinAsync(TestContext.Current.CancellationToken);

        Assert.Null(api.Sauvegardes().Etat.Echec);
        Assert.Equal([api.CopieDuJour(Le18)], Fichiers(_ailleurs));
    }

    [Fact]
    public async Task Relance_alors_que_le_dossier_choisi_manque_Sollicito_dit_encore_depuis_quand_aucune_copie_n_a_reussi()
    {
        DateTime derniere;
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));
        }

        // La copie de la fermeture est la dernière réussie ; puis la clé est retirée, Sollicito fermé.
        derniere = File.GetLastWriteTimeUtc(Path.Combine(_ailleurs, Fichiers(_ailleurs).Single()));
        await SupprimerAsync(_ailleurs);

        await using (var relancee = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            var etat = await relancee.AttendreLaPremiereVerificationAsync();

            Assert.Equal(MotifDEchec.DossierIntrouvable, etat.Echec!.Motif);
            Assert.Equal(derniere, etat.DerniereCopie);
            Assert.Equal(derniere, (await relancee.LireLesSauvegardesAsync())["derniereCopie"]!.GetValue<DateTime>().ToUniversalTime());
        }

        // Le fichier ne porte qu'un instant, sans chemin.
        var garde = Texte(Path.Combine(_dossier, "sauvegardes.json"));
        Assert.DoesNotContain(Path.GetFileName(_ailleurs), garde, StringComparison.Ordinal);
        Assert.Equal(["derniereCopie"], JsonNode.Parse(garde)!.AsObject().Select(propriete => propriete.Key));
    }

    [Fact]
    public async Task Revenir_au_dossier_par_defaut_y_fait_une_copie_et_l_adopte()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));
        await EffacerAsync(Path.Combine(ParDefaut, api.CopieDuJour(Le18)));

        using var reponse = await api.RevenirAuDossierParDefautAsync();

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var etat = (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
        Assert.Equal((ParDefaut, true), (etat["dossier"]!.GetValue<string>(), etat["parDefaut"]!.GetValue<bool>()));
        Assert.Equal([api.CopieDuJour(Le18)], Fichiers(ParDefaut));
        Assert.Null(JsonNode.Parse(Texte(DossierDeLaBase.FichierDesReglages))!["dossierDesSauvegardes"]);
        // Les copies du dossier quitté y restent.
        Assert.Equal([api.CopieDuJour(Le18)], Fichiers(_ailleurs));
    }

    [Fact]
    public async Task Un_retour_au_dossier_par_defaut_impossible_est_refuse_et_l_ancien_dossier_reste()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));
        // Un fichier à la place du dossier par défaut : il ne peut plus se créer.
        await SupprimerAsync(ParDefaut);
        await File.WriteAllTextAsync(ParDefaut, "fictif", TestContext.Current.CancellationToken);

        using var reponse = await api.RevenirAuDossierParDefautAsync();

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal(
            "Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie.",
            (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Equal(_ailleurs, api.Sauvegardes().Etat.Dossier);
        Assert.Equal(_ailleurs, JsonNode.Parse(Texte(DossierDeLaBase.FichierDesReglages))!["dossierDesSauvegardes"]!.GetValue<string>());
    }

    [Fact]
    public async Task Un_reglage_illisible_n_arrete_pas_Sollicito_et_envoie_les_copies_au_dossier_par_defaut_en_le_disant()
    {
        Directory.CreateDirectory(_dossier);
        await File.WriteAllTextAsync(DossierDeLaBase.FichierDesReglages, "{ pas du json", TestContext.Current.CancellationToken);

        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        var etat = await api.AttendreLaPremiereVerificationAsync();

        Assert.True(etat.ReglageIllisible);
        Assert.True(etat.ParDefaut);
        Assert.Equal([api.CopieDuJour(Le18)], Fichiers(ParDefaut));
        Assert.True((await api.LireLesSauvegardesAsync())["reglageIllisible"]!.GetValue<bool>());

        // Un nouveau choix réécrit le réglage, et l'alerte part.
        Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));
        Assert.False(api.Sauvegardes().Etat.ReglageIllisible);
        Assert.Equal(_ailleurs, JsonNode.Parse(Texte(DossierDeLaBase.FichierDesReglages))!["dossierDesSauvegardes"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deux_installations_qui_partagent_un_dossier_ne_touchent_pas_aux_copies_l_une_de_l_autre()
    {
        var autreDossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await using var premiere = await LancerSurAsync(_dossier, new HorlogeDeTest());
            await using var seconde = await LancerSurAsync(autreDossier, new HorlogeDeTest());
            await premiere.AttendreLaPremiereVerificationAsync();
            await seconde.AttendreLaPremiereVerificationAsync();
            Assert.NotEqual(premiere.Sauvegardes().Installation, seconde.Sauvegardes().Installation);

            // La seconde a déjà huit jours de copies dans le dossier partagé.
            var siennes = Enumerable.Range(1, 8).Select(jour => NomsDesCopies.DuJour(seconde.Sauvegardes().Installation, new DateOnly(2026, 11, jour))).ToArray();
            foreach (var nom in siennes)
            {
                await File.WriteAllTextAsync(Path.Combine(_ailleurs, nom), "fictif", TestContext.Current.CancellationToken);
            }

            Assert.Equal(IssueDuChoix.Choisi, await premiere.Sauvegardes().ChoisirLeDossierAsync(_ailleurs, TestContext.Current.CancellationToken));

            Assert.Equal([.. siennes.Append(premiere.CopieDuJour(Le18)).Order(StringComparer.Ordinal)], Fichiers(_ailleurs));
            Assert.Equal([premiere.CopieDuJour(Le18)], premiere.Sauvegardes().Etat.Copies.Where(copie => copie.Sorte == SorteDeCopie.DuJour).Select(copie => copie.Nom));
        }
        finally
        {
            await SupprimerAsync(autreDossier);
        }
    }

    [Fact]
    public async Task Aucune_route_des_sauvegardes_ne_recoit_de_chemin()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();

        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);
        var chemins = (await AppelsDesCandidatures.LireAsync(reponse))["paths"]!.AsObject();

        var routes = chemins.Where(chemin => chemin.Key.StartsWith("/api/sauvegardes", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["/api/sauvegardes", "/api/sauvegardes/dossier-par-defaut"], routes.Select(route => route.Key).Order(StringComparer.Ordinal));
        foreach (var operation in routes.SelectMany(route => route.Value!.AsObject()))
        {
            Assert.Null(operation.Value!["requestBody"]);
            Assert.Null(operation.Value!["parameters"]);
        }

        // Un corps porteur d'un chemin ne change rien au dossier.
        using var avecUnChemin = await api.EnvoyerAsync(HttpMethod.Post, "/api/sauvegardes", jeton: true, requete =>
        {
            requete.Headers.Add("Origin", api.Origine);
            requete.Content = new StringContent($$"""{"dossier":"{{_ailleurs.Replace(@"\", @"\\", StringComparison.Ordinal)}}"}""", System.Text.Encoding.UTF8, "application/json");
        });
        Assert.Equal((ParDefaut, true), (api.Sauvegardes().Etat.Dossier, api.Sauvegardes().Etat.ParDefaut));
        Assert.Empty(Fichiers(_ailleurs));
    }
}
