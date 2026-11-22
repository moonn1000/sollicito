using Sollicito.Domain.Candidatures;
using static Sollicito.Domain.Tests.Outils.Fuseaux;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Une candidature part du statut Envoyée, avec sa date et son heure d'envoi, gardées en UTC et jamais dans le futur
/// (modèle de domaine, « Statuts d'une candidature »).
/// </summary>
public sealed class CandidatureTests
{
    // Le 18/11/2026 à 14:30, heure de Bruxelles.
    private static readonly DateTime Maintenant = Utc(2026, 11, 18, 13, 30);

    [Fact]
    public void Une_candidature_part_toujours_du_statut_Envoyee()
    {
        var candidature = Candidature.Enregistrer(7, "  Développeuse .NET ", Utc(2026, 3, 27, 22, 30), Maintenant);

        Assert.Equal(StatutDeCandidature.Envoyee, candidature.Statut);
        Assert.Equal(7, candidature.EntrepriseId);
        Assert.Equal("Développeuse .NET", candidature.Poste);
        Assert.Equal(Utc(2026, 3, 27, 22, 30), candidature.EnvoyeeLe);
    }

    [Fact]
    public void Une_candidature_saisie_sans_offre_n_en_designe_aucune()
    {
        Assert.Null(Candidature.Enregistrer(7, "Comptable", Maintenant, Maintenant).OffreId);
    }

    [Fact]
    public void Postuler_a_une_offre_fait_une_candidature_envoyee_qui_la_designe()
    {
        var candidature = Candidature.Postuler(12, 7, " Comptable (H/F) ", Utc(2026, 11, 18, 9, 0), Maintenant);

        Assert.Equal(12, candidature.OffreId);
        Assert.Equal(7, candidature.EntrepriseId);
        Assert.Equal("Comptable (H/F)", candidature.Poste);
        Assert.Equal(StatutDeCandidature.Envoyee, candidature.Statut);
        Assert.Empty(candidature.Evenements);
    }

    [Fact]
    public void Postuler_suit_les_regles_de_l_enregistrement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Candidature.Postuler(0, 7, "Comptable", Maintenant, Maintenant));
        Assert.Throws<ArgumentException>(() => Candidature.Postuler(12, 7, "Comptable", Maintenant.AddSeconds(1), Maintenant));
        Assert.Throws<ArgumentException>(() => Candidature.Postuler(12, 7, "Comp\u0007table", Maintenant, Maintenant));
    }

    [Fact]
    public void La_modification_d_une_candidature_ne_la_detache_pas_de_son_offre()
    {
        var candidature = Candidature.Postuler(12, 7, "Comptable", Utc(2026, 11, 18, 9, 0), Maintenant);

        candidature.RattacherA(8);
        candidature.ChangerDePoste("Aide-comptable");
        candidature.CorrigerLEnvoi(Utc(2026, 11, 17, 9, 0), Maintenant);
        Assert.True(candidature.EssayerDeChangerDeStatut(StatutDeCandidature.Refusee, Maintenant, out _));

        Assert.Equal(12, candidature.OffreId);
    }

    [Fact]
    public void Une_candidature_envoyee_a_l_instant_present_est_admise()
    {
        Assert.Equal(Maintenant, Candidature.Enregistrer(1, "Comptable", Maintenant, Maintenant).EnvoyeeLe);
    }

    [Fact]
    public void Une_candidature_ne_s_enregistre_pas_dans_le_futur()
    {
        var erreur = Assert.Throws<ArgumentException>(() => Candidature.Enregistrer(1, "Comptable", Maintenant.AddSeconds(1), Maintenant));

        Assert.StartsWith(DateDEnvoi.DansLeFutur, erreur.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Une_candidature_sans_poste_est_refusee(string? poste)
    {
        Assert.Throws<ArgumentException>(() => Candidature.Enregistrer(1, poste, Maintenant, Maintenant));
    }

    [Fact]
    public void Une_candidature_sans_entreprise_enregistree_est_refusee()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Candidature.Enregistrer(0, "Comptable", Maintenant, Maintenant));
    }

    [Fact]
    public void Un_envoi_qui_n_est_pas_en_UTC_est_refuse()
    {
        var local = DateTime.SpecifyKind(Maintenant, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => Candidature.Enregistrer(1, "Comptable", local, Maintenant));
    }

    [Fact]
    public void L_envoi_saisi_se_lit_en_UTC_dans_le_fuseau_du_poste()
    {
        Assert.True(DateDEnvoi.EssayerDeLire(Saisie(2026, 3, 27, 23, 30), Bruxelles, Maintenant, out var instant, out _));

        Assert.Equal(Utc(2026, 3, 27, 22, 30), instant);
    }

    [Theory]
    [InlineData(2026, 11, 18, 14, 30)]
    [InlineData(2026, 11, 18, 14, 0)]
    [InlineData(2000, 1, 1, 0, 0)]
    public void Un_envoi_jusqu_a_l_instant_present_est_admis(int annee, int mois, int jour, int heure, int minute)
    {
        Assert.True(DateDEnvoi.EssayerDeLire(Saisie(annee, mois, jour, heure, minute), Bruxelles, Maintenant, out _, out _));
    }

    [Theory]
    [InlineData(2026, 11, 18, 14, 31)]
    [InlineData(2026, 11, 19, 0, 0)]
    [InlineData(2027, 1, 1, 0, 0)]
    [InlineData(9999, 12, 31, 23, 59)]
    public void Un_envoi_dans_le_futur_est_refuse(int annee, int mois, int jour, int heure, int minute)
    {
        Assert.False(DateDEnvoi.EssayerDeLire(Saisie(annee, mois, jour, heure, minute), Bruxelles, Maintenant, out _, out var erreur));

        Assert.Equal("La date et l'heure d'envoi ne peuvent pas être dans le futur.", erreur);
    }

    [Fact]
    public void Un_envoi_dans_le_trou_du_passage_a_l_heure_d_ete_est_refuse()
    {
        Assert.False(DateDEnvoi.EssayerDeLire(Saisie(2026, 3, 29, 2, 30), Bruxelles, Maintenant, out _, out var erreur));

        Assert.Equal("Cette heure n'existe pas : ce jour-là, au passage à l'heure d'été, l'horloge avance d'une heure.", erreur);
    }

    [Theory]
    [InlineData(1999, 12, 31, 23, 59)]
    [InlineData(1, 1, 1, 0, 0)]
    public void Un_envoi_avant_l_an_2000_est_refuse(int annee, int mois, int jour, int heure, int minute)
    {
        Assert.False(DateDEnvoi.EssayerDeLire(Saisie(annee, mois, jour, heure, minute), Bruxelles, Maintenant, out _, out var erreur));

        Assert.Equal("La date d'envoi ne peut pas être antérieure au 1er janvier 2000.", erreur);
    }
}
