using System.Text;
using System.Text.Json;
using Sollicito.Application.Relances;

namespace Sollicito.Fenetre.Tests;

/// <summary>
/// Le brouillon relu d'un premier jet (#47, ADR-0019) : la page le demande à la fenêtre par un message neuf, aussi strict
/// que celui de #14, qui ne porte que l'identifiant de la relance, jamais le texte ; la réponse a son propre type ; la
/// longueur que l'application calcule pour borner le texte relu est celle du lien que la fenêtre bâtit. Adresses fictives.
/// </summary>
public sealed class BrouillonReluTests
{
    private static readonly Uri Origine = new("http://127.0.0.1:52344");

    [Theory]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":7}""", 7)]
    [InlineData("http://127.0.0.1:52344/index.html", """{"relance":2147483647,"type":"ouvrirLeBrouillonRelu"}""", 2147483647)]
    public void L_interface_demande_le_brouillon_relu_par_le_seul_identifiant_de_la_relance(string source, string message, int attendue)
    {
        Assert.True(ReglesDeLaFenetre.DemandeLeBrouillonRelu(source, message, Origine, out var relance));
        Assert.Equal(attendue, relance);
        // Ce n'est ni le brouillon de #14, ni une autre demande.
        Assert.False(ReglesDeLaFenetre.DemandeLeBrouillon(source, message, Origine, out _));
        Assert.False(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
        Assert.False(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, message, Origine));
    }

    [Fact]
    public void Le_message_de_14_ne_demande_pas_le_brouillon_relu()
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeBrouillonRelu("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillon","relance":7}""", Origine, out _));
    }

    [Theory]
    [InlineData("""{"type":"ouvrirLeBrouillonRelu","relance":7,"texte":"Un autre texte."}""")]
    [InlineData("""{"type":"ouvrirLeBrouillonRelu","relance":7,"corps":"Un autre texte.","destinataire":"pirate@exemple.invalid"}""")]
    [InlineData("""{"type":"ouvrirLeBrouillonRelu","relance":7,"autre":null}""")]
    [InlineData("""{"type":"ouvrirLeBrouillonRelu","relance":7,"relance":8}""")]
    [InlineData("""{"type":"ouvrirLeBrouillonRelu","type":"ouvrirLeBrouillonRelu","relance":7}""")]
    public void Un_champ_de_plus_que_le_type_et_la_relance_n_ouvre_aucun_brouillon_relu(string message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeBrouillonRelu("http://127.0.0.1:52344/", message, Origine, out _));
    }

    [Theory]
    [InlineData("http://localhost:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":7}""")]
    [InlineData("https://exemple.invalid/", """{"type":"ouvrirLeBrouillonRelu","relance":7}""")]
    [InlineData("about:blank", """{"type":"ouvrirLeBrouillonRelu","relance":7}""")]
    [InlineData(null, """{"type":"ouvrirLeBrouillonRelu","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":null}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":"7"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":7.5}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":0}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":-3}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":2147483648}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"ouvrirLeBrouillonRelu","relance":[7]}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"OuvrirLeBrouillonRelu","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", """{"Type":"ouvrirLeBrouillonRelu","relance":7}""")]
    [InlineData("http://127.0.0.1:52344/", "{pas du json")]
    [InlineData("http://127.0.0.1:52344/", "")]
    [InlineData("http://127.0.0.1:52344/", null)]
    public void Aucun_autre_message_n_ouvre_de_brouillon_relu(string? source, string? message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLeBrouillonRelu(source, message, Origine, out _));
    }

    [Theory]
    [InlineData(nameof(IssueDuBrouillon.Ouvert), "ouvert")]
    [InlineData(nameof(IssueDuBrouillon.Refuse), "refuse")]
    [InlineData(nameof(IssueDuBrouillon.Impossible), "impossible")]
    public void La_reponse_a_son_propre_type_et_ne_porte_que_la_relance_et_l_issue(string issue, string attendue)
    {
        using var message = JsonDocument.Parse(ReglesDeLaFenetre.MessageDuBrouillonRelu(12, Enum.Parse<IssueDuBrouillon>(issue)));

        Assert.Equal(["type", "relance", "issue"], message.RootElement.EnumerateObject().Select(propriete => propriete.Name));
        Assert.Equal("brouillonRelu", message.RootElement.GetProperty("type").GetString());
        Assert.Equal(12, message.RootElement.GetProperty("relance").GetInt32());
        Assert.Equal(attendue, message.RootElement.GetProperty("issue").GetString());
        // La réponse de #14 garde le sien.
        Assert.Contains("\"brouillonDeRelance\"", ReglesDeLaFenetre.MessageDuBrouillon(12, Enum.Parse<IssueDuBrouillon>(issue)), StringComparison.Ordinal);
    }

    [Fact]
    public void La_longueur_que_l_application_calcule_est_celle_du_lien_que_la_fenetre_batit()
    {
        var hasard = new Random(47);
        string[] morceaux = ["Bonjour", " ", ",", "\n", "\r\n", "\r", "é", "à", "€", "😀", "?", "&", "#", "=", "+", "%", "'", "\"", "<b>", "~", "_", ".", "-", "\t", "œ", "ß"];
        string?[] adresses = [null, "jeanne.martin@exemple.invalid", "rh+candidatures@exemple.invalid", "o'brien@exemple.invalid"];
        for (var essai = 0; essai < 300; essai++)
        {
            var corps = new StringBuilder();
            var objet = new StringBuilder("Candidature au poste de ");
            for (var i = hasard.Next(0, 80); i > 0; i--)
            {
                corps.Append(morceaux[hasard.Next(morceaux.Length)]);
                objet.Append(morceaux[hasard.Next(morceaux.Length)].Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal));
            }

            var brouillon = new BrouillonDeRelance(adresses[hasard.Next(adresses.Length)], objet.ToString(), corps.ToString());

            Assert.Equal(LienDuBrouillon.Construire(brouillon)!.Length, brouillon.LongueurDuLien());
        }
    }
}
