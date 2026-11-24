using System.Text.Json;
using Sollicito.Application.IA;

namespace Sollicito.Application.Tests;

/// <summary>
/// La forme d'une clé d'API (#43, ADR-0016), et la clé en mémoire, qui ne s'écrit nulle part. Clés fictives.
/// </summary>
public sealed class FormeDUneCleTests
{
    private const string DeClaude = "sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive";

    [Theory]
    [InlineData(FournisseurDIA.Claude, DeClaude)]
    [InlineData(FournisseurDIA.OpenAI, "sk-proj-SOLLICITO-ESSAI-43-cle-fictive")]
    [InlineData(FournisseurDIA.OpenAI, "sk-svcacct-SOLLICITO-ESSAI-43-fictive")]
    [InlineData(FournisseurDIA.OpenAI, "sk-SOLLICITO-ESSAI-43-ancienne-forme")]
    public void Une_cle_a_sa_forme_est_admise(FournisseurDIA fournisseur, string saisie)
    {
        Assert.Null(FormeDUneCle.Verifier(fournisseur, saisie, out var cle));
        Assert.Equal(saisie, cle!.Valeur());
    }

    [Theory]
    [InlineData("  sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive\r\n")]
    [InlineData("\tsk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive ")]
    [InlineData("\u00a0sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive\u00a0")]
    public void Les_blancs_du_bord_sont_retires(string saisie)
    {
        Assert.Null(FormeDUneCle.Verifier(FournisseurDIA.Claude, saisie, out var cle));
        Assert.Equal(DeClaude, cle!.Valeur());
    }

    [Fact]
    public void Les_bornes_de_la_longueur_sont_admises()
    {
        var courte = "sk-ant-" + new string('a', FormeDUneCle.LongueurMinimale - 7);
        var longue = "sk-ant-" + new string('a', FormeDUneCle.LongueurMaximale - 7);

        Assert.Null(FormeDUneCle.Verifier(FournisseurDIA.Claude, courte, out _));
        Assert.Null(FormeDUneCle.Verifier(FournisseurDIA.Claude, longue, out _));
        Assert.Equal(MotifDuRefusDUneCle.LongueurHorsBornes, FormeDUneCle.Verifier(FournisseurDIA.Claude, courte[..^1], out _));
        Assert.Equal(MotifDuRefusDUneCle.LongueurHorsBornes, FormeDUneCle.Verifier(FournisseurDIA.Claude, longue + "a", out _));
    }

    [Theory]
    [InlineData(null, MotifDuRefusDUneCle.Manquante)]
    [InlineData("", MotifDuRefusDUneCle.Manquante)]
    [InlineData("   \r\n\t", MotifDuRefusDUneCle.Manquante)]
    [InlineData("sk-ant-api03-SOLLICITO ESSAI-43-cle", MotifDuRefusDUneCle.CaracteresRefuses)]
    [InlineData("sk-ant-api03-SOLLICITO-ESSAI-43-clé", MotifDuRefusDUneCle.CaracteresRefuses)]
    [InlineData("sk-ant-api03-SOLLICITO-ESSAI-43\t-cle", MotifDuRefusDUneCle.CaracteresRefuses)]
    [InlineData("sk-ant-api03-SOLLICITO-ESSAI-43\u0000-cle", MotifDuRefusDUneCle.CaracteresRefuses)]
    [InlineData("sk-ant-api03-SOLLICITO-ESSAI-43\u200b-cle", MotifDuRefusDUneCle.CaracteresRefuses)]
    [InlineData("sk-ant-api03-SOLLICITO-ESSAI-43\u202e-cle", MotifDuRefusDUneCle.CaracteresRefuses)]
    [InlineData("sk-ant-courte", MotifDuRefusDUneCle.LongueurHorsBornes)]
    [InlineData("sk-proj-SOLLICITO-ESSAI-43-cle-fictive", MotifDuRefusDUneCle.SansLePrefixeDeClaude)]
    [InlineData("SK-ANT-api03-SOLLICITO-ESSAI-43-cle", MotifDuRefusDUneCle.SansLePrefixeDeClaude)]
    public void Une_cle_saisie_pour_Claude_sans_sa_forme_est_refusee(string? saisie, MotifDuRefusDUneCle attendu)
    {
        Assert.Equal(attendu, FormeDUneCle.Verifier(FournisseurDIA.Claude, saisie, out var cle));
        Assert.Null(cle);
    }

