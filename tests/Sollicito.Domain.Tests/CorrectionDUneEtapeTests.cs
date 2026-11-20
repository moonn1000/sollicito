using Sollicito.Domain.Candidatures;
using static Sollicito.Domain.Tests.Outils.Fuseaux;
using S = Sollicito.Domain.Candidatures.StatutDeCandidature;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Corriger une étape (#25) : la correction, une étape aux mêmes règles, s'ajoute à l'historique et désigne l'étape
/// corrigée, qui y reste, marquée comme corrigée. Elle se corrige à son tour : la dernière de la chaîne est la version en
/// vigueur. Le statut ne change pas, et une étape corrigée ne borne plus la date d'envoi.
/// </summary>
public sealed class CorrectionDUneEtapeTests
{
    private static readonly DateTime Envoi = Utc(2026, 11, 2, 9, 0);
    private static readonly DateTime Maintenant = Utc(2026, 11, 18, 13, 30);
    private static readonly DateTime Premier = Utc(2026, 11, 10, 9, 0);
    private static readonly DateTime Deuxieme = Utc(2026, 11, 12, 9, 0);
    private static readonly DateTime Troisieme = Utc(2026, 11, 14, 9, 0);

    [Fact]
    public void La_correction_s_ajoute_designe_l_etape_corrigee_et_ne_change_pas_le_statut()
    {
        var candidature = Nouvelle();
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Entretien, Maintenant, out _));
        var aCorriger = candidature.ChangementACorriger;
        var etape = candidature.NoterUneEtape(TypeDEtape.Appel, Premier, "Rappel.");

        var correction = candidature.CorrigerUneEtape(etape, TypeDEtape.Entretien, Deuxieme, "  Premier entretien.  ");

        Assert.Same(correction, candidature.Evenements[^1]);
        Assert.Equal((TypeDEtape.Entretien, Deuxieme, "Premier entretien."), (correction.Type, correction.SurvenuLe, correction.Note));
        Assert.Same(etape, correction.Corrige);
        Assert.True(correction.EstUneCorrection);
        // L'étape corrigée reste, telle quelle, marquée comme corrigée.
        Assert.Equal((TypeDEtape.Appel, Premier, "Rappel."), (etape.Type, etape.SurvenuLe, etape.Note));
        Assert.True(candidature.EstCorrigee(etape));
        Assert.False(candidature.EstCorrigee(correction));
        Assert.Equal(S.Entretien, candidature.Statut);
        Assert.Same(aCorriger, candidature.ChangementACorriger);
        Assert.Equal(3, candidature.Evenements.Count);
    }

    [Fact]
    public void Une_correction_se_corrige_a_son_tour_et_seule_la_derniere_est_en_vigueur()
    {
        var candidature = Nouvelle();
        var etape = candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null);
        SimulerLaBase(candidature);
        var premiere = candidature.CorrigerUneEtape(candidature.EtapeEnVigueur(etape.Id)!, TypeDEtape.Appel, Deuxieme, null);
        SimulerLaBase(candidature);

        var seconde = candidature.CorrigerUneEtape(candidature.EtapeEnVigueur(premiere.Id)!, TypeDEtape.Echange, Troisieme, "Courriel.");

        Assert.Same(premiere, seconde.Corrige);
        Assert.Null(candidature.EtapeEnVigueur(etape.Id));
        Assert.Null(candidature.EtapeEnVigueur(premiere.Id));
        Assert.True(candidature.EstEnVigueur(seconde));
        Assert.False(candidature.EstEnVigueur(etape));
        Assert.False(candidature.EstEnVigueur(premiere));
    }

    [Fact]
    public void Une_etape_deja_corrigee_ou_d_une_autre_candidature_ne_se_corrige_pas()
    {
        var candidature = Nouvelle();
        var etape = candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null);
        candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Deuxieme, null);
        var autre = Nouvelle().NoterUneEtape(TypeDEtape.Appel, Premier, null);

        Assert.Throws<InvalidOperationException>(() => candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Troisieme, null));
        Assert.Throws<InvalidOperationException>(() => candidature.CorrigerUneEtape(autre, TypeDEtape.Appel, Troisieme, null));
        Assert.Equal(2, candidature.Evenements.Count);
    }

    [Fact]
    public void La_correction_suit_les_regles_d_une_etape()
    {
        var candidature = Nouvelle();
        var etape = candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null);

        Assert.Throws<ArgumentException>(() => candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Envoi.AddTicks(-1), null));
        Assert.Throws<ArgumentOutOfRangeException>(() => candidature.CorrigerUneEtape(etape, (TypeDEtape)42, Deuxieme, null));
        Assert.Throws<ArgumentException>(() => candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Deuxieme, new string('n', Etape.LongueurMaximaleDeLaNote + 1)));
        Assert.Throws<ArgumentException>(() => candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Deuxieme, "note\u0007"));
        Assert.Single(candidature.Evenements);
        Assert.False(candidature.EstCorrigee(etape));
        // À l'instant même de l'envoi, elle est admise, comme une étape notée.
        Assert.Equal(Envoi, candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Envoi, null).SurvenuLe);
    }

    [Fact]
    public void Une_etape_corrigee_ne_borne_plus_la_date_d_envoi_sa_correction_la_borne()
    {
        var candidature = Nouvelle();
        var etape = candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null);
        Assert.False(candidature.PeutEtreEnvoyeeLe(Premier.AddMinutes(1), Maintenant, out _));

        candidature.CorrigerUneEtape(etape, TypeDEtape.Appel, Troisieme, null);

        Assert.Equal(Troisieme, candidature.PremierEvenement);
        Assert.True(candidature.PeutEtreEnvoyeeLe(Deuxieme, Maintenant, out _));
        Assert.False(candidature.PeutEtreEnvoyeeLe(Troisieme.AddMinutes(1), Maintenant, out _));
    }

    [Fact]
    public void Une_candidature_close_corrige_ses_etapes()
    {
        var candidature = Nouvelle();
        var etape = candidature.NoterUneEtape(TypeDEtape.Entretien, Premier, null);
        Assert.True(candidature.EssayerDeChangerDeStatut(S.Refusee, Maintenant, out _));

        candidature.CorrigerUneEtape(etape, TypeDEtape.Entretien, Deuxieme, null);

        Assert.Equal(S.Refusee, candidature.Statut);
        Assert.Empty(candidature.PassagesPermis);
    }

    private static Candidature Nouvelle() => Candidature.Enregistrer(1, "Comptable", Envoi, Envoi);

    // La base donne leur identifiant aux événements enregistrés : on le simule, dans l'ordre d'enregistrement.
    private static void SimulerLaBase(Candidature candidature)
    {
        var identifiant = typeof(EvenementDeCandidature).GetProperty(nameof(EvenementDeCandidature.Id))!;
        for (var rang = 0; rang < candidature.Evenements.Count; rang++)
        {
            identifiant.SetValue(candidature.Evenements[rang], rang + 1);
        }
    }
}
