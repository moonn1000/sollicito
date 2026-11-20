using Sollicito.Domain.Candidatures;
using static Sollicito.Domain.Tests.Outils.Fuseaux;
using S = Sollicito.Domain.Candidatures.StatutDeCandidature;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Modifier une candidature (#25) : son entreprise, son poste et sa date d'envoi se corrigent, sans toucher ni au statut
/// ni à l'historique. L'historique commence à l'envoi : la date d'envoi corrigée ne suit jamais son premier événement,
/// quelle que soit sa sorte (modèle de domaine, « Modifier ou supprimer une candidature »).
/// </summary>
public sealed class ModificationDUneCandidatureTests
{
    // Envoyée le 02/11/2026 à 10:00, heure de Bruxelles ; le présent est le 18/11/2026 à 14:30.
    private static readonly DateTime Envoi = Utc(2026, 11, 2, 9, 0);
    private static readonly DateTime Maintenant = Utc(2026, 11, 18, 13, 30);
    private static readonly DateTime Premier = Utc(2026, 11, 10, 9, 0);
    private static readonly DateTime Deuxieme = Utc(2026, 11, 12, 9, 0);

    [Fact]
    public void Sans_historique_la_date_d_envoi_se_corrige_jusqu_a_l_instant_present()
    {
        var candidature = Nouvelle();

        Assert.Null(candidature.PremierEvenement);
        Assert.True(candidature.PeutEtreEnvoyeeLe(Maintenant, Maintenant, out _));
        candidature.CorrigerLEnvoi(Utc(2026, 11, 15, 8, 0), Maintenant);

        Assert.Equal(Utc(2026, 11, 15, 8, 0), candidature.EnvoyeeLe);
    }

    [Fact]
    public void Une_date_d_envoi_corrigee_dans_le_futur_est_refusee()
    {
        var candidature = Nouvelle();

        Assert.False(candidature.PeutEtreEnvoyeeLe(Maintenant.AddSeconds(1), Maintenant, out var refus));

        Assert.Equal("La date et l'heure d'envoi ne peuvent pas être dans le futur.", refus);
        Assert.Throws<ArgumentException>(() => candidature.CorrigerLEnvoi(Maintenant.AddSeconds(1), Maintenant));
        Assert.Equal(Envoi, candidature.EnvoyeeLe);
    }

    [Fact]
    public void La_date_d_envoi_ne_suit_pas_une_etape_deja_notee_mais_peut_tomber_a_son_instant_meme()
    {
        var candidature = Nouvelle();
        candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null);

        Assert.False(candidature.PeutEtreEnvoyeeLe(Premier.AddTicks(1), Maintenant, out var refus));
        Assert.Equal(
            "La date et l'heure d'envoi ne peuvent pas suivre le premier événement de l'historique : l'historique commence à l'envoi.",
            refus);
        Assert.Throws<ArgumentException>(() => candidature.CorrigerLEnvoi(Premier.AddTicks(1), Maintenant));
        Assert.Equal(Envoi, candidature.EnvoyeeLe);

        candidature.CorrigerLEnvoi(Premier, Maintenant);
        Assert.Equal(Premier, candidature.EnvoyeeLe);
    }

    [Fact]
    public void La_date_d_envoi_ne_suit_pas_un_changement_de_statut_en_vigueur()
    {
        var candidature = Nouvelle();
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Entretien, Premier, out _));

        Assert.Equal(Premier, candidature.PremierEvenement);
        Assert.False(candidature.PeutEtreEnvoyeeLe(Premier.AddMinutes(1), Maintenant, out var refus));
        Assert.Equal(DateDEnvoi.ApresLePremierEvenement, refus);
        Assert.True(candidature.PeutEtreEnvoyeeLe(Premier, Maintenant, out _));
    }

    [Fact]
    public void Un_changement_corrige_ne_compte_plus_mais_sa_correction_borne_l_envoi()
    {
        var candidature = Nouvelle();
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Entretien, Premier, out _));
        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Deuxieme, out _));

        // Le changement corrigé reste dans l'historique, mais n'est plus en vigueur : la correction, elle, l'est.
        Assert.Equal(Deuxieme, candidature.PremierEvenement);
        Assert.True(candidature.PeutEtreEnvoyeeLe(Premier.AddMinutes(1), Maintenant, out _));
        Assert.False(candidature.PeutEtreEnvoyeeLe(Deuxieme.AddMinutes(1), Maintenant, out _));
        Assert.True(candidature.PeutEtreEnvoyeeLe(Deuxieme, Maintenant, out _));
    }

    [Fact]
    public void Le_premier_evenement_est_le_plus_ancien_pas_le_premier_enregistre()
    {
        var candidature = Nouvelle();
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Entretien, Deuxieme, out _));
        // Notée après le changement, l'étape est datée avant : c'est elle qui ouvre l'historique.
        candidature.NoterUneEtape(TypeDEtape.Entretien, Premier, null);

        Assert.Equal(Premier, candidature.PremierEvenement);
        Assert.False(candidature.PeutEtreEnvoyeeLe(Premier.AddMinutes(1), Maintenant, out _));
    }

    [Fact]
    public void Plus_tot_la_date_d_envoi_se_corrige_toujours()
    {
        var candidature = Nouvelle();
        candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null);

        candidature.CorrigerLEnvoi(Utc(2026, 10, 1, 8, 0), Maintenant);

        Assert.Equal(Utc(2026, 10, 1, 8, 0), candidature.EnvoyeeLe);
    }

    [Fact]
    public void Modifier_ne_touche_ni_au_statut_ni_a_l_historique()
    {
        var candidature = Nouvelle();
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Entretien, Premier, out _));
        var etape = candidature.NoterUneEtape(TypeDEtape.Appel, Deuxieme, "Rappel.");
        var evenements = candidature.Evenements.ToArray();
        var dates = evenements.Select(evenement => evenement.SurvenuLe).ToArray();
        var aCorriger = candidature.ChangementACorriger;

        candidature.RattacherA(9);
        candidature.ChangerDePoste("  Comptable senior ");
        candidature.CorrigerLEnvoi(Utc(2026, 11, 1, 9, 0), Maintenant);

        Assert.Equal((9, "Comptable senior", Utc(2026, 11, 1, 9, 0)), (candidature.EntrepriseId, candidature.Poste, candidature.EnvoyeeLe));
        Assert.Equal(S.Entretien, candidature.Statut);
        Assert.Equal(evenements, candidature.Evenements);
        Assert.Equal(dates, candidature.Evenements.Select(evenement => evenement.SurvenuLe));
        Assert.Same(aCorriger, candidature.ChangementACorriger);
        Assert.Equal("Rappel.", etape.Note);
    }

    [Fact]
    public void Une_candidature_close_se_modifie_comme_une_autre()
    {
        var candidature = Nouvelle();
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Refusee, Premier, out _));

        candidature.ChangerDePoste("Aide-comptable");
        candidature.RattacherA(3);
        candidature.CorrigerLEnvoi(Utc(2026, 11, 1, 9, 0), Maintenant);

        Assert.Equal(S.Refusee, candidature.Statut);
        Assert.Empty(candidature.PassagesPermis);
        Assert.Equal("Aide-comptable", candidature.Poste);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Comptable\nsenior")]
    public void Un_poste_refuse_ne_change_pas_le_poste(string? poste)
    {
        var candidature = Nouvelle();

        Assert.Throws<ArgumentException>(() => candidature.ChangerDePoste(poste));

        Assert.Equal("Comptable", candidature.Poste);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Une_entreprise_qui_n_est_pas_enregistree_est_refusee(int entrepriseId)
    {
        var candidature = Nouvelle();

        Assert.Throws<ArgumentOutOfRangeException>(() => candidature.RattacherA(entrepriseId));

        Assert.Equal(1, candidature.EntrepriseId);
    }

    [Fact]
    public void Un_envoi_corrige_qui_n_est_pas_en_UTC_est_refuse()
    {
        var candidature = Nouvelle();
        var local = DateTime.SpecifyKind(Premier, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => candidature.PeutEtreEnvoyeeLe(local, Maintenant, out _));
        Assert.Throws<ArgumentException>(() => candidature.CorrigerLEnvoi(local, Maintenant));
    }

    [Theory]
    // L'heure que montre l'écran, à la minute : la même minute, secondes comprises ou non.
    [InlineData(2026, 11, 2, 10, 0, 0, true)]
    [InlineData(2026, 11, 2, 10, 0, 59, true)]
    [InlineData(2026, 11, 2, 10, 1, 0, false)]
    [InlineData(2026, 11, 2, 9, 59, 0, false)]
    // L'heure UTC n'est pas l'heure montrée.
    [InlineData(2026, 11, 2, 9, 0, 0, false)]
    public void Une_heure_saisie_est_l_envoi_enregistre_si_elle_tombe_sur_la_minute_montree(int annee, int mois, int jour, int heure, int minute, int seconde, bool attendu)
    {
        var saisie = new DateTime(annee, mois, jour, heure, minute, seconde, DateTimeKind.Unspecified);

        Assert.Equal(attendu, DateDEnvoi.EstLEnvoiEnregistre(saisie, Bruxelles, Envoi));
    }

    [Fact]
    public void Un_envoi_enregistre_avec_ses_secondes_se_reconnait_a_sa_minute()
    {
        var avecSecondes = Envoi.AddSeconds(45);

        Assert.True(DateDEnvoi.EstLEnvoiEnregistre(Saisie(2026, 11, 2, 10, 0), Bruxelles, avecSecondes));
    }

    [Fact]
    public void Dans_l_heure_qui_existe_deux_fois_chaque_passage_se_reconnait_a_l_heure_que_l_ecran_montre()
    {
        // Le 25/10/2026, 02:30 existe deux fois : 00:30 UTC à l'heure d'été, 01:30 UTC à l'heure d'hiver. Relue, la
        // saisie donnerait toujours 01:30 UTC ; reconnue, elle garde l'instant enregistré, quel qu'il soit.
        var saisie = Saisie(2026, 10, 25, 2, 30);

        Assert.True(DateDEnvoi.EstLEnvoiEnregistre(saisie, Bruxelles, Utc(2026, 10, 25, 0, 30)));
        Assert.True(DateDEnvoi.EstLEnvoiEnregistre(saisie, Bruxelles, Utc(2026, 10, 25, 1, 30)));
        Assert.False(DateDEnvoi.EstLEnvoiEnregistre(saisie, Bruxelles, Utc(2026, 10, 25, 2, 30)));
    }

    [Fact]
    public void Une_heure_saisie_avec_un_fuseau_ou_un_envoi_qui_n_est_pas_en_UTC_est_refuse()
    {
        Assert.Throws<ArgumentException>(() => DateDEnvoi.EstLEnvoiEnregistre(DateTime.SpecifyKind(Saisie(2026, 11, 2, 10, 0), DateTimeKind.Utc), Bruxelles, Envoi));
        Assert.Throws<ArgumentException>(() => DateDEnvoi.EstLEnvoiEnregistre(Saisie(2026, 11, 2, 10, 0), Bruxelles, DateTime.SpecifyKind(Envoi, DateTimeKind.Unspecified)));
    }

    private static Candidature Nouvelle() => Candidature.Enregistrer(1, "Comptable", Envoi, Envoi);
}
