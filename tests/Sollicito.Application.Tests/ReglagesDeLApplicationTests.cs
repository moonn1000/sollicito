using System.Text.Json.Nodes;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le fichier des réglages (#26, ADR-0008) : absent, il rend les réglages par défaut ; illisible, aussi, en le disant ;
/// réécrit, il garde ce qu'il portait d'autre.
/// </summary>
public sealed class ReglagesDeLApplicationTests
{
    [Fact]
    public void Sans_fichier_les_reglages_sont_ceux_par_defaut_et_lisibles()
    {
        Assert.Equal(new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: true), ReglagesDeLApplication.Lire(null));
        Assert.Null(ReglagesDeLApplication.ParDefaut.DossierDesSauvegardes);
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
    public void Un_fichier_sans_dossier_rend_le_dossier_par_defaut(string texte)
    {
        Assert.Equal(new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: true), ReglagesDeLApplication.Lire(texte));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ pas du json")]
    [InlineData("[]")]
    [InlineData("\"E:\\\\Sauvegardes\"")]
    [InlineData("""{ "dossierDesSauvegardes": 3 }""")]
    [InlineData("""{ "dossierDesSauvegardes": "Sauvegardes" }""")]
    [InlineData("""{ "dossierDesSauvegardes": "..\\Sauvegardes" }""")]
    [InlineData("""{ "dossierDesSauvegardes": "E:\\Sauve\u0000gardes" }""")]
    [InlineData("""{ "dossierDesSauvegardes": "E:\\A", "dossierDesSauvegardes": "E:\\B" }""")]
    public void Un_fichier_illisible_rend_le_dossier_par_defaut_en_le_disant(string texte)
    {
        Assert.Equal(new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: false), ReglagesDeLApplication.Lire(texte));
    }

    [Fact]
    public void Ecrire_garde_les_autres_reglages_du_fichier()
    {
        var texte = new ReglagesDeLApplication(@"E:\Sauvegardes").Ecrire("""{ "delaiDesRelances": 7, "dossierDesSauvegardes": "D:\\Ancien" }""");

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.Equal(7, relu["delaiDesRelances"]!.GetValue<int>());
        Assert.Equal(@"E:\Sauvegardes", relu["dossierDesSauvegardes"]!.GetValue<string>());
        Assert.Equal(@"E:\Sauvegardes", ReglagesDeLApplication.Lire(texte).Reglages.DossierDesSauvegardes);
    }

    [Fact]
    public void Revenir_au_dossier_par_defaut_retire_le_dossier_du_fichier()
    {
        var texte = ReglagesDeLApplication.ParDefaut.Ecrire("""{ "delaiDesRelances": 7, "dossierDesSauvegardes": "D:\\Ancien" }""");

        var relu = JsonNode.Parse(texte)!.AsObject();
        Assert.False(relu.ContainsKey("dossierDesSauvegardes"));
        Assert.Equal(7, relu["delaiDesRelances"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ pas du json")]
    [InlineData("[1, 2]")]
    public void Un_fichier_absent_ou_illisible_se_reecrit_d_un_bloc(string? avant)
    {
        var texte = new ReglagesDeLApplication(@"E:\Sauvegardes").Ecrire(avant);

        Assert.Equal(new LectureDesReglages(new ReglagesDeLApplication(@"E:\Sauvegardes"), Lisible: true), ReglagesDeLApplication.Lire(texte));
    }
}
