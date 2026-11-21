using System.Text.Json.Nodes;
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
            new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: false, DelaiLisible: false, FichierLisible: false),
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
}