    [Theory]
    [InlineData("pk-SOLLICITO-ESSAI-43-cle-fictive-openai", MotifDuRefusDUneCle.SansLePrefixeDOpenAI)]
    [InlineData(DeClaude, MotifDuRefusDUneCle.CleDeClaudeChezOpenAI)]
    public void Une_cle_saisie_pour_OpenAI_sans_sa_forme_est_refusee(string saisie, MotifDuRefusDUneCle attendu)
    {
        Assert.Equal(attendu, FormeDUneCle.Verifier(FournisseurDIA.OpenAI, saisie, out var cle));
        Assert.Null(cle);
    }

    [Fact]
    public void Un_refus_ne_reprend_jamais_la_saisie()
    {
        foreach (var motif in Enum.GetValues<MotifDuRefusDUneCle>())
        {
            var message = motif.Message();
            Assert.DoesNotContain("SOLLICITO", message, StringComparison.Ordinal);
            Assert.DoesNotContain("ESSAI", message, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(message));
        }

        Assert.Equal("Une clé de Claude commence par sk-ant-.", MotifDuRefusDUneCle.SansLePrefixeDeClaude.Message());
        Assert.Equal("Une clé d'API fait de 20 à 512 caractères.", MotifDuRefusDUneCle.LongueurHorsBornes.Message());
    }

    [Theory]
    [InlineData(FournisseurDIA.Aucun)]
    [InlineData(FournisseurDIA.Ollama)]
    public void Un_fournisseur_sans_cle_n_en_verifie_aucune(FournisseurDIA fournisseur)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FormeDUneCle.Verifier(fournisseur, DeClaude, out _));
    }

    [Fact]
    public void La_cle_en_memoire_ne_se_montre_ni_en_texte_ni_en_JSON()
    {
        Assert.Null(FormeDUneCle.Verifier(FournisseurDIA.Claude, DeClaude, out var cle));

        Assert.DoesNotContain("SOLLICITO", cle!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SOLLICITO", $"{cle}", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(cle));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(new { cle }));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CleDIA>($"\"{DeClaude}\""));
    }

    [Theory]
    [InlineData("Aucun", FournisseurDIA.Aucun)]
    [InlineData("Claude", FournisseurDIA.Claude)]
    [InlineData("OpenAI", FournisseurDIA.OpenAI)]
    [InlineData("Ollama", FournisseurDIA.Ollama)]
    public void Un_fournisseur_se_lit_par_son_nom_exact(string nom, FournisseurDIA attendu)
    {
        Assert.True(FournisseursDIA.EssayerDeLire(nom, out var fournisseur));
        Assert.Equal(attendu, fournisseur);
        Assert.Equal(nom, fournisseur.Nom());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("claude")]
    [InlineData("CLAUDE")]
    [InlineData("Openai")]
    [InlineData(" Claude")]
    [InlineData("Claude ")]
    [InlineData("1")]
    [InlineData("Mistral")]
    [InlineData("Claude,OpenAI")]
    public void Un_autre_nom_ne_se_lit_pas(string? nom)
    {
        Assert.False(FournisseursDIA.EssayerDeLire(nom, out _));
    }

    [Fact]
    public void Seuls_les_fournisseurs_en_ligne_prennent_une_cle()
    {
        Assert.False(FournisseurDIA.Aucun.PrendUneCle());
        Assert.True(FournisseurDIA.Claude.PrendUneCle());
        Assert.True(FournisseurDIA.OpenAI.PrendUneCle());
        Assert.False(FournisseurDIA.Ollama.PrendUneCle());
    }
}
