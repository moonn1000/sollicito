using System.Net;
using System.Text.Json.Nodes;
using Sollicito.Api.Relances;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Domain.Relances;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Le délai par défaut des relances (#14, ADR-0011), dans reglages.json : 7 jours d'usine, de 1 à 365, lu à part du
/// dossier des sauvegardes ; une écriture qui ne change que lui ; un fichier illisible en entier, qu'il ne réécrit pas.
/// </summary>
public sealed class ReglageDesRelancesTests : IDisposable
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private DossierDeLaBase DossierDeLaBase => new(_dossier);

    [Fact]
    public async Task Sans_reglage_le_delai_est_de_7_jours()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        var reglage = await api.ReglageDesRelancesAsync();

        Assert.Equal("""{"delaiParDefaut":7,"delaiLisible":true,"fichierLisible":true}""", reglage.ToJsonString());
    }

    [Fact]
    public async Task Le_delai_change_se_garde_hors_de_la_base_et_sert_aux_relances_planifiees_ensuite()
    {
        Directory.CreateDirectory(_dossier);
        await File.WriteAllTextAsync(DossierDeLaBase.FichierDesReglages, """{ "fournisseurDIA": "Ollama" }""", TestContext.Current.CancellationToken);
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        var avant = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T09:00"));
        await api.PlanifierAvecSuccesAsync(avant);

        using var reponse = await api.ChangerLeDelaiParDefautAsync(10);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(10, (await AppelsDesCandidatures.LireAsync(reponse))["delaiParDefaut"]!.GetValue<int>());
        Assert.Equal(10, (await api.ReglageDesRelancesAsync())["delaiParDefaut"]!.GetValue<int>());
        var fichier = JsonNode.Parse(await File.ReadAllTextAsync(DossierDeLaBase.FichierDesReglages, TestContext.Current.CancellationToken))!.AsObject();
        Assert.Equal(10, fichier["delaiDesRelances"]!.GetValue<int>());
        Assert.Equal("Ollama", fichier["fournisseurDIA"]!.GetValue<string>());

        // La relance planifiée avant garde son échéance ; la suivante prend le nouveau délai.
        Assert.Equal("2026-11-17", RelanceAFaire(await api.FicheAsync(avant))!["echeance"]!.GetValue<string>());
        var apres = AppelsDuSuivi.IdDe(await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-11-10T09:00"));
        Assert.Equal("2026-11-20", RelanceAFaire(await api.PlanifierAvecSuccesAsync(apres))!["echeance"]!.GetValue<string>());
    }

    [Fact]
    public async Task Changer_le_delai_garde_le_dossier_des_sauvegardes_et_le_choix_d_un_dossier_garde_le_delai()
    {
        var ailleurs = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
            await api.AttendreLaPremiereVerificationAsync();
            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(ailleurs, TestContext.Current.CancellationToken));

            using (var reponse = await api.ChangerLeDelaiParDefautAsync(21))
            {
                Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
            }

            Assert.Equal(ailleurs, JsonNode.Parse(await File.ReadAllTextAsync(DossierDeLaBase.FichierDesReglages, TestContext.Current.CancellationToken))!["dossierDesSauvegardes"]!.GetValue<string>());
            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().RevenirAuDossierParDefautAsync(TestContext.Current.CancellationToken));
            Assert.Equal(21, (await api.ReglageDesRelancesAsync())["delaiParDefaut"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(ailleurs, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, DelaiDeRelance.HorsBornes)]
    [InlineData(366, DelaiDeRelance.HorsBornes)]
    [InlineData(null, "Indiquez le délai par défaut, en jours.")]
    public async Task Un_delai_hors_bornes_ou_absent_est_refuse_sans_rien_ecrire(int? jours, string message)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        using var reponse = await api.ChangerLeDelaiParDefautAsync(jours);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var corps = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("Le délai par défaut n'a pas changé : corrigez le champ signalé.", corps["title"]!.GetValue<string>());
        Assert.Equal(message, corps["errors"]!["delaiParDefaut"]![0]!.GetValue<string>());
        Assert.Equal(7, (await api.ReglageDesRelancesAsync())["delaiParDefaut"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("\"10\"")]
    [InlineData("10.5")]
    [InlineData("0")]
    [InlineData("1000")]
    public async Task Un_delai_illisible_vaut_7_jours_sans_toucher_au_dossier_des_sauvegardes(string valeur)
    {
        Directory.CreateDirectory(_dossier);
        await File.WriteAllTextAsync(DossierDeLaBase.FichierDesReglages, $$"""{ "delaiDesRelances": {{valeur}} }""", TestContext.Current.CancellationToken);
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        Assert.Equal("""{"delaiParDefaut":7,"delaiLisible":false,"fichierLisible":true}""", (await api.ReglageDesRelancesAsync()).ToJsonString());
        Assert.False((await api.AttendreLaPremiereVerificationAsync()).ReglageIllisible);

        // Un nouveau délai le remplace.
        using var reponse = await api.ChangerLeDelaiParDefautAsync(12);
        Assert.Equal("""{"delaiParDefaut":12,"delaiLisible":true,"fichierLisible":true}""", (await AppelsDesCandidatures.LireAsync(reponse)).ToJsonString());
    }

    [Fact]
    public async Task Sur_un_fichier_illisible_en_entier_le_delai_ne_s_ecrit_pas_et_l_alerte_des_sauvegardes_reste()
    {
        Directory.CreateDirectory(_dossier);
        await File.WriteAllTextAsync(DossierDeLaBase.FichierDesReglages, "{ pas du json", TestContext.Current.CancellationToken);
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        Assert.True((await api.AttendreLaPremiereVerificationAsync()).ReglageIllisible);
        Assert.Equal("""{"delaiParDefaut":7,"delaiLisible":false,"fichierLisible":false}""", (await api.ReglageDesRelancesAsync()).ToJsonString());

        using var reponse = await api.ChangerLeDelaiParDefautAsync(10);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal("Le délai par défaut n'a pas changé : les réglages n'ont pas pu être relus. Choisissez de nouveau le dossier des " + "sauvegardes, qui les réécrit, puis réglez le délai.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Equal("{ pas du json", await File.ReadAllTextAsync(DossierDeLaBase.FichierDesReglages, TestContext.Current.CancellationToken));
        Assert.True((await api.LireLesSauvegardesAsync())["reglageIllisible"]!.GetValue<bool>());

        // Le choix d'un dossier réécrit le fichier : le délai s'écrit ensuite.
        Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().RevenirAuDossierParDefautAsync(TestContext.Current.CancellationToken));
        using var ensuite = await api.ChangerLeDelaiParDefautAsync(10);
        Assert.Equal(HttpStatusCode.OK, ensuite.StatusCode);
    }

    [Fact]
    public async Task Le_service_refuse_un_delai_hors_bornes()
    {
        var reglage = new ReglageDesRelances(DossierDeLaBase);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reglage.ChangerAsync(0));
        Assert.False(File.Exists(DossierDeLaBase.FichierDesReglages));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dossier, recursive: true);
        }
        catch (IOException)
        {
            // Le dossier temporaire reste : le système le videra.
        }
    }
}
