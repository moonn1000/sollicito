using Sollicito.Domain.Offres;

namespace Sollicito.Domain.Tests;

/// <summary>
/// La date de publication d'une ligne de CSV (#40, ADR-0015) : <c>JJ/MM/AAAA</c> en plus de <c>AAAA-MM-JJ</c>, le jour
/// toujours d'abord, sur un ou deux chiffres comme le mois, l'année sur quatre ; aux bornes de l'offre collée. La saisie
/// d'une offre collée garde <c>AAAA-MM-JJ</c> seule.
/// </summary>
public sealed class DateDePublicationTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 24);

    [Theory]
    [InlineData("18/11/2026", 2026, 11, 18)]
    [InlineData("8/11/2026", 2026, 11, 8)]
    [InlineData("08/1/2026", 2026, 1, 8)]
    [InlineData(" 1/2/2026 ", 2026, 2, 1)]
    [InlineData("29/02/2024", 2024, 2, 29)]
    [InlineData("2026-11-18", 2026, 11, 18)]
    public void Une_ligne_de_csv_lit_la_date_le_jour_d_abord_ou_en_aaaa_mm_jj(string saisie, int annee, int mois, int jour)
    {
        Assert.True(DateDePublication.EssayerDeLire(saisie, Aujourdhui, jourEnPremierAdmis: true, out var date, out var erreur), erreur);

        Assert.Equal(new DateOnly(annee, mois, jour), date);
    }

    [Theory]
    [InlineData("11/18/2026")]
    [InlineData("31/11/2026")]
    [InlineData("29/02/2026")]
    [InlineData("18/11/26")]
    [InlineData("18-11-2026")]
    [InlineData("18.11.2026")]
    [InlineData("118/11/2026")]
    [InlineData("18/011/2026")]
    [InlineData("0/11/2026")]
    [InlineData("18/0/2026")]
    [InlineData("18/11/2026/1")]
    [InlineData("١٨/١١/٢٠٢٦")]
    [InlineData("18 / 11 / 2026")]
    public void Une_date_hors_des_deux_formes_est_refusee_par_le_message_des_deux_formes(string saisie)
    {
        Assert.False(DateDePublication.EssayerDeLire(saisie, Aujourdhui, jourEnPremierAdmis: true, out var date, out var erreur));

        Assert.Null(date);
        Assert.Equal(DateDePublication.IllisibleJourEnPremier, erreur);
    }

    [Fact]
    public void Le_jour_d_abord_garde_les_bornes_de_l_offre_collee()
    {
        Assert.False(DateDePublication.EssayerDeLire("25/11/2026", Aujourdhui, jourEnPremierAdmis: true, out _, out var future));
        Assert.Equal(DateDePublication.Future, future);
        Assert.False(DateDePublication.EssayerDeLire("31/12/1999", Aujourdhui, jourEnPremierAdmis: true, out _, out var ancienne));
        Assert.Equal(DateDePublication.TropAncienne, ancienne);
        Assert.True(DateDePublication.EssayerDeLire("24/11/2026", Aujourdhui, jourEnPremierAdmis: true, out var aujourdhui, out _));
        Assert.Equal(Aujourdhui, aujourdhui);
    }

    [Fact]
    public void L_offre_collee_garde_aaaa_mm_jj_seule()
    {
        Assert.False(DateDePublication.EssayerDeLire("18/11/2026", Aujourdhui, out _, out var erreur));

        Assert.Equal(DateDePublication.Illisible, erreur);
    }

    [Fact]
    public void La_forme_canonique_est_aaaa_mm_jj()
    {
        Assert.Equal("2026-01-08", DateDePublication.FormeCanonique(new DateOnly(2026, 1, 8)));
    }

    [Fact]
    public void La_longueur_mesuree_du_texte_ramene_les_fins_de_ligne_et_retire_les_blancs_de_bord()
    {
        Assert.Equal(3, TexteDUneOffre.LongueurMesuree("  a\r\nb \t"));
        Assert.Equal(5, TexteDUneOffre.LongueurMesuree("a\u2028b\fc"));
    }
}
