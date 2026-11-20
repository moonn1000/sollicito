using Sollicito.Domain.Candidatures;

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

    // Construits à l'exécution : un attribut garde ses chaînes en UTF-8, où une moitié isolée de paire de
    // substitution deviendrait un caractère de remplacement, valide.
    public static TheoryData<string> CaracteresRefuses =>
    [
        "Appel\u0000reçu",
        "Appel\u0007reçu",
        "Appel\u001Breçu",
        "Appel\u007Freçu",
        "Appel\u0085reçu",
        "Appel\u000Breçu",
        "Appel\u000Creçu",
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
