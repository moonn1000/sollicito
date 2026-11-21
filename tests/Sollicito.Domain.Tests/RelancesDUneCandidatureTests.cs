using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Relances;
using Sollicito.Domain.Tests.Outils;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Les relances d'une candidature (modèle de domaine, « Relances » ; #14) : une seule à faire à la fois, sur une
/// candidature en cours ; une échéance qui part de la date locale de l'envoi, calculée sur la date ; faite ou
/// abandonnée pour de bon, avec sa clôture dans l'historique ; abandonnée quand la candidature se clôt, et sans
/// destinataire quand elle change d'entreprise.
/// </summary>
public sealed class RelancesDUneCandidatureTests
{
    // Le vendredi 27 mars 2026 à 23:30, heure de Bruxelles : l'exemple du modèle de domaine, « Dates et heures ».
    private static readonly DateTime Envoi = Fuseaux.Utc(2026, 3, 27, 22, 30);

    private static readonly DateTime Maintenant = Fuseaux.Utc(2026, 4, 2, 10, 0);

    [Fact]
    public void L_echeance_part_de_la_date_locale_de_l_envoi_et_se_calcule_sur_la_date()
    {
        // Le passage à l'heure d'été tombe le 29 mars, entre l'envoi et l'échéance : en heures, l'échéance tomberait le
        // samedi 4 avril.
        Assert.Equal(new DateOnly(2026, 4, 3), DelaiDeRelance.Echeance(Envoi, Fuseaux.Bruxelles, 7));
        Assert.Equal(new DateOnly(2026, 3, 27), DelaiDeRelance.JourLocal(Envoi, Fuseaux.Bruxelles));
    }

