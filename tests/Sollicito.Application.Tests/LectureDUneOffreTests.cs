using Sollicito.Application.Offres;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le pas de lecture du cœur (#40, ADR-0015 ; #98, point 16) : il valide ce qu'un connecteur annonce. Un connecteur qui
/// n'annonce aucun champ de l'empreinte, un champ deux fois, ou un champ inconnu, est une erreur de programmation, pas un
/// refus : le pas lève, sans rien lire. Données fictives.
/// </summary>
public sealed class LectureDUneOffreTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 24);

    private static readonly ChampsRecus Champs = new("Texte fictif.", "Lumière Verte SA", "Comptable", "Liège", null, null);

    public static TheoryData<ChampDeLEmpreinte[]> ChampsFaux => new()
    {
        Array.Empty<ChampDeLEmpreinte>(),
        new[] { ChampDeLEmpreinte.Poste, ChampDeLEmpreinte.Poste },
        new[] { ChampDeLEmpreinte.Texte, (ChampDeLEmpreinte)99 },
    };

    [Theory]
    [MemberData(nameof(ChampsFaux))]
    public void Un_connecteur_sans_champ_de_l_empreinte_ou_avec_un_champ_repete_ou_inconnu_est_une_erreur_de_programmation(ChampDeLEmpreinte[] champs)
    {
        var offre = new OffreRecue(SourceDOffre.FichierCsv, champs, Champs);

        var erreur = Assert.Throws<ArgumentException>(() => LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out _, out _));

        Assert.Equal("champs", erreur.ParamName);
    }

    [Fact]
    public void Un_connecteur_qui_n_annonce_pas_ses_champs_est_une_erreur_de_programmation()
    {
        var offre = new OffreRecue(SourceDOffre.FichierCsv, null!, Champs);

        var erreur = Assert.Throws<ArgumentException>(() => LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out _, out _));

        Assert.Equal("champs", erreur.ParamName);
    }

    [Fact]
    public void Une_source_inconnue_ou_des_champs_absents_sont_des_erreurs_de_programmation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LectureDUneOffre.EssayerDeLire(new OffreRecue((SourceDOffre)99, ConnecteurDuTexteColle.ChampsDeLEmpreinte, Champs), Aujourdhui, out _, out _));
        Assert.Throws<ArgumentNullException>(
            () => LectureDUneOffre.EssayerDeLire(new OffreRecue(SourceDOffre.TexteColle, ConnecteurDuTexteColle.ChampsDeLEmpreinte, null!), Aujourdhui, out _, out _));
    }

    [Fact]
    public void Les_champs_annonces_par_chaque_connecteur_se_lisent()
    {
        Assert.True(LectureDUneOffre.EssayerDeLire(new OffreRecue(SourceDOffre.TexteColle, ConnecteurDuTexteColle.ChampsDeLEmpreinte, Champs), Aujourdhui, out _, out _));
        Assert.True(LectureDUneOffre.EssayerDeLire(new OffreRecue(SourceDOffre.FichierCsv, ConnecteurDuFichierCsv.ChampsDeLEmpreinte, Champs), Aujourdhui, out _, out _));
    }
}
