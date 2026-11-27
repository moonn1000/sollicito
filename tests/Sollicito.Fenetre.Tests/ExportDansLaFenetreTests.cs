using System.Text.Json;
using Sollicito.Application.Export;

namespace Sollicito.Fenetre.Tests;

/// <summary>
/// L'export vu de la fenêtre (#49, ADR-0020) : le message de la page ne porte que le format, dans une forme stricte, jamais
/// un chemin ni un nom ; la réponse ne dit que le format et l'issue ; le dialogue « Enregistrer sous » propose un nom, un
/// seul type de fichier, et demande avant de remplacer un fichier existant.
/// </summary>
public sealed class ExportDansLaFenetreTests
{
    private static readonly Uri Origine = new("http://127.0.0.1:52344");

    [Theory]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"json"}""")]
    [InlineData("http://127.0.0.1:52344/index.html", """{ "format": "json", "type": "exporterLesDonnees" }""")]
    public void L_interface_demande_l_export_par_son_seul_format(string source, string message)
    {
        Assert.True(ReglesDeLaFenetre.DemandeLExport(source, message, Origine, out var format));
        Assert.Equal(FormatDExport.Json, format);
        // Ce message n'ouvre aucun autre dialogue, ni ne reçoit le jeton.
        Assert.False(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, message, Origine));
        Assert.False(ReglesDeLaFenetre.DemandeLaRestauration(source, message, Origine, out _));
        Assert.False(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
    }

    [Theory]
    [InlineData("http://localhost:52344/", """{"type":"exporterLesDonnees","format":"json"}""")]
    [InlineData("https://exemple.invalid/", """{"type":"exporterLesDonnees","format":"json"}""")]
    [InlineData(null, """{"type":"exporterLesDonnees","format":"json"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"json","chemin":"C:\\Users\\Compte\\export.json"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"json","nom":"export.json"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"json","format":"json"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","type":"exporterLesDonnees"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"JSON"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"xml"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"C:\\export.json"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":0}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":null}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":["json"]}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporter","format":"json"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":"json\u0000"}""")]
    [InlineData("http://127.0.0.1:52344/", """["exporterLesDonnees","json"]""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"exporterLesDonnees","format":""")]
    [InlineData("http://127.0.0.1:52344/", "")]
    [InlineData("http://127.0.0.1:52344/", null)]
    public void Un_autre_message_n_ouvre_aucun_dialogue_d_export(string? source, string? message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLExport(source, message, Origine, out _));
    }

    [Fact]
    public void La_reponse_ne_porte_que_le_format_et_l_issue()
    {
        Assert.Equal("""{"type":"export","format":"json","issue":"enCours"}""", ReglesDeLaFenetre.MessageDeLExportEnCours(FormatDExport.Json));
        (IssueDeLExport Issue, string Texte)[] cas =
        [
            (IssueDeLExport.Exporte, "exporte"),
            (IssueDeLExport.Annule, "annule"),
            (IssueDeLExport.Occupe, "occupe"),
            (IssueDeLExport.Existe, "existe"),
            (IssueDeLExport.Extension, "extension"),
            (IssueDeLExport.DossierDeDonnees, "dossierDeDonnees"),
            (IssueDeLExport.AccesRefuse, "accesRefuse"),
            (IssueDeLExport.PlaceManquante, "placeManquante"),
            (IssueDeLExport.DossierIntrouvable, "dossierIntrouvable"),
            (IssueDeLExport.Impossible, "impossible"),
        ];
        Assert.Equal(Enum.GetValues<IssueDeLExport>().Order(), cas.Select(un => un.Issue).Order());
        foreach (var (issue, texte) in cas)
        {
            var message = ReglesDeLaFenetre.MessageDeLExport(FormatDExport.Json, issue);
            Assert.Equal($$"""{"type":"export","format":"json","issue":"{{texte}}"}""", message);
            using var lu = JsonDocument.Parse(message);
            Assert.Equal(3, lu.RootElement.EnumerateObject().Count());
        }
    }

    [Fact]
    public void Le_dialogue_propose_un_nom_un_seul_type_de_fichier_et_demande_avant_de_remplacer()
    {
        var nom = FormatDeLExport.NomPropose(FormatDExport.Json, new DateOnly(2026, 11, 27));
        Assert.Equal("sollicito-export-2026-11-27.json", nom);

        using var dialogue = ChoixDUnFichierParLeDialogue.Construire(FormatDExport.Json, nom);

        Assert.True(dialogue.OverwritePrompt);
        Assert.False(dialogue.CreatePrompt);
        Assert.True(dialogue.CheckPathExists);
        Assert.True(dialogue.AddExtension);
        Assert.True(dialogue.RestoreDirectory);
        Assert.Equal("json", dialogue.DefaultExt);
        Assert.Equal("Export JSON (*.json)|*.json", dialogue.Filter);
        Assert.Equal("Exporter vos données", dialogue.Title);
        Assert.Equal(nom, dialogue.FileName);
        // Le dialogue s'ouvre là où Windows l'a laissé : jamais sur le dossier de données.
        Assert.Equal("", dialogue.InitialDirectory);
    }
}
