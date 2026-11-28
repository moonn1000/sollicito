using System.Text.Json;

namespace Sollicito.Fenetre.Tests;

/// <summary>
/// Les règles que la fenêtre applique à ses événements : ce qui reste dans la fenêtre, ce qui s'ouvre dans le
/// navigateur par défaut, quel message reçoit le jeton (ADR-0001, D9 ; ADR-0003).
/// </summary>
public sealed class ReglesDeLaFenetreTests
{
    private static readonly Uri Origine = new("http://127.0.0.1:52344");

    [Theory]
    [InlineData("http://127.0.0.1:52344/")]
    [InlineData("http://127.0.0.1:52344/index.html")]
    [InlineData("http://127.0.0.1:52344/assets/app.js?v=1#haut")]
    [InlineData("HTTP://127.0.0.1:52344/")]
    public void Une_adresse_de_l_interface_reste_dans_la_fenetre(string adresse)
    {
        Assert.True(ReglesDeLaFenetre.EstDeLOrigine(adresse, Origine));
    }

    [Theory]
    [InlineData("http://127.0.0.1:52345/")]
    [InlineData("http://localhost:52344/")]
    [InlineData("https://127.0.0.1:52344/")]
    [InlineData("http://127.0.0.2:52344/")]
    [InlineData("http://[::1]:52344/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("https://exemple.invalid/")]
    [InlineData("http://127.0.0.1:52344@exemple.invalid/")]
    [InlineData("http://nom:secret@127.0.0.1:52344/")]
    [InlineData("about:blank")]
    [InlineData("data:text/html,<p>page</p>")]
    [InlineData("blob:http://127.0.0.1:52344/6d1c4e2a-0000-4000-8000-000000000000")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/index.html")]
    [InlineData("")]
    [InlineData(null)]
    public void Toute_autre_adresse_est_hors_de_l_origine(string? adresse)
    {
        Assert.False(ReglesDeLaFenetre.EstDeLOrigine(adresse, Origine));
    }

    [Theory]
    [InlineData("https://exemple.invalid/offre?id=1", "https://exemple.invalid/offre?id=1")]
    [InlineData("http://exemple.invalid/", "http://exemple.invalid/")]
    [InlineData("https://exemple.invalid/une offre", "https://exemple.invalid/une%20offre")]
    public void Un_lien_http_ou_https_clique_s_ouvre_dans_le_navigateur(string adresse, string ouverte)
    {
        Assert.Equal(ouverte, ReglesDeLaFenetre.LienPourLeNavigateur(adresse, demandeParLUtilisateur: true)?.AbsoluteUri);
    }

    [Fact]
    public void Un_lien_ouvert_par_un_script_sans_geste_de_l_utilisateur_ne_s_ouvre_pas()
    {
        Assert.Null(ReglesDeLaFenetre.LienPourLeNavigateur("https://exemple.invalid/", demandeParLUtilisateur: false));
    }

    [Theory]
    [InlineData("mailto:contact@exemple.invalid")]
    [InlineData("ftp://exemple.invalid/fichier")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<p>page</p>")]
    [InlineData("https://banque.exemple@exemple.invalid/")]
    [InlineData("/relative")]
    [InlineData("")]
    [InlineData(null)]
    public void Tout_autre_lien_est_refuse(string? adresse)
    {
        Assert.Null(ReglesDeLaFenetre.LienPourLeNavigateur(adresse, demandeParLUtilisateur: true));
    }

    [Theory]
    [InlineData("http://127.0.0.1:52344/", """{"type":"prete"}""")]
    [InlineData("http://127.0.0.1:52344/index.html", """{ "type" : "prete" }""")]
    public void L_interface_prete_recoit_le_jeton(string source, string message)
    {
        Assert.True(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
    }

    [Theory]
    // Le message ne porte que son type (#88) : un champ de plus, répété compris, et rien n'est remis.
    [InlineData("http://127.0.0.1:52344/index.html", """{"type":"prete","autre":1}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"prete","type":"prete"}""")]
    [InlineData("http://localhost:52344/", """{"type":"prete"}""")]
    [InlineData("https://exemple.invalid/", """{"type":"prete"}""")]
    [InlineData("about:blank", """{"type":"prete"}""")]
    [InlineData(null, """{"type":"prete"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"autre"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":1}""")]
    [InlineData("http://127.0.0.1:52344/", """{"Type":"prete"}""")]
    [InlineData("http://127.0.0.1:52344/", """["prete"]""")]
    [InlineData("http://127.0.0.1:52344/", "\"prete\"")]
    [InlineData("http://127.0.0.1:52344/", "{pas du json")]
    [InlineData("http://127.0.0.1:52344/", "")]
    [InlineData("http://127.0.0.1:52344/", null)]
    public void Aucun_autre_message_ne_recoit_le_jeton(string? source, string? message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
    }

    [Fact]
    public void Le_message_du_jeton_porte_son_type_et_sa_valeur()
    {
        using var message = JsonDocument.Parse(ReglesDeLaFenetre.MessageDuJeton("valeur-fictive"));

        Assert.Equal("jeton", message.RootElement.GetProperty("type").GetString());
        Assert.Equal("valeur-fictive", message.RootElement.GetProperty("jeton").GetString());
    }

    [Theory]
    [InlineData("http://127.0.0.1:52344/", """{"type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData("http://127.0.0.1:52344/index.html", """{ "type": "choisirLeDossierDesSauvegardes" }""")]
    public void L_interface_demande_le_choix_du_dossier_des_sauvegardes(string source, string message)
    {
        Assert.True(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, message, Origine));
        // Ce message ne reçoit pas le jeton, ni le précédent n'ouvre de dialogue.
        Assert.False(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
        Assert.False(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, """{"type":"prete"}""", Origine));
    }

    [Theory]
    // Le message ne porte que son type, jamais un chemin (#88) : un champ de plus, répété compris, et rien ne s'ouvre.
    [InlineData("http://127.0.0.1:52344/index.html", """{"type":"choisirLeDossierDesSauvegardes","dossier":"\\\\serveur\\partage"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"choisirLeDossierDesSauvegardes","type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData("http://localhost:52344/", """{"type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData("https://exemple.invalid/", """{"type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData("about:blank", """{"type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData(null, """{"type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"choisirLeDossier"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"Type":"choisirLeDossierDesSauvegardes"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":["choisirLeDossierDesSauvegardes"]}""")]
    [InlineData("http://127.0.0.1:52344/", "\"choisirLeDossierDesSauvegardes\"")]
    [InlineData("http://127.0.0.1:52344/", "{pas du json")]
    [InlineData("http://127.0.0.1:52344/", "")]
    [InlineData("http://127.0.0.1:52344/", null)]
    public void Aucun_autre_message_n_ouvre_le_dialogue_de_dossier(string? source, string? message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, message, Origine));
    }

    [Theory]
    [InlineData(nameof(IssueDuChoixDuDossier.Choisi), "choisi")]
    [InlineData(nameof(IssueDuChoixDuDossier.Annule), "annule")]
    [InlineData(nameof(IssueDuChoixDuDossier.Refuse), "refuse")]
    [InlineData(nameof(IssueDuChoixDuDossier.Occupe), "occupe")]
    public void La_reponse_au_choix_du_dossier_porte_son_issue_sans_chemin(string issue, string attendue)
    {
        using var message = JsonDocument.Parse(ReglesDeLaFenetre.MessageDuChoixDuDossier(Enum.Parse<IssueDuChoixDuDossier>(issue)));

        Assert.Equal(["type", "issue"], message.RootElement.EnumerateObject().Select(propriete => propriete.Name));
        Assert.Equal("dossierDesSauvegardes", message.RootElement.GetProperty("type").GetString());
        Assert.Equal(attendue, message.RootElement.GetProperty("issue").GetString());
    }
}
