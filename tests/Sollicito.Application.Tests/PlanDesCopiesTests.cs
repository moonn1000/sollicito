using Sollicito.Application.Sauvegardes;

namespace Sollicito.Application.Tests;

/// <summary>
/// Ce que Sollicito copie et ce qu'il garde (#26) : une copie par jour d'utilisation, et les sept dernières copies du jour
/// de l'installation, sans toucher à rien d'autre.
/// </summary>
public sealed class PlanDesCopiesTests
{
    private const string Installation = "1a2b3c4d";
    private const string AutreInstallation = "99aa88bb";

    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    [Fact]
    public void Sans_aucune_copie_la_copie_du_jour_est_a_faire()
    {
        Assert.True(PlanDesCopies.ACopier([], Installation, new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void Avec_la_copie_d_hier_seulement_la_copie_du_jour_est_a_faire()
    {
        Assert.True(PlanDesCopies.ACopier(["sollicito-1a2b3c4d-2026-11-17.db"], Installation, new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void Avec_la_copie_du_jour_rien_n_est_a_faire()
    {
        Assert.False(PlanDesCopies.ACopier(["sollicito-1a2b3c4d-2026-11-17.db", "sollicito-1a2b3c4d-2026-11-18.db"], Installation, new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void La_copie_du_jour_d_un_autre_poste_ne_compte_pas()
    {
        Assert.True(PlanDesCopies.ACopier(["sollicito-99aa88bb-2026-11-18.db"], Installation, new DateOnly(2026, 11, 18)));
    }

    [Theory]
    // 23:59 à Bruxelles, en hiver : encore le 18.
    [InlineData("2026-11-18T22:59:00Z", 18)]
    // Minuit passé : le 19, alors qu'il n'est que 23:00 en UTC.
    [InlineData("2026-11-18T23:00:00Z", 19)]
    [InlineData("2026-11-18T23:30:00Z", 19)]
    public void Le_jour_d_une_copie_est_le_jour_local_du_poste(string instant, int jour)
    {
        Assert.Equal(new DateOnly(2026, 11, jour), PlanDesCopies.JourLocal(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture), Bruxelles));
    }

    [Fact]
    public void La_rotation_garde_les_sept_copies_les_plus_recentes_de_l_installation()
    {
        string[] noms = [.. Enumerable.Range(1, 9).Select(jour => $"sollicito-1a2b3c4d-2026-11-{jour:00}.db")];

        Assert.Equal(["sollicito-1a2b3c4d-2026-11-02.db", "sollicito-1a2b3c4d-2026-11-01.db"], PlanDesCopies.AEffacer(noms, Installation));
    }

    [Fact]
    public void La_rotation_ne_touche_ni_aux_autres_fichiers_ni_aux_copies_d_un_autre_poste_ni_a_celles_d_avant_une_mise_a_jour()
    {
        string[] noms =
        [
            .. Enumerable.Range(1, 8).Select(jour => $"sollicito-1a2b3c4d-2026-11-{jour:00}.db"),
            .. Enumerable.Range(1, 9).Select(jour => $"sollicito-99aa88bb-2026-10-{jour:00}.db"),
            "sollicito-avant-20261119133602_CandidaturesEtEntreprises.db",
            "sollicito-avant-20261120164027_CorrectionsDesEtapes.db",
            "notes-personnelles.txt",
            "sollicito-1a2b3c4d-2026-10-01.db.0a1b2c3d.partielle",
            "sollicito.db",
        ];

        Assert.Equal(["sollicito-1a2b3c4d-2026-11-01.db"], PlanDesCopies.AEffacer(noms, Installation));
        Assert.Equal(
            ["sollicito-99aa88bb-2026-10-02.db", "sollicito-99aa88bb-2026-10-01.db"],
            PlanDesCopies.AEffacer(noms, AutreInstallation));
    }

    [Fact]
    public void Sept_copies_ou_moins_ne_donnent_rien_a_effacer()
    {
        Assert.Empty(PlanDesCopies.AEffacer([.. Enumerable.Range(1, 7).Select(jour => $"sollicito-1a2b3c4d-2026-11-{jour:00}.db")], Installation));
        Assert.Equal(7, PlanDesCopies.CopiesDuJourGardees);
    }

    [Fact]
    public void Les_copies_d_avant_une_mise_a_jour_se_rangent_de_la_plus_recente_a_la_plus_ancienne()
    {
        var copies = PlanDesCopies.CopiesDAvantUneMiseAJour(
        [
            "sollicito-avant-20261119133602_CandidaturesEtEntreprises.db",
            "sollicito-1a2b3c4d-2026-11-18.db",
            "sollicito-avant-20261120164027_CorrectionsDesEtapes.db",
        ]);

        Assert.Equal(["20261120164027_CorrectionsDesEtapes", "20261119133602_CandidaturesEtEntreprises"], copies.Select(copie => copie.Migration));
    }
}
