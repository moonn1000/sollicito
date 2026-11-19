using Sollicito.Domain.Temps;
using static Sollicito.Domain.Tests.Outils.Fuseaux;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Les heures saisies se convertissent en UTC dans le fuseau du poste (ADR-0005 ; modèle de domaine, « Dates et
/// heures »), sur les valeurs relevées par la sonde de #6.
/// </summary>
public sealed class HeureLocaleTests
{
    public static TheoryData<DateTime, DateTime> Conversions => new()
    {
        // Heure d'hiver, puis heure d'été.
        { Saisie(2026, 3, 27, 23, 30), Utc(2026, 3, 27, 22, 30) },
        { Saisie(2026, 11, 18, 14, 30), Utc(2026, 11, 18, 13, 30) },
        { Saisie(2026, 7, 1, 14, 30), Utc(2026, 7, 1, 12, 30) },
        // Une heure qui existe deux fois, au retour à l'heure d'hiver, se lit à l'heure d'hiver (UTC+1).
        { Saisie(2026, 10, 25, 2, 30), Utc(2026, 10, 25, 1, 30) },
        { Saisie(2027, 10, 31, 2, 30), Utc(2027, 10, 31, 1, 30) },
        // Juste avant et juste après le trou du passage à l'heure d'été.
        { Saisie(2026, 3, 29, 1, 59), Utc(2026, 3, 29, 0, 59) },
        { Saisie(2026, 3, 29, 3, 0), Utc(2026, 3, 29, 1, 0) },
    };

    [Theory]
    [MemberData(nameof(Conversions))]
    public void Une_heure_saisie_se_convertit_en_UTC(DateTime saisie, DateTime attendu)
    {
        Assert.True(HeureLocale.EssayerVersUtc(saisie, Bruxelles, out var instant));

        Assert.Equal(attendu, instant);
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    [Theory]
    [InlineData(2026, 3, 29, 2, 0)]
    [InlineData(2026, 3, 29, 2, 30)]
    [InlineData(2026, 3, 29, 2, 59)]
    [InlineData(2027, 3, 28, 2, 30)]
    public void Une_heure_du_trou_du_passage_a_l_heure_d_ete_n_existe_pas(int annee, int mois, int jour, int heure, int minute)
    {
        Assert.False(HeureLocale.EssayerVersUtc(Saisie(annee, mois, jour, heure, minute), Bruxelles, out _));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void Une_heure_qui_porte_deja_un_fuseau_n_est_pas_une_saisie(DateTimeKind genre)
    {
        var heure = new DateTime(2026, 11, 18, 14, 30, 0, genre);

        Assert.Throws<ArgumentException>(() => HeureLocale.EssayerVersUtc(heure, Bruxelles, out _));
    }
}
