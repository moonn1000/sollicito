using Sollicito.Domain.Candidatures;
using static Sollicito.Domain.Tests.Outils.Fuseaux;
using S = Sollicito.Domain.Candidatures.StatutDeCandidature;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Les passages entre statuts (modèle de domaine, « Statuts d'une candidature ») : la table du modèle, recopiée ici
/// sans passer par le code, dit seule ce qui est permis. Chacun des 49 couples de statuts est essayé sur une candidature
/// amenée au statut de départ par des passages permis.
/// </summary>
public sealed class PassagesDeStatutTests
{
    private static readonly DateTime Envoi = Utc(2026, 11, 2, 9, 0);
    private static readonly DateTime Maintenant = Utc(2026, 11, 18, 13, 30);

    // La table « Passages permis » du modèle de domaine.
    private static readonly Dictionary<S, S[]> Table = new()
    {
        [S.Envoyee] = [S.Entretien, S.Refusee, S.Retiree, S.SansReponse],
        [S.Entretien] = [S.Proposition, S.Refusee, S.Retiree, S.SansReponse],
        [S.Proposition] = [S.Acceptee, S.Refusee, S.Retiree, S.SansReponse],
        [S.Acceptee] = [],
        [S.Refusee] = [],
        [S.Retiree] = [],
        [S.SansReponse] = [],
    };

    // Un chemin de passages permis qui mène, depuis Envoyée, à chaque statut.
    private static readonly Dictionary<S, S[]> Chemins = new()
    {
        [S.Envoyee] = [],
        [S.Entretien] = [S.Entretien],
        [S.Proposition] = [S.Entretien, S.Proposition],
        [S.Acceptee] = [S.Entretien, S.Proposition, S.Acceptee],
        [S.Refusee] = [S.Refusee],
        [S.Retiree] = [S.Entretien, S.Retiree],
        [S.SansReponse] = [S.Entretien, S.Proposition, S.SansReponse],
    };

    public static TheoryData<S, S> TousLesCouples()
    {
        var couples = new TheoryData<S, S>();
        foreach (var de in Enum.GetValues<S>())
        {
            foreach (var vers in Enum.GetValues<S>())
            {
                couples.Add(de, vers);
            }
        }

        return couples;
    }

    [Theory]
    [MemberData(nameof(TousLesCouples))]
    public void Un_passage_n_est_permis_que_s_il_figure_dans_la_table_du_modele(S de, S vers)
    {
        var candidature = AmeneeA(de);
        var evenementsAvant = candidature.Evenements.Count;
        var permis = Table[de].Contains(vers);

        var change = candidature.EssayerDeChangerDeStatut(vers, Maintenant, out var refus);

        Assert.Equal(permis, change);
        Assert.Equal(permis, PassagesDeStatut.EstPermis(de, vers));
        if (permis)
        {
            Assert.Null(refus);
            Assert.Equal(vers, candidature.Statut);
            var changement = Assert.IsType<ChangementDeStatut>(candidature.Evenements[^1]);
            Assert.Equal((de, vers, Maintenant, false), (changement.Ancien, changement.Nouveau, changement.SurvenuLe, changement.EstUneCorrection));
            Assert.Equal(evenementsAvant + 1, candidature.Evenements.Count);
        }
        else
        {
            // Refusé : un message, et rien ne change.
            Assert.False(string.IsNullOrWhiteSpace(refus));
            Assert.Equal(de, candidature.Statut);
            Assert.Equal(evenementsAvant, candidature.Evenements.Count);
        }
    }

    public static TheoryData<S> TousLesStatuts => [.. Enum.GetValues<S>()];

    [Theory]
    [MemberData(nameof(TousLesStatuts))]
    public void Les_passages_permis_sont_ceux_de_la_table_dans_son_ordre(S de)
    {
        Assert.Equal(Table[de], PassagesDeStatut.Depuis(de));
        Assert.Equal(Table[de], AmeneeA(de).PassagesPermis);
        Assert.Equal(Table[de].Length == 0, PassagesDeStatut.EstClos(de));
    }

    [Theory]
    [InlineData(S.Envoyee, S.Envoyee, "La candidature est déjà au statut Envoyée.")]
    [InlineData(S.SansReponse, S.SansReponse, "La candidature est déjà au statut Sans réponse.")]
    [InlineData(S.Envoyee, S.Proposition, "Une candidature ne passe pas du statut Envoyée au statut Proposition. Depuis Envoyée, elle peut passer à Entretien, Refusée, Retirée ou Sans réponse.")]
    [InlineData(S.Entretien, S.Envoyee, "Une candidature ne passe pas du statut Entretien au statut Envoyée. Depuis Entretien, elle peut passer à Proposition, Refusée, Retirée ou Sans réponse.")]
    [InlineData(S.Proposition, S.Entretien, "Une candidature ne passe pas du statut Proposition au statut Entretien. Depuis Proposition, elle peut passer à Acceptée, Refusée, Retirée ou Sans réponse.")]
    [InlineData(S.Refusee, S.Entretien, "La candidature est close, au statut Refusée : son statut ne change plus. Si ce statut est une erreur, corrigez le dernier changement.")]
    [InlineData(S.Acceptee, S.Retiree, "La candidature est close, au statut Acceptée : son statut ne change plus. Si ce statut est une erreur, corrigez le dernier changement.")]
    public void Un_passage_refuse_dit_pourquoi_et_ce_qui_reste_possible(S de, S vers, string message)
    {
        Assert.False(AmeneeA(de).EssayerDeChangerDeStatut(vers, Maintenant, out var refus));

        Assert.Equal(message, refus);
    }

    [Fact]
    public void Chaque_statut_a_son_libelle()
    {
        Assert.Equal(
            ["Envoyée", "Entretien", "Proposition", "Acceptée", "Refusée", "Retirée", "Sans réponse"],
            Enum.GetValues<S>().Select(PassagesDeStatut.Libelle));
    }

    [Fact]
    public void Un_statut_qui_n_existe_pas_est_une_erreur_de_programmation()
    {
        var candidature = AmeneeA(S.Envoyee);

        Assert.Throws<ArgumentOutOfRangeException>(() => candidature.EssayerDeChangerDeStatut((S)99, Maintenant, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => PassagesDeStatut.Depuis((S)(-1)));
        Assert.Empty(candidature.Evenements);
    }

    [Fact]
    public void Un_changement_se_date_en_UTC()
    {
        var candidature = AmeneeA(S.Envoyee);

        Assert.Throws<ArgumentException>(() =>
            candidature.EssayerDeChangerDeStatut(S.Entretien, DateTime.SpecifyKind(Maintenant, DateTimeKind.Unspecified), out _));
        Assert.Equal(S.Envoyee, candidature.Statut);
        Assert.Empty(candidature.Evenements);
    }

    private static Candidature AmeneeA(S statut)
    {
        var candidature = Candidature.Enregistrer(1, "Comptable", Envoi, Envoi);
        foreach (var etape in Chemins[statut])
        {
            Assert.True(candidature.EssayerDeChangerDeStatut(etape, Maintenant, out _));
        }

        Assert.Equal(statut, candidature.Statut);
        return candidature;
    }
}
