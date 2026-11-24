using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.IA;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.IA;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDeLIA;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Le réglage de l'IA (#43, ADR-0016) : aucune IA par défaut ; le fournisseur dans reglages.json, lu et écrit à part ; la
/// clé écrite une fois, jamais rendue, chiffrée hors de la base, et effacée quand le fournisseur change ; une IA mal
/// réglée ou une clé illisible qui ne cassent rien. Clés fictives, dossiers temporaires.
/// </summary>
public sealed class ReglageDeLIATests : IDisposable
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private DossierDeLaBase Base => new(_dossier);

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

    [Fact]
    public async Task Sans_reglage_aucune_IA_n_est_choisie_et_le_lancement_n_ecrit_ni_trousseau_ni_cle()
    {
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();

            Assert.Equal(Etat("Aucun", choisi: false, "SansObjet"), (await api.EtatDeLIAAsync()).ToJsonString());
            var aucune = Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur());
            Assert.Equal(RaisonDIndisponibilite.AucuneIA, aucune.Raison);
        }

        Assert.False(Directory.Exists(Base.Trousseau));
        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.False(File.Exists(Base.FichierDesReglages));
    }

    [Fact]
    public async Task Choisir_Claude_puis_enregistrer_sa_cle_qui_ne_revient_jamais()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        Assert.Equal(Etat("Claude", choisi: true, "Aucune"), (await api.ChoisirLIAAvecSuccesAsync("Claude")).ToJsonString());
        using var reponse = await api.EnregistrerLaCleAsync("  " + CleDeClaude + "\n");
        var corps = await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), corps);
        Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), (await api.EtatDeLIAAsync()).ToJsonString());
        // La clé et le trousseau naissent sous le dossier du test, et nulle part la clé n'est en clair.
        Assert.True(File.Exists(Base.FichierDeLaCle));
        Assert.NotEmpty(Directory.GetFiles(Base.Trousseau, "key-*.xml"));
        Assert.Equal("Claude", JsonNode.Parse(await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken))!["fournisseurDIA"]!.GetValue<string>());
        AucunFichierNeContientDeCle(_dossier);
        // L'adaptateur dit qu'il ne sert pas encore : Claude arrive avec #48.
        Assert.Equal(RaisonDIndisponibilite.PasEncoreBranche, Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur()).Raison);
    }

    [Fact]
    public async Task Remplacer_la_cle_garde_la_seule_nouvelle()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

        Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), (await api.EnregistrerLaCleAvecSuccesAsync(AutreCleDeClaude)).ToJsonString());

        var garde = api.Services.GetRequiredService<Sollicito.Infrastructure.IA.GardeDeLaCle>();
        Assert.Equal(AutreCleDeClaude, garde.Lire(FournisseurDIA.Claude).Cle!.Valeur());
    }

    [Fact]
    public async Task Changer_de_fournisseur_efface_d_abord_la_cle_et_choisir_le_meme_la_garde()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

        // Le même fournisseur : rien ne change, la clé reste.
        Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), (await api.ChoisirLIAAvecSuccesAsync("Claude")).ToJsonString());
        Assert.True(File.Exists(Base.FichierDeLaCle));

        // Un autre : la clé de Claude est effacée, le trousseau reste.
        Assert.Equal(Etat("OpenAI", choisi: true, "Aucune"), (await api.ChoisirLIAAvecSuccesAsync("OpenAI")).ToJsonString());
        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.True(Directory.Exists(Base.Trousseau));

        // Une clé d'OpenAI, puis un modèle local, qui n'en prend pas : elle s'efface aussi.
        await api.EnregistrerLaCleAvecSuccesAsync(CleDOpenAI);
        Assert.Equal(Etat("Ollama", choisi: true, "SansObjet"), (await api.ChoisirLIAAvecSuccesAsync("Ollama")).ToJsonString());
        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.Equal(RaisonDIndisponibilite.PasEncoreBranche, Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur()).Raison);
    }

    [Fact]
    public async Task Aucune_IA_choisie_s_ecrit_et_le_choix_reste_fait()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        Assert.Equal(Etat("Aucun", choisi: true, "SansObjet"), (await api.ChoisirLIAAvecSuccesAsync("Aucun")).ToJsonString());

        Assert.Equal("Aucun", JsonNode.Parse(await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken))!["fournisseurDIA"]!.GetValue<string>());
        Assert.Equal(Etat("Aucun", choisi: true, "SansObjet"), (await api.EtatDeLIAAsync()).ToJsonString());
    }

    [Fact]
    public async Task Un_fichier_de_main_se_relit_sans_perte_et_garde_ses_reglages_quand_l_IA_s_y_ajoute()
    {
        var copies = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            // Le fichier tel que main l'écrit : un dossier choisi, un délai changé, et un réglage que la branche ne connaît pas.
            var deMain = new JsonObject
            {
                ["dossierDesSauvegardes"] = copies,
                ["delaiDesRelances"] = 14,
                ["reglageAVenir"] = new JsonObject { ["a"] = new JsonArray(1, 2) },
            }.ToJsonString();
            await File.WriteAllTextAsync(Base.FichierDesReglages, deMain, TestContext.Current.CancellationToken);
            await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

            Assert.Equal(Etat("Aucun", choisi: false, "SansObjet"), (await api.EtatDeLIAAsync()).ToJsonString());
            await api.ChoisirLIAAvecSuccesAsync("Claude");

            var fichier = JsonNode.Parse(await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken))!.AsObject();
            Assert.Equal(copies, fichier["dossierDesSauvegardes"]!.GetValue<string>());
            Assert.Equal(14, fichier["delaiDesRelances"]!.GetValue<int>());
            Assert.Equal(2, fichier["reglageAVenir"]!["a"]!.AsArray().Count);
            Assert.Equal("Claude", fichier["fournisseurDIA"]!.GetValue<string>());
            Assert.Equal(14, (await api.ReglageDesRelancesAsync())["delaiParDefaut"]!.GetValue<int>());
            Assert.Equal(copies, (await api.LireLesSauvegardesAsync())["dossier"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(copies, recursive: true);
        }
    }

    [Fact]
    public async Task Un_choix_illisible_vaut_aucune_IA_sans_toucher_au_delai_et_un_nouveau_choix_le_reecrit()
    {
        await File.WriteAllTextAsync(Base.FichierDesReglages, """{ "fournisseurDIA": "Mistral", "delaiDesRelances": 9 }""", TestContext.Current.CancellationToken);
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        Assert.Equal(Etat("Aucun", choisi: true, "SansObjet", reglageLisible: false), (await api.EtatDeLIAAsync()).ToJsonString());
        Assert.Equal(9, (await api.ReglageDesRelancesAsync())["delaiParDefaut"]!.GetValue<int>());
        Assert.Equal(RaisonDIndisponibilite.ReglageIllisible, Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur()).Raison);

        Assert.Equal(Etat("OpenAI", choisi: true, "Aucune"), (await api.ChoisirLIAAvecSuccesAsync("OpenAI")).ToJsonString());
        Assert.Equal(9, (await api.ReglageDesRelancesAsync())["delaiParDefaut"]!.GetValue<int>());
    }

    [Fact]
    public async Task Un_fichier_illisible_en_entier_refuse_le_choix_et_la_cle_sans_rien_ecrire()
    {
        const string Illisible = "{ pas du json";
        await File.WriteAllTextAsync(Base.FichierDesReglages, Illisible, TestContext.Current.CancellationToken);
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());

        Assert.Equal(Etat("Aucun", choisi: true, "SansObjet", reglageLisible: false, fichierLisible: false), (await api.EtatDeLIAAsync()).ToJsonString());
        await AttendreUnConflitAsync(api.ChoisirLIAAsync("Claude"), RoutesDeLIA.TitreDuChoixSurDesReglagesIllisibles);
        await AttendreUnConflitAsync(api.EnregistrerLaCleAsync(CleDeClaude), RoutesDeLIA.TitreDeLaCleSurDesReglagesIllisibles);
        // Retirer une clé reste permis : cela n'écrit pas les réglages.
        using (var retrait = await api.RetirerLaCleAsync())
        {
            Assert.Equal(HttpStatusCode.OK, retrait.StatusCode);
        }

        Assert.Equal(Illisible, await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.False(Directory.Exists(Base.Trousseau));
    }

    [Theory]
    [InlineData("Claude", CleDOpenAI, "Une clé de Claude commence par sk-ant-.")]
    [InlineData("OpenAI", CleDeClaude, "Cette clé est une clé de Claude : choisissez Claude, ou collez une clé d'OpenAI.")]
    [InlineData("OpenAI", "pk-SOLLICITO-ESSAI-43-cle-fictive", "Une clé d'OpenAI commence par sk-.")]
    [InlineData("Claude", "sk-ant-SOLLICITO-ESSAI-43 avec espace", "Une clé d'API ne contient ni espace ni caractère accentué.")]
    [InlineData("Claude", "sk-ant-SOLLICITO-ESSAI-43-clé", "Une clé d'API ne contient ni espace ni caractère accentué.")]
    [InlineData("Claude", "sk-ant-ESSAI-43", "Une clé d'API fait de 20 à 512 caractères.")]
    [InlineData("Claude", "", "Collez la clé d'API.")]
    [InlineData("Claude", null, "Collez la clé d'API.")]
    public async Task Une_cle_sans_sa_forme_est_refusee_sans_jamais_reprendre_la_saisie(string fournisseur, string? cle, string message)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync(fournisseur);

        using var reponse = await api.EnregistrerLaCleAsync(cle);
        var corps = await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var probleme = JsonNode.Parse(corps)!;
        Assert.Equal(RoutesDeLIA.TitreDeLaCleRefusee, probleme["title"]!.GetValue<string>());
        Assert.Equal(message, Assert.Single(probleme["errors"]!["cle"]!.AsArray())!.GetValue<string>());
        Assert.DoesNotContain(PartieReconnaissable, corps, StringComparison.Ordinal);
        Assert.False(File.Exists(Base.FichierDeLaCle));
    }

    [Fact]
    public async Task Une_cle_trop_longue_est_refusee_par_sa_forme_puis_par_la_borne_du_corps()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");

        using (var longue = await api.EnregistrerLaCleAsync("sk-ant-" + new string('a', 600)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longue.StatusCode);
        }

        using (var tropGrosse = await api.EnregistrerLaCleAsync("sk-ant-" + new string('a', RoutesDeLIA.BorneDuCorps)))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tropGrosse.StatusCode);
        }

        using (var choixTropGros = await api.EcrireAsync(HttpMethod.Put, Adresse, $$"""{"fournisseur":"Claude","x":"{{new string('a', RoutesDeLIA.BorneDuCorps)}}"}""")
        )
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, choixTropGros.StatusCode);
        }

        Assert.False(File.Exists(Base.FichierDeLaCle));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Aucun")]
    [InlineData("Ollama")]
    public async Task Une_cle_pour_un_fournisseur_qui_n_en_prend_pas_est_refusee(string? fournisseur)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        if (fournisseur is not null)
        {
            await api.ChoisirLIAAvecSuccesAsync(fournisseur);
        }

        await AttendreUnConflitAsync(api.EnregistrerLaCleAsync(CleDeClaude), RoutesDeLIA.TitreDeLaCleSansFournisseur);

        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.False(Directory.Exists(Base.Trousseau));
    }

    [Theory]
    [InlineData("/api/reglages/ia", """{"fournisseur":"Claude","autre":1}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":"Claude","fournisseur":"OpenAI"}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":"Claude","Fournisseur":"OpenAI"}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":1}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":["Claude"]}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":"claude"}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":"Mistral"}""")]
    [InlineData("/api/reglages/ia", """{"fournisseur":"Claude""")]
    [InlineData("/api/reglages/ia", "null")]
    [InlineData("/api/reglages/ia/cle", """{"cle":"sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0001","autre":1}""")]
    [InlineData("/api/reglages/ia/cle", """{"cle":"sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0001","cle":"sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0002"}""")]
    [InlineData("/api/reglages/ia/cle", """{"cle":"sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0001","Cle":"x"}""")]
    [InlineData("/api/reglages/ia/cle", """{"cle":12345678901234567890}""")]
    [InlineData("/api/reglages/ia/cle", """{"cle":"sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0001""")]
    [InlineData("/api/reglages/ia/cle", "null")]
    public async Task Un_corps_illisible_ne_change_rien(string chemin, string corps)
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Ollama");
        var avant = await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken);

        using var reponse = await api.EcrireAsync(HttpMethod.Put, chemin, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.DoesNotContain(PartieReconnaissable, await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(avant, await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Base.FichierDeLaCle));
    }

    [Fact]
    public async Task Retirer_la_cle_l_efface_et_se_repete_sans_erreur()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

        for (var fois = 0; fois < 2; fois++)
        {
            using var reponse = await api.RetirerLaCleAsync();
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
            Assert.Equal(Etat("Claude", choisi: true, "Aucune"), await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.Equal(RaisonDIndisponibilite.CleAbsente, Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur()).Raison);
    }

    [Fact]
    public async Task Une_cle_tenue_par_un_autre_programme_fait_refuser_le_changement_et_le_retrait()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

        using (new FileStream(Base.FichierDeLaCle, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await AttendreUnConflitAsync(api.ChoisirLIAAsync("OpenAI"), RoutesDeLIA.TitreDeLaCleNonEffacee);
            await AttendreUnConflitAsync(api.RetirerLaCleAsync(), RoutesDeLIA.TitreDeLaCleNonRetiree);
        }

        // Rien n'a changé : le fournisseur et sa clé.
        Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), (await api.EtatDeLIAAsync()).ToJsonString());
        Assert.Equal("Claude", JsonNode.Parse(await File.ReadAllTextAsync(Base.FichierDesReglages, TestContext.Current.CancellationToken))!["fournisseurDIA"]!.GetValue<string>());
    }

    [Fact]
    public async Task Un_trousseau_ou_l_on_ne_peut_pas_ecrire_fait_refuser_la_cle_sans_rien_casser()
    {
        // Un fichier à la place du dossier du trousseau : Data Protection ne peut pas y écrire sa première clé.
        await File.WriteAllTextAsync(Base.Trousseau, "pas un dossier", TestContext.Current.CancellationToken);
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");

        await AttendreUnConflitAsync(api.EnregistrerLaCleAsync(CleDeClaude), RoutesDeLIA.TitreDeLaCleNonChiffree);

        Assert.False(File.Exists(Base.FichierDeLaCle));
        Assert.Equal(Etat("Claude", choisi: true, "Aucune"), (await api.EtatDeLIAAsync()).ToJsonString());
        AucunFichierNeContientDeCle(_dossier);
    }

    [Fact]
    public async Task Un_trousseau_efface_l_application_demarre_marche_sans_IA_et_le_dit_puis_une_cle_se_ressaisit()
    {
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.ChoisirLIAAvecSuccesAsync("Claude");
            await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);
        }

        Directory.Delete(Base.Trousseau, recursive: true);

        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            Assert.Equal(Etat("Claude", choisi: true, "Illisible"), (await api.EtatDeLIAAsync()).ToJsonString());
            Assert.Equal(RaisonDIndisponibilite.CleIllisible, Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur()).Raison);
            // Tout le reste marche.
            await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T09:00");
            Assert.Single(await api.ListerAsync());

            Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), (await api.EnregistrerLaCleAvecSuccesAsync(AutreCleDeClaude)).ToJsonString());
        }
    }

    [Fact]
    public async Task Un_reglage_change_hors_de_l_API_laisse_la_cle_illisible_et_jamais_servie()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

        // Un programme, ou l'exe de main, réécrit le fournisseur sans passer par l'API : la clé de Claude reste sur le disque.
        await File.WriteAllTextAsync(Base.FichierDesReglages, """{ "fournisseurDIA": "OpenAI" }""", TestContext.Current.CancellationToken);

        Assert.Equal(Etat("OpenAI", choisi: true, "Illisible"), (await api.EtatDeLIAAsync()).ToJsonString());
        Assert.Equal(RaisonDIndisponibilite.CleIllisible, Assert.IsType<AucuneIA>(api.Services.GetRequiredService<ReglageDeLIA>().Fournisseur()).Raison);
        // Une nouvelle clé d'OpenAI remplace celle de Claude.
        Assert.Equal(Etat("OpenAI", choisi: true, "Enregistree"), (await api.EnregistrerLaCleAvecSuccesAsync(CleDOpenAI)).ToJsonString());
    }

    [Fact]
    public async Task Une_restauration_ne_change_ni_le_choix_ni_la_cle()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.SauvegarderMaintenantAsync();
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);

        Assert.Equal(IssueDeLaRestauration.Restauree, await api.RestaurerAsync(api.Copie(api.CopieDuJour(new DateOnly(2026, 11, 18)))));

        Assert.Equal(Etat("Claude", choisi: true, "Enregistree"), (await api.EtatDeLIAAsync()).ToJsonString());
        AucunFichierNeContientDeCle(_dossier);
    }

    [Fact]
    public async Task Porte_fermee_pendant_une_restauration_les_routes_de_l_IA_repondent()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        var porte = api.Services.GetRequiredService<PorteDeLaBase>();
        Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken));
        try
        {
            Assert.Equal(Etat("Aucun", choisi: false, "SansObjet"), (await api.EtatDeLIAAsync()).ToJsonString());
            await api.ChoisirLIAAvecSuccesAsync("Claude");
            await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude);
            using var retrait = await api.RetirerLaCleAsync();
            Assert.Equal(HttpStatusCode.OK, retrait.StatusCode);
        }
        finally
        {
            porte.Ouvrir();
        }
    }

    [Fact]
    public async Task La_cle_n_est_ni_dans_une_reponse_ni_dans_la_base_ni_dans_les_copies_ni_dans_aucun_fichier()
    {
        var reponses = new StringBuilder();
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
            await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T09:00");
            reponses.Append((await api.ChoisirLIAAvecSuccesAsync("Claude")).ToJsonString());
            reponses.Append((await api.EnregistrerLaCleAvecSuccesAsync(CleDeClaude)).ToJsonString());
            reponses.Append((await api.EtatDeLIAAsync()).ToJsonString());
            reponses.Append((await api.SauvegarderMaintenantAsync()).ToJsonString());
            reponses.Append((await api.ListerAsync()).ToJsonString());
            using (var documentation = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true))
            {
                reponses.Append(await documentation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }
        }

        Assert.DoesNotContain(PartieReconnaissable, reponses.ToString(), StringComparison.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(Base.DossierDesSauvegardes, "*.db"));
        // La base, ses journaux, les copies, les réglages, le trousseau et le fichier de la clé lui-même.
        AucunFichierNeContientDeCle(_dossier);
    }

    private static async Task AttendreUnConflitAsync(Task<HttpResponseMessage> envoi, string titre)
    {
        using var reponse = await envoi;
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal(titre, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
    }
}