    [Fact]
    public void Le_jour_de_l_envoi_est_celui_du_fuseau_du_poste_pas_celui_d_UTC()
    {
        // Le 18 novembre 2026 à 00:30, heure de Bruxelles, c'est encore le 17 en UTC.
        var peuApresMinuit = Fuseaux.Utc(2026, 11, 17, 23, 30);

        Assert.Equal(new DateOnly(2026, 11, 18), DelaiDeRelance.JourLocal(peuApresMinuit, Fuseaux.Bruxelles));
        Assert.Equal(new DateOnly(2026, 11, 25), DelaiDeRelance.Echeance(peuApresMinuit, Fuseaux.Bruxelles, 7));
        Assert.Throws<ArgumentException>(() => DelaiDeRelance.JourLocal(DateTime.SpecifyKind(peuApresMinuit, DateTimeKind.Unspecified), Fuseaux.Bruxelles));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(365, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(366, false)]
    public void Le_delai_compte_de_1_a_365_jours(int jours, bool admis)
    {
        Assert.Equal(admis, DelaiDeRelance.EstAdmis(jours));
        if (!admis)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DelaiDeRelance.Echeance(Envoi, Fuseaux.Bruxelles, jours));
        }
    }

    [Fact]
    public void Une_candidature_en_cours_planifie_une_relance_a_faire()
    {
        var candidature = Envoyee();

        Assert.True(candidature.EssayerDePlanifierUneRelance(7, destinataireId: 4, Fuseaux.Bruxelles, out var refus));

        Assert.Null(refus);
        var relance = Assert.Single(candidature.Relances);
        Assert.Same(relance, candidature.RelanceAFaire);
        Assert.Equal(EtatDeRelance.AFaire, relance.Etat);
        Assert.Equal(new DateOnly(2026, 4, 3), relance.Echeance);
        Assert.Equal(4, relance.DestinataireId);
        Assert.Empty(candidature.Evenements);
    }

    [Fact]
    public void Une_echeance_deja_passee_est_admise()
    {
        var candidature = Envoyee();

        Assert.True(candidature.EssayerDePlanifierUneRelance(1, destinataireId: null, Fuseaux.Bruxelles, out _));

        Assert.True(candidature.RelanceAFaire!.EstEchueLe(new DateOnly(2026, 4, 2)));
        Assert.Null(candidature.RelanceAFaire.DestinataireId);
    }

    [Fact]
    public void Une_seconde_relance_a_faire_est_refusee()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _);

        Assert.False(candidature.EssayerDePlanifierUneRelance(14, null, Fuseaux.Bruxelles, out var refus));

        Assert.Equal(Candidature.RelanceDejaAFaire, refus);
        Assert.Single(candidature.Relances);
    }

    [Theory]
    [InlineData(StatutDeCandidature.Refusee)]
    [InlineData(StatutDeCandidature.Retiree)]
    [InlineData(StatutDeCandidature.SansReponse)]
    public void Une_candidature_close_ne_recoit_plus_de_relance(StatutDeCandidature clos)
    {
        var candidature = Envoyee();
        candidature.EssayerDeChangerDeStatut(clos, Maintenant, out _);

        Assert.False(candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out var refus));

        Assert.Equal(Candidature.RelanceSurCandidatureClose, refus);
        Assert.Empty(candidature.Relances);
    }

    [Fact]
    public void Une_candidature_en_entretien_ou_en_proposition_recoit_une_relance()
    {
        var candidature = Envoyee();
        candidature.EssayerDeChangerDeStatut(StatutDeCandidature.Entretien, Maintenant, out _);
        Assert.True(candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _));

        candidature.EssayerDeChangerDeStatut(StatutDeCandidature.Proposition, Maintenant, out _);
        Assert.NotNull(candidature.RelanceAFaire);
    }

    [Fact]
    public void La_relance_a_faire_change_de_delai_et_de_destinataire()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, 4, Fuseaux.Bruxelles, out _);

        Assert.True(candidature.EssayerDeChangerLaRelance(relanceId: 0, delai: 10, destinataireId: 5, Fuseaux.Bruxelles, out var refus));

        Assert.Null(refus);
        Assert.Equal(new DateOnly(2026, 4, 6), candidature.RelanceAFaire!.Echeance);
        Assert.Equal(5, candidature.RelanceAFaire.DestinataireId);
    }

    [Fact]
    public void Seule_la_relance_a_faire_se_change()
    {
        var candidature = Envoyee();
        Assert.False(candidature.EssayerDeChangerLaRelance(0, 7, null, Fuseaux.Bruxelles, out var sansRelance));
        Assert.Equal(Candidature.RelancePasAFaire, sansRelance);

        candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _);
        Assert.False(candidature.EssayerDeChangerLaRelance(relanceId: 99, 10, null, Fuseaux.Bruxelles, out var autre));
        Assert.Equal(Candidature.RelancePasAFaire, autre);
        Assert.Equal(new DateOnly(2026, 4, 3), candidature.RelanceAFaire!.Echeance);

        candidature.EssayerDeCloreLaRelance(0, EtatDeRelance.Faite, Maintenant, out _);
        Assert.False(candidature.EssayerDeChangerLaRelance(0, 10, null, Fuseaux.Bruxelles, out var close));
        Assert.Equal(Candidature.RelancePasAFaire, close);
    }

    [Theory]
    [InlineData(EtatDeRelance.Faite)]
    [InlineData(EtatDeRelance.Abandonnee)]
    public void Une_relance_close_l_est_pour_de_bon_et_entre_dans_l_historique(EtatDeRelance issue)
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, 4, Fuseaux.Bruxelles, out _);
        var relance = candidature.RelanceAFaire!;

        Assert.True(candidature.EssayerDeCloreLaRelance(0, issue, Maintenant, out var refus));

        Assert.Null(refus);
        Assert.Equal(issue, relance.Etat);
        Assert.Null(candidature.RelanceAFaire);
        Assert.Equal(4, relance.DestinataireId);
        var cloture = Assert.IsType<ClotureDeRelance>(Assert.Single(candidature.Evenements));
        Assert.Same(relance, cloture.Relance);
        Assert.Equal(Maintenant, cloture.SurvenuLe);

        Assert.False(candidature.EssayerDeCloreLaRelance(0, EtatDeRelance.Faite, Maintenant, out var deuxFois));
        Assert.Equal(Candidature.RelancePasAFaire, deuxFois);
        Assert.Single(candidature.Evenements);
    }

    [Fact]
    public void Une_relance_close_laisse_planifier_la_suivante()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _);
        candidature.EssayerDeCloreLaRelance(0, EtatDeRelance.Faite, Maintenant, out _);

        // La relance suivante part aussi de la date d'envoi.
        Assert.True(candidature.EssayerDePlanifierUneRelance(14, null, Fuseaux.Bruxelles, out _));

        Assert.Equal(2, candidature.Relances.Count);
        Assert.Equal(new DateOnly(2026, 4, 10), candidature.RelanceAFaire!.Echeance);
    }

    [Fact]
    public void Clore_la_candidature_abandonne_sa_relance_a_faire_et_la_correction_ne_la_rouvre_pas()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, 4, Fuseaux.Bruxelles, out _);
        var relance = candidature.RelanceAFaire!;

        Assert.True(candidature.EssayerDeChangerDeStatut(StatutDeCandidature.SansReponse, Maintenant, out _));

        Assert.Equal(EtatDeRelance.Abandonnee, relance.Etat);
        Assert.Collection(
            candidature.Historique,
            evenement => Assert.IsType<ChangementDeStatut>(evenement),
            evenement => Assert.Same(relance, Assert.IsType<ClotureDeRelance>(evenement).Relance));

        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Maintenant.AddMinutes(1), out _));
        Assert.Equal(StatutDeCandidature.Envoyee, candidature.Statut);
        Assert.Equal(EtatDeRelance.Abandonnee, relance.Etat);
        Assert.Null(candidature.RelanceAFaire);
    }

    [Fact]
    public void Passer_a_un_statut_en_cours_garde_la_relance_a_faire()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _);

        candidature.EssayerDeChangerDeStatut(StatutDeCandidature.Entretien, Maintenant, out _);

        Assert.NotNull(candidature.RelanceAFaire);
        Assert.IsType<ChangementDeStatut>(Assert.Single(candidature.Evenements));
    }

    [Fact]
    public void Une_autre_entreprise_retire_le_destinataire_de_la_relance_a_faire_mais_pas_des_closes()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, 4, Fuseaux.Bruxelles, out _);
        var close = candidature.RelanceAFaire!;
        candidature.EssayerDeCloreLaRelance(0, EtatDeRelance.Faite, Maintenant, out _);
        candidature.EssayerDePlanifierUneRelance(14, 4, Fuseaux.Bruxelles, out _);

        candidature.RattacherA(1);
        Assert.Equal(4, candidature.RelanceAFaire!.DestinataireId);

        candidature.RattacherA(2);
        Assert.Null(candidature.RelanceAFaire.DestinataireId);
        Assert.Equal(4, close.DestinataireId);
    }

    [Fact]
    public void Corriger_la_date_d_envoi_ne_deplace_pas_l_echeance()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _);

        candidature.CorrigerLEnvoi(Fuseaux.Utc(2026, 3, 20, 9, 0), Maintenant);

        Assert.Equal(new DateOnly(2026, 4, 3), candidature.RelanceAFaire!.Echeance);
    }

    [Fact]
    public void Une_cloture_de_relance_borne_la_date_d_envoi_corrigee()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(7, null, Fuseaux.Bruxelles, out _);
        candidature.EssayerDeCloreLaRelance(0, EtatDeRelance.Faite, Maintenant, out _);

        Assert.Equal(Maintenant, candidature.PremierEvenement);
        Assert.False(candidature.PeutEtreEnvoyeeLe(Maintenant.AddMinutes(1), Maintenant.AddHours(1), out var refus));
        Assert.Equal(DateDEnvoi.ApresLePremierEvenement, refus);
        Assert.True(candidature.PeutEtreEnvoyeeLe(Maintenant, Maintenant.AddHours(1), out _));
    }

    [Theory]
    [InlineData(2026, 4, 2, true, false)]
    [InlineData(2026, 4, 3, false, true)]
    [InlineData(2026, 4, 9, false, true)]
    [InlineData(2026, 4, 10, false, false)]
    [InlineData(2026, 3, 1, true, false)]
    public void Une_relance_a_faire_est_echue_ou_de_la_semaine_selon_son_echeance(int annee, int mois, int jour, bool echue, bool deLaSemaine)
    {
        // Aujourd'hui, le 2 avril 2026.
        var aujourdhui = new DateOnly(2026, 4, 2);
        var echeance = new DateOnly(annee, mois, jour);

        Assert.Equal(echue, Relance.EstUneEcheanceEchue(echeance, aujourdhui));
        Assert.Equal(deLaSemaine, Relance.EstUneEcheanceDeLaSemaine(echeance, aujourdhui));
        Assert.Equal(new DateOnly(2026, 4, 9), Relance.FinDeLaSemaine(aujourdhui));
    }

    [Fact]
    public void Une_relance_close_n_est_ni_echue_ni_de_la_semaine()
    {
        var candidature = Envoyee();
        candidature.EssayerDePlanifierUneRelance(1, null, Fuseaux.Bruxelles, out _);
        var relance = candidature.RelanceAFaire!;
        Assert.True(relance.EstEchueLe(new DateOnly(2026, 4, 2)));

        candidature.EssayerDeCloreLaRelance(0, EtatDeRelance.Abandonnee, Maintenant, out _);

        Assert.False(relance.EstEchueLe(new DateOnly(2026, 4, 2)));
        Assert.False(relance.EstDeLaSemaineDu(new DateOnly(2026, 3, 27)));
    }

    private static Candidature Envoyee() => Candidature.Enregistrer(1, "Comptable", Envoi, Maintenant);
}
