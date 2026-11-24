using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Offres;

namespace Sollicito.Domain.Tests;

/// <summary>
/// La note d'une étape (modèle de domaine, « Noter une étape ») : facultative, sur plusieurs lignes, de 2000 caractères
/// au plus, sans caractère de contrôle autre que le saut de ligne et la tabulation, ni caractère invalide.
/// </summary>
public sealed class NoteDUneEtapeTests
{
    private const string MessageDesCaracteres = "La note ne doit contenir, hors sauts de ligne et tabulations, aucun caractère de contrôle ou invalide.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t\n ")]
    public void Une_note_absente_ou_blanche_ne_fait_aucune_note(string? saisie)
    {
        Assert.True(Etape.EssayerDeLireLaNote(saisie, out var note, out _));

        Assert.Null(note);
    }

    [Theory]
    [InlineData("  Rappel prévu lundi.  ", "Rappel prévu lundi.")]
    [InlineData("Ligne 1\r\nLigne 2", "Ligne 1\nLigne 2")]
    [InlineData("Ligne 1\rLigne 2\n\nLigne 4", "Ligne 1\nLigne 2\n\nLigne 4")]
    [InlineData("Salaire :\t3 000 € brut", "Salaire :\t3 000 € brut")]
    // Les sept formes de fin de ligne du modèle de domaine (« Coller une offre ») : CRLF, CR, U+000B, U+000C, U+0085,
    // U+2028 et U+2029. Une note les ramène toutes à U+000A, comme le texte d'une offre (#82, point 10).
    [InlineData("Ligne 1\u000BLigne 2", "Ligne 1\nLigne 2")]
    [InlineData("Ligne 1\u000CLigne 2", "Ligne 1\nLigne 2")]
    [InlineData("Ligne 1\u0085Ligne 2", "Ligne 1\nLigne 2")]
    [InlineData("Ligne 1\u2028Ligne 2", "Ligne 1\nLigne 2")]
    [InlineData("Ligne 1\u2029Ligne 2", "Ligne 1\nLigne 2")]
    [InlineData("Ligne 1\r\nLigne 2\rLigne 3\u2028Ligne 4\u2029\u2029Ligne 6", "Ligne 1\nLigne 2\nLigne 3\nLigne 4\n\nLigne 6")]
    [InlineData("<b>gras</b> & \"guillemets\"", "<b>gras</b> & \"guillemets\"")]
    public void Une_note_garde_ses_lignes_et_ses_tabulations_et_perd_ses_blancs_d_extremite(string saisie, string attendue)
    {
        Assert.True(Etape.EssayerDeLireLaNote(saisie, out var note, out var erreur), erreur);

        Assert.Equal(attendue, note);
    }

    [Fact]
    public void Une_note_de_la_longueur_maximale_est_admise_un_caractere_de_plus_est_refuse()
    {
        // Les fins de ligne se comptent une fois ramenées à un seul caractère.
        var auMaximum = new string('a', Etape.LongueurMaximaleDeLaNote - 2) + "\r\nb";

        Assert.True(Etape.EssayerDeLireLaNote(auMaximum, out var note, out _));
        Assert.Equal(Etape.LongueurMaximaleDeLaNote, note!.Length);
        Assert.False(Etape.EssayerDeLireLaNote(auMaximum + "c", out _, out var erreur));
        Assert.Equal("La note ne doit pas dépasser 2000 caractères.", erreur);
    }

    // Les sept formes, et le saut de ligne lui-même, comme le texte d'une offre les lit.
    public static TheoryData<string> LesFinsDeLigne =>
    [
        "\r\n",
        "\r",
        "\n",
        "\u000B",
        "\u000C",
        "\u0085",
        "" + (char)0x2028,
        "" + (char)0x2029,
    ];

    // La fonction des fins de ligne est écrite deux fois, pour la note et pour le texte d'une offre : ce test garde
    // qu'elles disent la même chose, tant que l'offre ne reprend pas la fonction commune (#82, point 10).
    [Theory]
    [MemberData(nameof(LesFinsDeLigne))]
    public void Une_note_et_un_texte_d_offre_ramenent_une_fin_de_ligne_de_la_meme_facon(string fin)
    {
        var saisie = "Ligne 1" + fin + "Ligne 2";

        Assert.True(TexteDUneOffre.EssayerDeLire(saisie, out var texteDeLOffre, out _));
        Assert.True(Etape.EssayerDeLireLaNote(saisie, out var note, out _));

        Assert.Equal(texteDeLOffre, note);
        Assert.Equal("Ligne 1\nLigne 2", note);
    }

    // Construits à l'exécution : un attribut garde ses chaînes en UTF-8, où une moitié isolée de paire de
    // substitution deviendrait un caractère de remplacement, valide.
    public static TheoryData<string> CaracteresRefuses =>
    [
        "Appel\u0000reçu",
        "Appel\u0007reçu",
        "Appel\u001Breçu",
        "Appel\u007Freçu",
        "Appel" + (char)0x202E + "uçer",
        "Appel" + (char)0x2067 + "reçu",
        "Appel" + (char)0xD800 + "reçu",
        "Appel reçu" + (char)0xDC00,
        "Appel" + (char)0xFFFE + "reçu",
        "Appel" + (char)0xFDD0 + "reçu",
        "Appel" + char.ConvertFromUtf32(0x10FFFF) + "reçu",
    ];

    [Theory]
    [MemberData(nameof(CaracteresRefuses))]
    public void Une_note_avec_un_autre_caractere_de_controle_ou_invalide_est_refusee_sans_etre_citee(string saisie)
    {
        Assert.False(Etape.EssayerDeLireLaNote(saisie, out var note, out var erreur));

        Assert.Null(note);
        Assert.Equal(MessageDesCaracteres, erreur);
        Assert.DoesNotContain("Appel", erreur, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_caractere_hors_du_plan_de_base_bien_forme_est_admis()
    {
        Assert.True(Etape.EssayerDeLireLaNote("Entretien réussi 🎉", out var note, out _));

        Assert.Equal("Entretien réussi 🎉", note);
    }
}
