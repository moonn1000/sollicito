using Sollicito.Domain.Offres;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Le texte d'une offre collée (#36, ADR-0013) : gardé tel que collé, ses fins de ligne écrites U+000A et ses blancs de
/// bord retirés ; requis, de 50 000 caractères au plus ; refusé, à la ligne du premier caractère refusé, pour les
/// caractères que refuse un texte saisi (#11).
/// </summary>
public sealed class TexteDUneOffreTests
{
    private static string Car(int pointDeCode) => char.ConvertFromUtf32(pointDeCode);

    private static string Unite(int unite) => ((char)unite).ToString();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    public void Un_texte_absent_ou_blanc_est_refuse(string? saisie)
    {
        Assert.False(TexteDUneOffre.EssayerDeLire(saisie, out var texte, out var erreur));

        Assert.Null(texte);
        Assert.Equal(TexteDUneOffre.Manquant, erreur);
    }

    [Fact]
    public void Le_texte_est_garde_tel_quel_balises_accents_tabulations_et_lignes_vides_compris()
    {
        const string colle = "<h1>Développeur .NET</h1>\n\n**Missions** :\t<script>alert(1)</script>\n  - « Œuvres » & co";

        Assert.True(TexteDUneOffre.EssayerDeLire("  \n" + colle + "\n  ", out var texte, out var erreur), erreur);

        Assert.Equal(colle, texte);
    }

    [Fact]
    public void Toute_fin_de_ligne_s_ecrit_U_000A_et_CRLF_n_en_compte_qu_une()
    {
        var colle = "a\r\nb\rc\vd\fe" + Unite(0x85) + "f" + Unite(0x2028) + "g" + Unite(0x2029) + "h\n\ni";

        Assert.True(TexteDUneOffre.EssayerDeLire(colle, out var texte, out var erreur), erreur);

        Assert.Equal("a\nb\nc\nd\ne\nf\ng\nh\n\ni", texte);
    }

    [Fact]
    public void Un_texte_de_la_longueur_maximale_est_admis_un_caractere_de_plus_est_refuse()
    {
        // Les fins de ligne se comptent une fois ramenées à un seul caractère.
        var auMaximum = new string('a', TexteDUneOffre.LongueurMaximale - 2) + "\r\nb";

        Assert.True(TexteDUneOffre.EssayerDeLire(auMaximum, out var texte, out _));
        Assert.Equal(TexteDUneOffre.LongueurMaximale, texte!.Length);

        Assert.False(TexteDUneOffre.EssayerDeLire(auMaximum + "c", out _, out var erreur));
        Assert.Equal(TexteDUneOffre.TropLong, erreur);
    }

    public static TheoryData<string> CaracteresRefuses() =>
    [
        Unite(0x0000), Unite(0x0001), Unite(0x001F), Unite(0x007F), Unite(0x0090),
        Unite(0x202A), Unite(0x202E), Unite(0x2066), Unite(0x2069),
        Unite(0xFDD0), Unite(0xFFFE), Unite(0xFFFF), Car(0x1FFFE), Car(0x10FFFF),
        Unite(0xD800), Unite(0xDC00),
    ];

    [Theory]
    [MemberData(nameof(CaracteresRefuses))]
    public void Un_caractere_refuse_fait_refuser_le_texte_a_sa_ligne_sans_reprendre_le_texte(string refuse)
    {
        // Ligne 1, ligne 2 après CRLF, ligne 3 après un saut de page, ligne 4 après un séparateur de ligne.
        var colle = "Offre fictive\r\nDeuxième ligne\fTroisième ligne" + Unite(0x2028) + "Quatrième " + refuse + " ligne";

        Assert.False(TexteDUneOffre.EssayerDeLire(colle, out var texte, out var erreur));

        Assert.Null(texte);
        Assert.Equal(TexteDUneOffre.CaractereRefuse(4), erreur);
        Assert.DoesNotContain("fictive", erreur, StringComparison.Ordinal);
    }

    [Fact]
    public void La_ligne_se_compte_dans_le_texte_tel_que_l_ecran_le_montre_lignes_vides_du_debut_comprises()
    {
        Assert.False(TexteDUneOffre.EssayerDeLire("\n\nTitre" + Unite(0x0007), out _, out var erreur));

        Assert.Equal("Le texte de l'offre contient, à la ligne 3, un caractère de contrôle ou invalide : retirez-le, puis réessayez.", erreur);
    }

    [Fact]
    public void Une_paire_de_substitution_entiere_est_admise()
    {
        Assert.True(TexteDUneOffre.EssayerDeLire("Équipe sympa " + Car(0x1F600), out var texte, out var erreur), erreur);

        Assert.EndsWith(Car(0x1F600), texte, StringComparison.Ordinal);
    }
}
