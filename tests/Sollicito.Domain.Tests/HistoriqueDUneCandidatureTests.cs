using Sollicito.Domain.Candidatures;
using static Sollicito.Domain.Tests.Outils.Fuseaux;
using S = Sollicito.Domain.Candidatures.StatutDeCandidature;

namespace Sollicito.Domain.Tests;

/// <summary>
/// L'historique d'une candidature (modèle de domaine, « Événement de candidature » et « Corriger le dernier
/// changement ») : il se fait par ajouts seulement ; une correction ramène au statut d'avant sans rien effacer ; une
/// étape ne change pas le statut ; l'historique se lit dans le temps, à égalité dans l'ordre d'enregistrement.
/// </summary>
public sealed class HistoriqueDUneCandidatureTests
{
    private static readonly DateTime Envoi = Utc(2026, 11, 2, 9, 0);
    private static readonly DateTime Premier = Utc(2026, 11, 10, 8, 0);
    private static readonly DateTime Deuxieme = Utc(2026, 11, 12, 8, 0);
    private static readonly DateTime Troisieme = Utc(2026, 11, 14, 8, 0);
    private static readonly DateTime Quatrieme = Utc(2026, 11, 16, 8, 0);

    [Fact]
    public void Une_candidature_neuve_n_a_ni_historique_ni_changement_a_corriger()
    {
        var candidature = Nouvelle();

        Assert.Empty(candidature.Evenements);
        Assert.Null(candidature.ChangementACorriger);
        Assert.False(candidature.EssayerDeCorrigerLeDernierChangement(Premier, out var refus));
        Assert.Equal("Aucun changement de statut n'est à corriger : la candidature est à son statut de départ.", refus);
        Assert.Equal(S.Envoyee, candidature.Statut);
        Assert.Empty(candidature.Evenements);
    }

