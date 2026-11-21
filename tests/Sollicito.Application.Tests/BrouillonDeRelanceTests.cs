using Sollicito.Application.Contacts;
using Sollicito.Application.Relances;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le brouillon d'une relance (#14, ADR-0011) : le minimum, mot pour mot, avec le poste, la date de l'envoi et le nom
/// du contact ; sans contact, sans destinataire. Toutes les données sont fictives.
/// </summary>
public sealed class BrouillonDeRelanceTests
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    // Le 27 septembre 2026 à 23:30, heure de Bruxelles (UTC+2).
    private static readonly DateTime Envoi = new(2026, 9, 27, 21, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Le_brouillon_s_adresse_au_contact_et_reprend_le_poste_et_la_date_de_l_envoi()
    {
        var brouillon = BrouillonDeRelance.De(
            new RelancePourLeBrouillon(1, "Développeur .NET (H/F)", Envoi, new ContactConnu(4, "Jeanne Martin", "jeanne.martin@exemple.invalid")),
            Bruxelles);

        Assert.Equal("jeanne.martin@exemple.invalid", brouillon.Destinataire);
        Assert.Equal("Candidature au poste de Développeur .NET (H/F)", brouillon.Objet);
        Assert.Equal(
            "Bonjour Jeanne Martin,\n\n"
            + "Je me permets de revenir vers vous au sujet de ma candidature au poste de Développeur .NET (H/F), envoyée le "
            + "27 septembre 2026. Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.\n\n"
            + "Bien cordialement,",
            brouillon.Corps);
    }

    [Fact]
    public void Sans_contact_le_brouillon_s_ouvre_sans_destinataire()
    {
        var brouillon = BrouillonDeRelance.De(new RelancePourLeBrouillon(1, "Comptable", Envoi, Destinataire: null), Bruxelles);

        Assert.Null(brouillon.Destinataire);
        Assert.StartsWith("Bonjour,\n\nJe me permets", brouillon.Corps, StringComparison.Ordinal);
    }

    [Fact]
    public void La_date_de_l_envoi_est_le_jour_local_pas_celui_d_UTC()
    {
        // Le 1er octobre 2026 à 00:30, heure de Bruxelles : encore le 30 septembre en UTC.
        var brouillon = BrouillonDeRelance.De(
            new RelancePourLeBrouillon(1, "Comptable", new DateTime(2026, 9, 30, 22, 30, 0, DateTimeKind.Utc), Destinataire: null),
            Bruxelles);

        Assert.Contains("envoyée le 1er octobre 2026.", brouillon.Corps, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 1, 1, "1er janvier 2026")]
    [InlineData(2026, 2, 2, "2 février 2026")]
    [InlineData(2026, 3, 31, "31 mars 2026")]
    [InlineData(2026, 4, 10, "10 avril 2026")]
    [InlineData(2026, 5, 1, "1er mai 2026")]
    [InlineData(2026, 6, 15, "15 juin 2026")]
    [InlineData(2026, 7, 14, "14 juillet 2026")]
    [InlineData(2026, 8, 21, "21 août 2026")]
    [InlineData(2026, 9, 9, "9 septembre 2026")]
    [InlineData(2026, 10, 30, "30 octobre 2026")]
    [InlineData(2026, 11, 11, "11 novembre 2026")]
    [InlineData(2026, 12, 25, "25 décembre 2026")]
    public void Une_date_s_ecrit_en_toutes_lettres_en_francais(int annee, int mois, int jour, string attendue)
    {
        Assert.Equal(attendue, BrouillonDeRelance.EnToutesLettres(new DateOnly(annee, mois, jour)));
    }
}
