using System.Text.Json;
using Sollicito.Application.Relances;

namespace Sollicito.Fenetre.Tests;

/// <summary>
/// Le lien mailto du brouillon d'une relance (#14, ADR-0011 ; RFC 6068) : la fenêtre le construit seule, à partir des
/// champs lus en base ; tout y est encodé, et il ne porte que le destinataire, l'objet et le corps. Le message de la
/// page ne porte que l'identifiant de la relance. Toutes les adresses sont fictives.
/// </summary>
public sealed class LienDuBrouillonTests
{
    private static readonly Uri Origine = new("http://127.0.0.1:52344");

    [Fact]
    public void Le_lien_porte_le_destinataire_l_objet_et_le_corps_encodes()
    {
        var lien = LienDuBrouillon.Construire(new BrouillonDeRelance("jeanne.martin@exemple.invalid", "Candidature au poste de Comptable", "Bonjour,\n\nMerci."));

        Assert.Equal(
            "mailto:jeanne.martin@exemple.invalid?subject=Candidature%20au%20poste%20de%20Comptable&body=Bonjour%2C%0D%0A%0D%0AMerci.",
            lien);
    }

    [Fact]
    public void Sans_destinataire_le_lien_n_a_que_l_objet_et_le_corps()
    {
        Assert.Equal("mailto:?subject=Objet&body=Corps", LienDuBrouillon.Construire(new BrouillonDeRelance(null, "Objet", "Corps")));
    }

    [Theory]
    [InlineData("?", "%3F")]
    [InlineData("&", "%26")]
    [InlineData("#", "%23")]
    [InlineData("=", "%3D")]
    [InlineData("+", "%2B")]
    [InlineData("%", "%25")]
    [InlineData("%0A", "%250A")]
    [InlineData("é", "%C3%A9")]
    [InlineData("€", "%E2%82%AC")]
    [InlineData("😀", "%F0%9F%98%80")]
    [InlineData("&bcc=pirate@exemple.invalid", "%26bcc%3Dpirate%40exemple.invalid")]
    public void Un_signe_de_l_objet_ou_du_corps_ne_peut_ni_couper_le_lien_ni_ajouter_un_en_tete(string texte, string encode)
    {
        var lien = LienDuBrouillon.Construire(new BrouillonDeRelance(null, texte, texte))!;

        Assert.Equal($"mailto:?subject={encode}&body={encode}", lien);
        Assert.Single(lien, signe => signe == '?');
        Assert.Single(lien, signe => signe == '&');
        Assert.DoesNotContain('#', lien);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void Chaque_fin_de_ligne_devient_celle_d_un_courriel(string corps)
    {
        Assert.EndsWith("&body=a%0D%0Ab", LienDuBrouillon.Construire(new BrouillonDeRelance(null, "Objet", corps)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rh+candidatures@exemple.invalid", "rh%2Bcandidatures@exemple.invalid")]
    [InlineData("o'brien@exemple.invalid", "o%27brien@exemple.invalid")]
    [InlineData("a&b=c#d/e@exemple.invalid", "a%26b%3Dc%23d%2Fe@exemple.invalid")]
    public void L_adresse_s_encode_aussi_sauf_son_arobase(string adresse, string encodee)
    {
        Assert.StartsWith($"mailto:{encodee}?subject=", LienDuBrouillon.Construire(new BrouillonDeRelance(adresse, "Objet", "Corps")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jeanne@exemple.invalid,pirate@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid?cc=pirate@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid\r\nbcc:pirate@exemple.invalid")]
    [InlineData("pas une adresse")]
    [InlineData("")]
    public void Un_destinataire_qui_n_est_pas_une_adresse_de_contact_ne_fait_aucun_lien(string adresse)
    {
        Assert.Null(LienDuBrouillon.Construire(new BrouillonDeRelance(adresse, "Objet", "Corps")));
    }

    [Fact]
    public void Le_lien_est_toujours_un_lien_mailto()
    {
        Assert.StartsWith(LienDuBrouillon.Schema, LienDuBrouillon.Construire(new BrouillonDeRelance(null, "https://exemple.invalid", "javascript:x")), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new OuvreurDeBrouillonsParLeShell().Ouvrir("https://exemple.invalid/"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":7}""", 7)]
    [InlineData("http://127.0.0.1:52344/index.html", """{"relance":2147483647,"type":"ouvrirLeBrouillon"}""", 2147483647)]
    public void L_interface_demande_le_brouillon_d_une_relance_par_son_seul_identifiant(string source, string message, int attendue)
    {
        Assert.True(ReglesDeLaFenetre.DemandeLeBrouillon(source, message, Origine, out var relance));
        Assert.Equal(attendue, relance);
        Assert.False(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
        Assert.False(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, message, Origine));
    }

    [Theory]
    [InlineData("""{"type":"ouvrirLeBrouillon","relance":7,"destinataire":"pirate@exemple.invalid","corps":"autre"}""")]
    [InlineData("""{"type":"ouvrirLeBrouillon","relance":7,"autre":null}""")]
    [InlineData("""{"type":"ouvrirLeBrouillon","relance":7,"relance":8}""")]
    [InlineData("""{"type":"ouvrirLeBrouillon","type":"ouvrirLeBrouillon","relance":7}""")]
    public void Un_champ_de_plus_que_le_type_et_la_relance_n_ouvre_aucun_brouillon(string message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeBrouillon("http://127.0.0.1:52344/", message, Origine, out _));
    }

    [Theory]
    [InlineData("http://localhost:52344/", """{"type":"ouvrirLeBrouillon","relance":7}""")]
    [InlineData("https://exemple.invalid/", """{"type":"ouvrirLeBrouillon","relance":7}""")]
    [InlineData("about:blank", """{"type":"ouvrirLeBrouillon","relance":7}""")]
    [InlineData(null, """{"type":"ouvrirLeBrouillon","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":null}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":"7"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":7.5}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":0}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":-3}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":2147483648}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":[7]}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirUnBrouillon","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", """{"Type":"ouvrirLeBrouillon","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"prete","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", "{pas du json")]
    [InlineData("http://127.0.0.1:52344/", "")]
    [InlineData("http://127.0.0.1:52344/", null)]
    public void Aucun_autre_message_n_ouvre_de_brouillon(string? source, string? message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeBrouillon(source, message, Origine, out _));
    }

    [Theory]
    [InlineData(nameof(IssueDuBrouillon.Ouvert), "ouvert")]
    [InlineData(nameof(IssueDuBrouillon.Refuse), "refuse")]
    [InlineData(nameof(IssueDuBrouillon.Impossible), "impossible")]
    public void La_reponse_porte_la_relance_et_l_issue_sans_rien_du_brouillon(string issue, string attendue)
    {
        using var message = JsonDocument.Parse(ReglesDeLaFenetre.MessageDuBrouillon(12, Enum.Parse<IssueDuBrouillon>(issue)));

        Assert.Equal(["type", "relance", "issue"], message.RootElement.EnumerateObject().Select(propriete => propriete.Name));
        Assert.Equal("brouillonDeRelance", message.RootElement.GetProperty("type").GetString());
        Assert.Equal(12, message.RootElement.GetProperty("relance").GetInt32());
        Assert.Equal(attendue, message.RootElement.GetProperty("issue").GetString());
    }
}
