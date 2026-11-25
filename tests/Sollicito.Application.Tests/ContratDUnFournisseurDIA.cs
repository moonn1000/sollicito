using Sollicito.Application.IA;
using Sollicito.Tests.Commun;

namespace Sollicito.Application.Tests;

/// <summary>L'adaptateur « aucune IA » au banc : il répond toujours « indisponible ».</summary>
public sealed class AucuneIAAuBanc : ContratDUnFournisseurDIA
{
    protected override IFournisseurDIA Creer() => new AucuneIA(RaisonDIndisponibilite.AucuneIA);

    [Theory]
    [InlineData(RaisonDIndisponibilite.AucuneIA)]
    [InlineData(RaisonDIndisponibilite.ReglageIllisible)]
    [InlineData(RaisonDIndisponibilite.CleAbsente)]
    [InlineData(RaisonDIndisponibilite.CleIllisible)]
    [InlineData(RaisonDIndisponibilite.PasEncoreBranche)]
    public async Task Elle_rend_sa_raison_et_ne_sert_aucun_fournisseur(RaisonDIndisponibilite raison)
    {
        var aucune = new AucuneIA(raison);

        var reponse = await aucune.DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(raison, Assert.IsType<ReponseDeLIA.Indisponible>(reponse).Raison);
        Assert.Equal(FournisseurDIA.Aucun, aucune.Fournisseur);
    }
}

/// <summary>Le faux des tests au banc, dans ses trois sortes de réponse.</summary>
public sealed class FausseIAAuBanc : ContratDUnFournisseurDIA
{
    protected override IFournisseurDIA Creer() => new FausseIA("Une réponse fictive de l'IA.");

    [Fact]
    public async Task Une_reponse_trop_longue_n_est_pas_rendue()
    {
        var fausse = new FausseIA(new string('a', 500));

        var reponse = await fausse.DemanderAsync(Demande(caracteres: 100), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.ReponseTropLongue, Assert.IsType<ReponseDeLIA.Echec>(reponse).Sorte);
    }

    [Fact]
    public async Task Les_donnees_lui_parviennent_a_part_des_consignes()
    {
        var fausse = new FausseIA("ok");

        await fausse.DemanderAsync(Demande(DonneesPiegees), TestContext.Current.CancellationToken);

        var recue = Assert.Single(fausse.Demandes);
        Assert.Equal(DonneesPiegees, recue.Donnees);
        Assert.DoesNotContain("SOLLICITO-DONNEE-43", recue.Consignes, StringComparison.Ordinal);
    }
}

/// <summary>
/// Le faux fournisseur d'IA des tests (#43) : il note chaque demande et rend la réponse qu'on lui a donnée, ou un échec
/// si elle dépasse la borne de la demande. Il ne contacte rien. Les tests de #46 et #47 s'en serviront.
/// </summary>
public sealed class FausseIA(string reponse, FournisseurDIA fournisseur = FournisseurDIA.Claude) : IFournisseurDIA
{
    private readonly List<DemandeALIA> _demandes = [];

    /// <summary>Les demandes reçues, dans l'ordre.</summary>
    public IReadOnlyList<DemandeALIA> Demandes => _demandes;

    /// <inheritdoc />
    public FournisseurDIA Fournisseur => fournisseur;

    /// <inheritdoc />
    public Task<ReponseDeLIA> DemanderAsync(DemandeALIA demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        annulation.ThrowIfCancellationRequested();
        _demandes.Add(demande);
        ReponseDeLIA rendue = reponse.Length > demande.CaracteresDeLaReponse
            ? new ReponseDeLIA.Echec(SorteDEchec.ReponseTropLongue)
            : new ReponseDeLIA.Texte(reponse);
        return Task.FromResult(rendue);
    }
}
