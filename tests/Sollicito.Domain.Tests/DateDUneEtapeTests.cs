using Sollicito.Domain.Candidatures;
using static Sollicito.Domain.Tests.Outils.Fuseaux;

namespace Sollicito.Domain.Tests;

/// <summary>
/// La date et l'heure d'une étape (modèle de domaine, « Noter une étape ») : saisies dans le fuseau du poste, gardées en
/// UTC, jamais avant l'envoi, ni plus d'un an après l'instant présent.
/// </summary>
public sealed class DateDUneEtapeTests
{
    // Le 18/11/2026 à 14:30, heure de Bruxelles.
    private static readonly DateTime Maintenant = Utc(2026, 11, 18, 13, 30);

    // Envoyée le 01/10/2026 à 10:00, heure de Bruxelles (UTC+2, heure d'été).
    private static readonly DateTime Envoi = Utc(2026, 10, 1, 8, 0);

    [Theory]
    [InlineData(2026, 11, 10, 14, 0, "2026-11-10T13:00:00Z")]
    // À venir, et à l'heure d'été : UTC+2.
    [InlineData(2027, 7, 1, 14, 30, "2027-07-01T12:30:00Z")]
    // Une heure qui existe deux fois, au retour à l'heure d'hiver, se lit à l'heure d'hiver.
    [InlineData(2026, 10, 25, 2, 30, "2026-10-25T01:30:00Z")]
    // Les deux bornes : l'envoi lui-même, et un an après l'instant présent, en heure locale.
    [InlineData(2026, 10, 1, 10, 0, "2026-10-01T08:00:00Z")]
    [InlineData(2027, 11, 18, 14, 30, "2027-11-18T13:30:00Z")]
    public void Une_etape_passee_ou_a_venir_se_lit_en_UTC_dans_le_fuseau_du_poste(int annee, int mois, int jour, int heure, int minute, string attendu)
    {
        Assert.True(DateDUneEtape.EssayerDeLire(Saisie(annee, mois, jour, heure, minute), Bruxelles, Maintenant, Envoi, out var instant, out var erreur), erreur);

        Assert.Equal(DateTimeKind.Utc, instant.Kind);
        Assert.Equal(attendu, instant.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(2026, 10, 1, 9, 59)]
    [InlineData(2026, 9, 30, 23, 0)]
    [InlineData(1999, 12, 31, 23, 59)]
    [InlineData(1, 1, 1, 0, 0)]
    public void Une_etape_avant_l_envoi_est_refusee(int annee, int mois, int jour, int heure, int minute)
    {
        Assert.False(DateDUneEtape.EssayerDeLire(Saisie(annee, mois, jour, heure, minute), Bruxelles, Maintenant, Envoi, out _, out var erreur));

        Assert.Equal("La date et l'heure ne peuvent pas précéder l'envoi de la candidature.", erreur);
    }

    [Theory]
    [InlineData(2027, 11, 18, 14, 31)]
    [InlineData(2062, 11, 18, 14, 30)]
    [InlineData(9999, 12, 31, 23, 59)]
    public void Une_etape_plus_d_un_an_apres_l_instant_present_est_refusee(int annee, int mois, int jour, int heure, int minute)
    {
        Assert.False(DateDUneEtape.EssayerDeLire(Saisie(annee, mois, jour, heure, minute), Bruxelles, Maintenant, Envoi, out _, out var erreur));

        Assert.Equal("La date et l'heure ne peuvent pas être à plus d'un an dans le futur.", erreur);
    }

    [Fact]
    public void Une_etape_dans_le_trou_du_passage_a_l_heure_d_ete_est_refusee()
    {
        Assert.False(DateDUneEtape.EssayerDeLire(Saisie(2027, 3, 28, 2, 30), Bruxelles, Maintenant, Envoi, out _, out var erreur));

        Assert.Equal("Cette heure n'existe pas : ce jour-là, au passage à l'heure d'été, l'horloge avance d'une heure.", erreur);
    }

    [Fact]
    public void L_instant_present_et_l_envoi_se_donnent_en_UTC()
    {
        var local = DateTime.SpecifyKind(Maintenant, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => DateDUneEtape.EssayerDeLire(Saisie(2026, 11, 10, 14, 0), Bruxelles, local, Envoi, out _, out _));
        Assert.Throws<ArgumentException>(() => DateDUneEtape.EssayerDeLire(Saisie(2026, 11, 10, 14, 0), Bruxelles, Maintenant, DateTime.SpecifyKind(Envoi, DateTimeKind.Local), out _, out _));
    }
}