    [Fact]
    public void Corriger_le_dernier_changement_ramene_au_statut_d_avant_sans_rien_effacer()
    {
        var candidature = Nouvelle();
        Passer(candidature, S.Entretien, Premier);
        Passer(candidature, S.Refusee, Deuxieme);
        var refus = (ChangementDeStatut)candidature.Evenements[1];

        Assert.Same(refus, candidature.ChangementACorriger);
        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Troisieme, out _));

        Assert.Equal(S.Entretien, candidature.Statut);
        Assert.Equal(3, candidature.Evenements.Count);
        var correction = Assert.IsType<ChangementDeStatut>(candidature.Evenements[2]);
        Assert.Equal((S.Refusee, S.Entretien, Troisieme), (correction.Ancien, correction.Nouveau, correction.SurvenuLe));
        Assert.Same(refus, correction.Corrige);
        Assert.True(correction.EstUneCorrection);
        // Le changement corrigé reste, tel quel, marqué comme corrigé.
        Assert.Equal((S.Entretien, S.Refusee, Deuxieme), (refus.Ancien, refus.Nouveau, refus.SurvenuLe));
        Assert.True(candidature.EstCorrige(refus));
        Assert.False(candidature.EstCorrige(correction));
        Assert.False(candidature.EstCorrige((ChangementDeStatut)candidature.Evenements[0]));
        // Les passages repartent du statut retrouvé.
        Assert.Equal([S.Proposition, S.Refusee, S.Retiree, S.SansReponse], candidature.PassagesPermis);
    }

    [Fact]
    public void Repetee_la_correction_remonte_jusqu_a_Envoyee_puis_n_a_plus_rien_a_corriger()
    {
        var candidature = Nouvelle();
        Passer(candidature, S.Entretien, Premier);
        Passer(candidature, S.Proposition, Deuxieme);
        var entretien = (ChangementDeStatut)candidature.Evenements[0];

        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Troisieme, out _));
        Assert.Equal(S.Entretien, candidature.Statut);
        Assert.Same(entretien, candidature.ChangementACorriger);
        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Quatrieme, out _));
        Assert.Equal(S.Envoyee, candidature.Statut);

        Assert.Null(candidature.ChangementACorriger);
        Assert.False(candidature.EssayerDeCorrigerLeDernierChangement(Quatrieme, out var refus));
        Assert.NotNull(refus);
        Assert.Equal(4, candidature.Evenements.Count);
        Assert.Same(entretien, ((ChangementDeStatut)candidature.Evenements[3]).Corrige);
    }

    [Fact]
    public void Une_correction_ne_se_corrige_pas_mais_un_passage_refait_se_corrige()
    {
        var candidature = Nouvelle();
        Passer(candidature, S.Entretien, Premier);
        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Deuxieme, out _));

        // Rien à corriger : le passage est corrigé, et sa correction ne se corrige pas.
        Assert.Null(candidature.ChangementACorriger);
        Assert.False(candidature.EssayerDeCorrigerLeDernierChangement(Troisieme, out _));

        // Pour revenir au statut corrigé, on refait le passage, qui se corrige à son tour.
        Passer(candidature, S.Entretien, Troisieme);
        var refait = (ChangementDeStatut)candidature.Evenements[2];
        Assert.Same(refait, candidature.ChangementACorriger);
        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Quatrieme, out _));
        Assert.Equal(S.Envoyee, candidature.Statut);
        Assert.True(candidature.EstCorrige(refait));
    }

    [Theory]
    [InlineData(S.Acceptee)]
    [InlineData(S.Refusee)]
    [InlineData(S.Retiree)]
    [InlineData(S.SansReponse)]
    public void La_correction_reprend_une_candidature_close(S cloture)
    {
        var candidature = Nouvelle();
        Passer(candidature, S.Entretien, Premier);
        if (cloture == S.Acceptee)
        {
            Passer(candidature, S.Proposition, Premier);
        }

        var avant = candidature.Statut;
        Passer(candidature, cloture, Deuxieme);
        Assert.Empty(candidature.PassagesPermis);

        Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(Troisieme, out _));

        Assert.Equal(avant, candidature.Statut);
        Assert.NotEmpty(candidature.PassagesPermis);
    }

    [Fact]
    public void Apres_toute_suite_de_passages_et_de_corrections_le_statut_est_le_nouveau_statut_du_dernier_changement()
    {
        var candidature = Nouvelle();
        var instant = Premier;
        // Passages (un statut) et corrections (rien), dans un ordre qui mêle les deux.
        S?[] suite = [S.Entretien, null, S.Entretien, S.Proposition, null, S.SansReponse, null, null, S.Retiree, null, S.Refusee];

        foreach (var action in suite)
        {
            instant = instant.AddHours(1);
            if (action is { } statut)
            {
                Passer(candidature, statut, instant);
            }
            else
            {
                Assert.True(candidature.EssayerDeCorrigerLeDernierChangement(instant, out _));
            }

            var dernier = candidature.Evenements.OfType<ChangementDeStatut>().LastOrDefault();
            Assert.Equal(dernier?.Nouveau ?? S.Envoyee, candidature.Statut);
        }

        Assert.Equal(S.Refusee, candidature.Statut);
        Assert.Equal(suite.Length, candidature.Evenements.Count);
    }

    [Fact]
    public void Une_etape_s_ajoute_a_l_historique_sans_changer_le_statut()
    {
        var candidature = Nouvelle();
        Passer(candidature, S.Entretien, Deuxieme);
        var aCorriger = candidature.ChangementACorriger;

        var etape = candidature.NoterUneEtape(TypeDEtape.Entretien, Premier, "  Premier entretien\r\navec la responsable.  ");

        Assert.Equal(S.Entretien, candidature.Statut);
        Assert.Same(etape, candidature.Evenements[^1]);
        Assert.Equal((TypeDEtape.Entretien, Premier, "Premier entretien\navec la responsable."), (etape.Type, etape.SurvenuLe, etape.Note));
        // Une étape n'est pas un changement : la correction vise toujours le même.
        Assert.Same(aCorriger, candidature.ChangementACorriger);
        Assert.Equal([S.Proposition, S.Refusee, S.Retiree, S.SansReponse], candidature.PassagesPermis);
    }

    [Fact]
    public void Une_etape_sans_note_n_en_garde_aucune()
    {
        var candidature = Nouvelle();

        Assert.Null(candidature.NoterUneEtape(TypeDEtape.Appel, Premier, null).Note);
        Assert.Null(candidature.NoterUneEtape(TypeDEtape.Appel, Premier, " \n\t ").Note);
    }

    [Fact]
    public void L_historique_se_lit_dans_le_temps_et_a_egalite_dans_l_ordre_d_enregistrement()
    {
        var candidature = Nouvelle();
        Passer(candidature, S.Entretien, Deuxieme);
        var avant = candidature.NoterUneEtape(TypeDEtape.Appel, Premier, "avant le passage");
        var aEgaliteA = candidature.NoterUneEtape(TypeDEtape.Echange, Deuxieme, "même instant que le passage");
        var aVenir = candidature.NoterUneEtape(TypeDEtape.Entretien, Quatrieme, "à venir");
        var aEgaliteB = candidature.NoterUneEtape(TypeDEtape.Autre, Deuxieme, "même instant, enregistrée après");
        var passage = candidature.Evenements[0];

        Assert.Equal([avant, passage, aEgaliteA, aEgaliteB, aVenir], candidature.Historique);
        // Les événements eux-mêmes restent dans l'ordre de leur enregistrement.
        Assert.Equal([passage, avant, aEgaliteA, aVenir, aEgaliteB], candidature.Evenements);
    }

    [Fact]
    public void Une_etape_ne_precede_pas_l_envoi()
    {
        var candidature = Nouvelle();

        Assert.Throws<ArgumentException>(() => candidature.NoterUneEtape(TypeDEtape.Appel, Envoi.AddTicks(-1), null));
        Assert.Equal(Envoi, candidature.NoterUneEtape(TypeDEtape.Appel, Envoi, null).SurvenuLe);
        Assert.Single(candidature.Evenements);
    }

    [Fact]
    public void Une_etape_d_un_type_inconnu_ou_d_une_note_refusee_n_est_pas_notee()
    {
        var candidature = Nouvelle();

        Assert.Throws<ArgumentOutOfRangeException>(() => candidature.NoterUneEtape((TypeDEtape)42, Premier, null));
        Assert.Throws<ArgumentException>(() => candidature.NoterUneEtape(TypeDEtape.Appel, Premier, new string('n', Etape.LongueurMaximaleDeLaNote + 1)));
        Assert.Throws<ArgumentException>(() => candidature.NoterUneEtape(TypeDEtape.Appel, Premier, "note\u0007"));
        Assert.Throws<ArgumentException>(() => candidature.NoterUneEtape(TypeDEtape.Appel, DateTime.SpecifyKind(Premier, DateTimeKind.Local), null));
        Assert.Empty(candidature.Evenements);
    }

    private static Candidature Nouvelle() => Candidature.Enregistrer(1, "Comptable", Envoi, Envoi);

    private static void Passer(Candidature candidature, S statut, DateTime instant) =>
        Assert.True(candidature.EssayerDeChangerDeStatut(statut, instant, out var refus), refus);
}
