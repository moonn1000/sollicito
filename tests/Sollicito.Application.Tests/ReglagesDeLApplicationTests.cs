using System.Text.Json.Nodes;
using Sollicito.Application.IA;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le fichier des réglages (#26, #14, ADR-0008, ADR-0011) : absent, il rend les réglages par défaut ; illisible, aussi,
/// en le disant ; chaque réglage se lit à part, et une écriture ne change que le sien, en gardant ce que le fichier
/// portait d'autre.
/// </summary>
public sealed class ReglagesDeLApplicationTests
{
    [Fact]
    public void Sans_fichier_les_reglages_sont_ceux_par_defaut_et_lisibles()
    {
        Assert.Equal(new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: true), ReglagesDeLApplication.Lire(null));
        Assert.Null(ReglagesDeLApplication.ParDefaut.DossierDesSauvegardes);
        Assert.Null(ReglagesDeLApplication.ParDefaut.DelaiDesRelances);
        Assert.Equal(7, ReglagesDeLApplication.ParDefaut.DelaiDesRelancesEnVigueur);
    }

    [Fact]
    public void Un_dossier_choisi_se_relit()
    {
        var lecture = ReglagesDeLApplication.Lire("""{ "dossierDesSauvegardes": "E:\\Sauvegardes Sollicito" }""");

        Assert.True(lecture.Lisible);
        Assert.Equal(@"E:\Sauvegardes Sollicito", lecture.Reglages.DossierDesSauvegardes);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "dossierDesSauvegardes": null }""")]
    [InlineData("""{ "delaiDesRelances": 7 }""")]
    [InlineData("""{ "fournisseurDIA": "Ollama" }""")]
    public void Un_fichier_sans_dossier_rend_le_dossier_par_defaut(string texte)
    {
        var lecture = ReglagesDeLApplication.Lire(texte);

        Assert.True(lecture.Lisible);
        Assert.True(lecture.FichierLisible);
        Assert.Null(lecture.Reglages.DossierDesSauvegardes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ pas du json")]
    [InlineData("[]")]
    [InlineData("\"E:\\\\Sauvegardes\"")]
    [InlineData("""{ "dossierDesSauvegardes": "E:\\A", "dossierDesSauvegardes": "E:\\B" }""")]
    [InlineData("""{ "delaiDesRelances": 7, "delaiDesRelances": 8 }""")]
    public void Un_fichier_illisible_en_entier_rend_les_reglages_par_defaut_en_le_disant(string texte)
    {
        Assert.Equal(
            new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: false, DelaiLisible: false, FichierLisible: false, FournisseurLisible: false),
            ReglagesDeLApplication.Lire(texte));
    }

    [Theory]
    [InlineData("""{ "dossierDesSauvegardes": 3 }""")]
    [InlineData("""{ "dossierDesSauvegardes": "Sauvegardes" }""")]
    [InlineData("""{ "dossierDesSauvegardes": "..\\Sauvegardes" }""")]
    [InlineData("""{ "dossierDesSauvegardes": "E:\\Sauve\u0000gardes" }""")]
    public void Un_dossier_illisible_rend_le_dossier_par_defaut_en_le_disant(string texte)
    {
        Assert.Equal(new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: false), ReglagesDeLApplication.Lire(texte));
    }

    [Fact]
    public void Un_delai_choisi_se_relit()
    {
        var lecture = ReglagesDeLApplication.Lire("""{ "delaiDesRelances": 10 }""");

        Assert.True(lecture.DelaiLisible);
        Assert.Equal(10, lecture.Reglages.DelaiDesRelances);
        Assert.Equal(10, lecture.Reglages.DelaiDesRelancesEnVigueur);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("365", 365)]
    public void Les_bornes_du_delai_se_relisent(string jours, int attendu)
    {
        Assert.Equal(attendu, ReglagesDeLApplication.Lire($$"""{ "delaiDesRelances": {{jours}} }""").Reglages.DelaiDesRelances);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("366")]
    [InlineData("-7")]
    [InlineData("7.5")]
    [InlineData("\"7\"")]
    [InlineData("true")]
    [InlineData("[7]")]
    [InlineData("99999999999")]
    public void Un_delai_illisible_rend_le_delai_d_usine_sans_toucher_au_dossier(string jours)
    {
        var lecture = ReglagesDeLApplication.Lire($$"""{ "dossierDesSauvegardes": "E:\\Sauvegardes", "delaiDesRelances": {{jours}} }""");

        Assert.False(lecture.DelaiLisible);
        Assert.Null(lecture.Reglages.DelaiDesRelances);
        Assert.Equal(7, lecture.Reglages.DelaiDesRelancesEnVigueur);
        Assert.True(lecture.Lisible);
        Assert.True(lecture.FichierLisible);
        Assert.Equal(@"E:\Sauvegardes", lecture.Reglages.DossierDesSauvegardes);
    }

    [Fact]
    public void Un_dossier_illisible_ne_touche_pas_au_delai()
    {
        var lecture = ReglagesDeLApplication.Lire("""{ "dossierDesSauvegardes": "Sauvegardes", "delaiDesRelances": 12 }""");

        Assert.False(lecture.Lisible);
        Assert.True(lecture.DelaiLisible);
        Assert.Equal(12, lecture.Reglages.DelaiDesRelances);
    }

    [Fact]
    public void Ecrire_le_dossier_garde_les_autres_reglages_du_fichier()
    {
        var texte = ReglagesDeLApplication.AvecLeDossier("""{ "delaiDesRelances": 7, "dossierDesSauvegardes": "D:\\Ancien" }""", @"E:\Sauvegardes");

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.Equal(7, relu["delaiDesRelances"]!.GetValue<int>());
        Assert.Equal(@"E:\Sauvegardes", relu["dossierDesSauvegardes"]!.GetValue<string>());
        Assert.Equal(@"E:\Sauvegardes", ReglagesDeLApplication.Lire(texte).Reglages.DossierDesSauvegardes);
    }

    [Fact]
    public void Revenir_au_dossier_par_defaut_retire_le_dossier_du_fichier()
    {
        var texte = ReglagesDeLApplication.AvecLeDossier("""{ "delaiDesRelances": 7, "dossierDesSauvegardes": "D:\\Ancien" }""", dossier: null);

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.False(relu.ContainsKey("dossierDesSauvegardes"));
        Assert.Equal(7, relu["delaiDesRelances"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ pas du json")]
    [InlineData("[1, 2]")]
    public void Un_fichier_absent_ou_illisible_se_reecrit_d_un_bloc_au_choix_d_un_dossier(string? avant)
    {
        var texte = ReglagesDeLApplication.AvecLeDossier(avant, @"E:\Sauvegardes");

        Assert.Equal(new LectureDesReglages(new ReglagesDeLApplication(@"E:\Sauvegardes"), Lisible: true), ReglagesDeLApplication.Lire(texte));
    }

    [Fact]
    public void Ecrire_le_delai_garde_le_dossier_et_les_reglages_inconnus()
    {
        var texte = ReglagesDeLApplication.AvecLeDelaiDesRelances(
            """{ "dossierDesSauvegardes": "D:\\Copies", "fournisseurDIA": "Ollama", "delaiDesRelances": 7 }""",
            jours: 14);

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.Equal(14, relu["delaiDesRelances"]!.GetValue<int>());
        Assert.Equal(@"D:\Copies", relu["dossierDesSauvegardes"]!.GetValue<string>());
        Assert.Equal("Ollama", relu["fournisseurDIA"]!.GetValue<string>());
    }

    [Fact]
    public void Un_dossier_illisible_reste_illisible_quand_le_delai_s_ecrit()
    {
        var texte = ReglagesDeLApplication.AvecLeDelaiDesRelances("""{ "dossierDesSauvegardes": "Sauvegardes" }""", jours: 10);

        var lecture = ReglagesDeLApplication.Lire(texte);
        Assert.False(lecture.Lisible);
        Assert.Equal(10, lecture.Reglages.DelaiDesRelances);
    }

    [Fact]
    public void Revenir_au_delai_d_usine_retire_le_delai_du_fichier()
    {
        var texte = ReglagesDeLApplication.AvecLeDelaiDesRelances("""{ "dossierDesSauvegardes": "D:\\Copies", "delaiDesRelances": 9 }""", jours: null);

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.False(relu.ContainsKey("delaiDesRelances"));
        Assert.Equal(@"D:\Copies", relu["dossierDesSauvegardes"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Un_delai_hors_bornes_ne_s_ecrit_pas(int jours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReglagesDeLApplication.AvecLeDelaiDesRelances(null, jours));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "fournisseurDIA": null }""")]
    [InlineData("""{ "dossierDesSauvegardes": "E:\\Copies", "delaiDesRelances": 10 }""")]
    public void Sans_fournisseur_ecrit_aucun_choix_n_est_fait_et_aucune_IA_ne_s_applique(string texte)
    {
        var lecture = ReglagesDeLApplication.Lire(texte);

        Assert.True(lecture.FournisseurLisible);
        Assert.Null(lecture.Reglages.Fournisseur);
        Assert.Equal(FournisseurDIA.Aucun, lecture.Reglages.FournisseurEnVigueur);
        Assert.Equal(FournisseurDIA.Aucun, ReglagesDeLApplication.ParDefaut.FournisseurEnVigueur);
    }

    [Theory]
    [InlineData("Aucun", FournisseurDIA.Aucun)]
    [InlineData("Claude", FournisseurDIA.Claude)]
    [InlineData("OpenAI", FournisseurDIA.OpenAI)]
    [InlineData("Ollama", FournisseurDIA.Ollama)]
    public void Un_fournisseur_choisi_se_relit_par_son_nom_exact(string nom, FournisseurDIA attendu)
    {
        var lecture = ReglagesDeLApplication.Lire($$"""{ "fournisseurDIA": "{{nom}}" }""");

        Assert.True(lecture.FournisseurLisible);
        Assert.Equal(attendu, lecture.Reglages.Fournisseur);
        Assert.Equal(attendu, lecture.Reglages.FournisseurEnVigueur);
    }

    [Theory]
    [InlineData("\"claude\"")]
    [InlineData("\"CLAUDE\"")]
    [InlineData("\" Claude\"")]
    [InlineData("\"Mistral\"")]
    [InlineData("\"\"")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[\"Claude\"]")]
    [InlineData("{ \"nom\": \"Claude\" }")]
    public void Un_fournisseur_illisible_vaut_aucune_IA_sans_toucher_aux_autres_reglages(string valeur)
    {
        var lecture = ReglagesDeLApplication.Lire($$"""{ "dossierDesSauvegardes": "E:\\Copies", "delaiDesRelances": 12, "fournisseurDIA": {{valeur}} }""");

        Assert.False(lecture.FournisseurLisible);
        Assert.Null(lecture.Reglages.Fournisseur);
        Assert.Equal(FournisseurDIA.Aucun, lecture.Reglages.FournisseurEnVigueur);
        Assert.True(lecture.Lisible);
        Assert.True(lecture.DelaiLisible);
        Assert.True(lecture.FichierLisible);
        Assert.Equal(@"E:\Copies", lecture.Reglages.DossierDesSauvegardes);
        Assert.Equal(12, lecture.Reglages.DelaiDesRelances);
    }

    [Fact]
    public void Un_dossier_et_un_delai_illisibles_ne_touchent_pas_au_fournisseur()
    {
        var lecture = ReglagesDeLApplication.Lire("""{ "dossierDesSauvegardes": 3, "delaiDesRelances": "7", "fournisseurDIA": "OpenAI" }""");

        Assert.False(lecture.Lisible);
        Assert.False(lecture.DelaiLisible);
        Assert.True(lecture.FournisseurLisible);
        Assert.Equal(FournisseurDIA.OpenAI, lecture.Reglages.Fournisseur);
    }

    [Fact]
    public void Ecrire_le_fournisseur_garde_un_fichier_de_main_et_ses_reglages_inconnus()
    {
        // Le fichier tel que main l'écrit : le dossier choisi, le délai changé, et un réglage qu'il ne connaît pas.
        const string DeMain = """{ "dossierDesSauvegardes": "D:\\Copies", "delaiDesRelances": 14, "reglageAVenir": { "a": [1, 2] } }""";

        var texte = ReglagesDeLApplication.AvecLeFournisseurDIA(DeMain, FournisseurDIA.Claude);

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.Equal("Claude", relu["fournisseurDIA"]!.GetValue<string>());
        Assert.Equal(@"D:\Copies", relu["dossierDesSauvegardes"]!.GetValue<string>());
        Assert.Equal(14, relu["delaiDesRelances"]!.GetValue<int>());
        Assert.Equal(2, relu["reglageAVenir"]!["a"]!.AsArray().Count);
        var lecture = ReglagesDeLApplication.Lire(texte);
        Assert.Equal(new ReglagesDeLApplication(@"D:\Copies", 14, FournisseurDIA.Claude), lecture.Reglages);
        Assert.True(lecture.Lisible && lecture.DelaiLisible && lecture.FournisseurLisible && lecture.FichierLisible);
    }

    [Fact]
    public void Aucune_IA_choisie_s_ecrit_pour_que_le_choix_reste_fait()
    {
        var texte = ReglagesDeLApplication.AvecLeFournisseurDIA("""{ "fournisseurDIA": "Claude" }""", FournisseurDIA.Aucun);

        Assert.Equal("Aucun", JsonNode.Parse(texte)!["fournisseurDIA"]!.GetValue<string>());
        Assert.Equal(FournisseurDIA.Aucun, ReglagesDeLApplication.Lire(texte).Reglages.Fournisseur);
    }

    [Fact]
    public void Les_autres_ecritures_gardent_le_fournisseur()
    {
        const string Avant = """{ "fournisseurDIA": "OpenAI" }""";

        Assert.Equal(FournisseurDIA.OpenAI, ReglagesDeLApplication.Lire(ReglagesDeLApplication.AvecLeDossier(Avant, @"E:\Copies")).Reglages.Fournisseur);
        Assert.Equal(FournisseurDIA.OpenAI, ReglagesDeLApplication.Lire(ReglagesDeLApplication.AvecLeDelaiDesRelances(Avant, 9)).Reglages.Fournisseur);
    }
}
