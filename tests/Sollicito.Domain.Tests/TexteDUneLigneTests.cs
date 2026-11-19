using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Tests;

/// <summary>Les textes d'une ligne saisis par l'utilisateur : présence, longueur, caractères admis.</summary>
public sealed class TexteDUneLigneTests
{
    private static readonly LibelleDuChamp Champ = new("Indiquez le poste.", "Le poste");

    [Fact]
    public void Les_blancs_du_debut_et_de_la_fin_sont_retires()
    {
        Assert.True(TexteDUneLigne.EssayerDeLire("  Développeuse .NET  ", Champ, out var texte, out _));

        Assert.Equal("Développeuse .NET", texte);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  ")]
    public void Un_texte_absent_ou_blanc_est_refuse(string? saisie)
    {
        Assert.False(TexteDUneLigne.EssayerDeLire(saisie, Champ, out _, out var erreur));

        Assert.Equal("Indiquez le poste.", erreur);
    }

    [Fact]
    public void Un_texte_de_la_longueur_maximale_est_admis_un_caractere_de_plus_est_refuse()
    {
        var auMaximum = new string('a', TexteDUneLigne.LongueurMaximale);

        Assert.True(TexteDUneLigne.EssayerDeLire(auMaximum, Champ, out _, out _));
        Assert.False(TexteDUneLigne.EssayerDeLire(auMaximum + "a", Champ, out _, out var erreur));
        Assert.Equal("Le poste ne doit pas dépasser 200 caractères.", erreur);
    }

    // Construits à l'exécution : un attribut garde ses chaînes en UTF-8, où une moitié isolée de paire de
    // substitution deviendrait un caractère de remplacement, valide.
    public static TheoryData<string> CaracteresRefuses =>
    [
        "Développeuse\n.NET",
        "Développeuse\t.NET",
        "Développeuse\r.NET",
        "Dévelo\u0000ppeuse",
        "Dévelo\u007Fppeuse",
        "Dévelo\u0085ppeuse",
        "Dévelo" + (char)0xD800 + "ppeuse",
        "Développeuse" + (char)0xDC00,
        "Dévelo" + (char)0xDC00 + (char)0xD800 + "ppeuse",
    ];

    [Theory]
    [MemberData(nameof(CaracteresRefuses))]
    public void Un_caractere_de_controle_ou_invalide_est_refuse(string saisie)
    {
        Assert.False(TexteDUneLigne.EssayerDeLire(saisie, Champ, out _, out var erreur));

        Assert.Equal("Le poste ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide.", erreur);
    }

    [Fact]
    public void Un_caractere_hors_du_plan_de_base_est_admis()
    {
        Assert.True(TexteDUneLigne.EssayerDeLire("Pâtissière 🧁", Champ, out var texte, out _));

        Assert.Equal("Pâtissière 🧁", texte);
    }

    [Fact]
    public void Le_message_ne_reprend_jamais_le_texte_refuse()
    {
        var saisie = new string('x', TexteDUneLigne.LongueurMaximale) + "Dupont-Fictif";

        Assert.False(TexteDUneLigne.EssayerDeLire(saisie, Champ, out _, out var erreur));

        Assert.DoesNotContain("Dupont-Fictif", erreur, StringComparison.Ordinal);
    }
}
