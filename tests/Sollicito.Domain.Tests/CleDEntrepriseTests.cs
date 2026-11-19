using Sollicito.Domain.Entreprises;

namespace Sollicito.Domain.Tests;

/// <summary>
/// La clé d'une entreprise (ADR-0005 ; modèle de domaine, « Reconnaître une entreprise ») : les exemples du modèle,
/// sur des noms fictifs, et ses limites connues.
/// </summary>
public sealed class CleDEntrepriseTests
{
    [Theory]
    [InlineData("Lumière Verte SA", "lumiere verte")]
    [InlineData("LUMIÈRE VERTE S.A.", "lumiere verte")]
    [InlineData("Lumière Verte NV/SA", "lumiere verte")]
    [InlineData("L’Atelier d’Ondine", "l atelier d ondine")]
    [InlineData("Brasserie Lune Rousse S.R.L.", "brasserie lune rousse")]
    [InlineData("SRL Lune Rousse", "lune rousse")]
    [InlineData("ASBL Les Amis du Parc", "les amis du parc")]
    [InlineData("Les Amis du Parc ASBL", "les amis du parc")]
    [InlineData("Les Œuvres du Cœur ASBL", "les oeuvres du coeur")]
    [InlineData("AG Logistique SA", "ag logistique")]
    [InlineData("SC Durand", "sc durand")]
    [InlineData("NV Lumière Verte SA", "nv lumiere verte")]
    [InlineData("Lumière Verte Belgique", "lumiere verte belgique")]
    public void Les_exemples_du_modele_donnent_leur_cle(string nom, string cle)
    {
        Assert.Equal(cle, CleDEntreprise.De(nom));
    }

    [Theory]
    [InlineData("SA", "sa")]
    [InlineData("ASBL SA", "asbl")]
    [InlineData("SRL ASBL", "srl")]
    public void Le_dernier_mot_n_est_jamais_retire(string nom, string cle)
    {
        Assert.Equal(cle, CleDEntreprise.De(nom));
    }

    [Theory]
    [InlineData("S. A.", "s a")]
    [InlineData("Lumière.Verte", "lumiereverte")]
    [InlineData("Mon CV", "mon")]
    public void Les_limites_connues_du_modele_restent_telles_qu_il_les_decrit(string nom, string cle)
    {
        Assert.Equal(cle, CleDEntreprise.De(nom));
    }

    [Theory]
    [InlineData("  Lumière   Verte  ", "lumiere verte")]
    [InlineData("Lumière Verte", "lumiere verte")]
    [InlineData("Ｌｕｍｉｅｒｅ Ｖｅｒｔｅ", "lumiere verte")]
    [InlineData("Æther & Fils SA", "aether fils")]
    [InlineData("Lumière-Verte GmbH", "lumiere verte")]
    public void Les_blancs_la_ponctuation_et_les_formes_de_compatibilite_ne_changent_pas_la_cle(string nom, string cle)
    {
        Assert.Equal(cle, CleDEntreprise.De(nom));
    }

    [Fact]
    public void La_cle_ne_depend_pas_de_la_langue_du_poste()
    {
        var avant = Thread.CurrentThread.CurrentCulture;
        try
        {
            // En turc, la minuscule de « I » est « ı », sans point.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            Assert.Equal("lumiere industrie", CleDEntreprise.De("LUMIERE INDUSTRIE"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = avant;
        }
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("...")]
    [InlineData(" - / ")]
    public void Un_nom_sans_lettre_ni_chiffre_donne_une_cle_vide(string nom)
    {
        Assert.Equal("", CleDEntreprise.De(nom));
    }
}
