using Sollicito.Application.IA;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le choix de l'adaptateur (#43, ADR-0016) : une IA mal réglée, une clé absente ou illisible ne cassent rien, et la
/// raison en est dite. Et la demande à l'IA, dont les bornes se vérifient.
/// </summary>
public sealed class ChoixDuFournisseurDIATests
{
    [Theory]
    [InlineData(FournisseurDIA.Aucun, true, EtatDeLaCle.SansObjet, RaisonDIndisponibilite.AucuneIA)]
    [InlineData(FournisseurDIA.Claude, false, EtatDeLaCle.Enregistree, RaisonDIndisponibilite.ReglageIllisible)]
    [InlineData(FournisseurDIA.Aucun, false, EtatDeLaCle.SansObjet, RaisonDIndisponibilite.ReglageIllisible)]
    [InlineData(FournisseurDIA.Claude, true, EtatDeLaCle.Aucune, RaisonDIndisponibilite.CleAbsente)]
    [InlineData(FournisseurDIA.OpenAI, true, EtatDeLaCle.Aucune, RaisonDIndisponibilite.CleAbsente)]
    [InlineData(FournisseurDIA.Claude, true, EtatDeLaCle.Illisible, RaisonDIndisponibilite.CleIllisible)]
    [InlineData(FournisseurDIA.OpenAI, true, EtatDeLaCle.Illisible, RaisonDIndisponibilite.CleIllisible)]
    public void Une_IA_qui_ne_peut_pas_servir_dit_pourquoi(FournisseurDIA fournisseur, bool lisible, EtatDeLaCle cle, RaisonDIndisponibilite attendue)
    {
        Assert.Equal(attendue, ChoixDuFournisseurDIA.Raison(fournisseur, lisible, cle));
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude, EtatDeLaCle.Enregistree)]
    [InlineData(FournisseurDIA.OpenAI, EtatDeLaCle.Enregistree)]
    [InlineData(FournisseurDIA.Ollama, EtatDeLaCle.SansObjet)]
    public void Un_fournisseur_regle_et_sa_cle_lisible_sont_prets(FournisseurDIA fournisseur, EtatDeLaCle cle)
    {
        Assert.Null(ChoixDuFournisseurDIA.Raison(fournisseur, reglageLisible: true, cle));
    }

    [Fact]
    public void Une_demande_garde_ses_bornes()
    {
        var duree = TimeSpan.FromSeconds(30);

        Assert.Throws<ArgumentException>(() => new DemandeALIA(" ", "données", 10, duree));
        Assert.Throws<ArgumentNullException>(() => new DemandeALIA("consignes", null!, 10, duree));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandeALIA(new string('c', DemandeALIA.TailleMaximaleDesConsignes + 1), "", 10, duree));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandeALIA("consignes", new string('d', DemandeALIA.TailleMaximaleDesDonnees + 1), 10, duree));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandeALIA("consignes", "", 0, duree));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandeALIA("consignes", "", DemandeALIA.TailleMaximaleDeLaReponse + 1, duree));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandeALIA("consignes", "", 10, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemandeALIA("consignes", "", 10, DemandeALIA.DureeMaximale + TimeSpan.FromSeconds(1)));

        var demande = new DemandeALIA("consignes", "", DemandeALIA.TailleMaximaleDeLaReponse, DemandeALIA.DureeMaximale);
        Assert.Equal("", demande.Donnees);
    }
}
