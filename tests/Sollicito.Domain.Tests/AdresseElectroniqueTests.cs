using Sollicito.Domain.Contacts;

namespace Sollicito.Domain.Tests;

/// <summary>
/// L'adresse électronique d'un contact (modèle de domaine, « Contact » ; #14) : une seule adresse, de la forme
/// nom@domaine, que le lien mailto de la fenêtre ne peut que viser. Toutes les adresses sont fictives.
/// </summary>
public sealed class AdresseElectroniqueTests
{
    [Theory]
    [InlineData("jeanne.martin@lumiere-verte.be")]
    [InlineData("j@x.be")]
    [InlineData("Jeanne.Martin@Lumiere-Verte.BE")]
    [InlineData("rh+candidatures@exemple.invalid")]
    [InlineData("o'brien@exemple.invalid")]
    [InlineData("a_b-c@sous.domaine.exemple.invalid")]
    [InlineData("!#$&*/=^`{|}~@exemple.invalid")]
    [InlineData("contact@xn--socit-esab.be")]
    [InlineData("contact@123.be")]
    public void Une_adresse_de_la_forme_nom_arobase_domaine_est_admise(string adresse)
    {
        Assert.True(AdresseElectronique.EssayerDeLire(adresse, out var lue, out var erreur));
        Assert.Equal(adresse, lue);
        Assert.Null(erreur);
        Assert.True(AdresseElectronique.EstValide(adresse));
    }

    [Fact]
    public void Les_blancs_du_debut_et_de_la_fin_sont_retires()
    {
        Assert.True(AdresseElectronique.EssayerDeLire("  jeanne@exemple.invalid \t", out var lue, out _));
        Assert.Equal("jeanne@exemple.invalid", lue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Une_adresse_absente_est_refusee(string? saisie)
    {
        Assert.False(AdresseElectronique.EssayerDeLire(saisie, out _, out var erreur));
        Assert.Equal(AdresseElectronique.Manquante, erreur);
    }

    [Theory]
    [InlineData("jeanne")]
    [InlineData("@exemple.invalid")]
    [InlineData("jeanne@")]
    [InlineData("jeanne@exemple")]
    [InlineData("jeanne@@exemple.invalid")]
    [InlineData("jeanne@exemple@invalid.be")]
    [InlineData("jeanne martin@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid,paul@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid;paul@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid?cc=paul@exemple.invalid")]
    [InlineData("jeanne?@exemple.invalid")]
    [InlineData("jeanne%40exemple.invalid@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid&bcc=paul@exemple.invalid")]
    [InlineData("jeanne@exemple.invalid\nbcc: paul@exemple.invalid")]
    [InlineData("jeanne@exem\u0000ple.invalid")]
    [InlineData("jéanne@exemple.invalid")]
    [InlineData("jeanne@société.be")]
    [InlineData("\"jeanne martin\"@exemple.invalid")]
    [InlineData("Jeanne <jeanne@exemple.invalid>")]
    [InlineData(".jeanne@exemple.invalid")]
    [InlineData("jeanne.@exemple.invalid")]
    [InlineData("jeanne..martin@exemple.invalid")]
    [InlineData("jeanne@.exemple.invalid")]
    [InlineData("jeanne@exemple..invalid")]
    [InlineData("jeanne@exemple.invalid.")]
    [InlineData("jeanne@-exemple.invalid")]
    [InlineData("jeanne@exemple-.invalid")]
    [InlineData("jeanne@exemple_test.invalid")]
    [InlineData("jeanne@[127.0.0.1]")]
    [InlineData("mailto:jeanne@exemple.invalid")]
    public void Une_adresse_d_une_autre_forme_est_refusee_sans_etre_reprise(string adresse)
    {
        Assert.False(AdresseElectronique.EssayerDeLire(adresse, out var lue, out var erreur));
        Assert.Null(lue);
        Assert.Equal(AdresseElectronique.Illisible, erreur);
        Assert.False(AdresseElectronique.EstValide(adresse));
    }

    [Fact]
    public void La_partie_locale_compte_64_caracteres_au_plus()
    {
        Assert.True(AdresseElectronique.EssayerDeLire(new string('a', 64) + "@exemple.invalid", out _, out _));
        Assert.False(AdresseElectronique.EssayerDeLire(new string('a', 65) + "@exemple.invalid", out _, out var erreur));
        Assert.Equal(AdresseElectronique.Illisible, erreur);
    }

    [Fact]
    public void Une_partie_du_domaine_compte_63_caracteres_au_plus()
    {
        Assert.True(AdresseElectronique.EssayerDeLire("a@" + new string('b', 63) + ".be", out _, out _));
        Assert.False(AdresseElectronique.EssayerDeLire("a@" + new string('b', 64) + ".be", out _, out _));
    }

    [Fact]
    public void L_adresse_compte_254_caracteres_au_plus()
    {
        var domaine = string.Join('.', Enumerable.Repeat(new string('d', 60), 4));
        var admise = new string('a', 254 - domaine.Length - 1) + "@" + domaine;
        Assert.Equal(254, admise.Length);
        Assert.True(AdresseElectronique.EssayerDeLire(admise, out _, out _));

        Assert.False(AdresseElectronique.EssayerDeLire("a" + admise, out _, out var erreur));
        Assert.Equal(AdresseElectronique.TropLongue, erreur);
        Assert.False(AdresseElectronique.EstValide("a" + admise));
    }

    [Fact]
    public void EstValide_ne_retire_pas_les_blancs()
    {
        Assert.False(AdresseElectronique.EstValide(" jeanne@exemple.invalid"));
        Assert.False(AdresseElectronique.EstValide(null));
    }

    [Fact]
    public void Un_contact_a_un_nom_et_une_adresse_admis()
    {
        var contact = Contact.Nouveau(3, "  Jeanne Martin ", " jeanne@exemple.invalid ");

        Assert.Equal(3, contact.EntrepriseId);
        Assert.Equal("Jeanne Martin", contact.Nom);
        Assert.Equal("jeanne@exemple.invalid", contact.Email);

        contact.Modifier("Jeanne Dupont", "jeanne.dupont@exemple.invalid");
        Assert.Equal("Jeanne Dupont", contact.Nom);
        Assert.Equal("jeanne.dupont@exemple.invalid", contact.Email);
    }

    [Theory]
    [InlineData("", "jeanne@exemple.invalid")]
    [InlineData("Jeanne\nMartin", "jeanne@exemple.invalid")]
    [InlineData("Jeanne", "jeanne")]
    [InlineData("Jeanne", null)]
    public void Un_contact_aux_champs_refuses_ne_se_cree_ni_ne_se_modifie(string? nom, string? email)
    {
        Assert.Throws<ArgumentException>(() => Contact.Nouveau(1, nom, email));

        var contact = Contact.Nouveau(1, "Paul Lambert", "paul@exemple.invalid");
        Assert.Throws<ArgumentException>(() => contact.Modifier(nom, email));
        Assert.Equal("Paul Lambert", contact.Nom);
        Assert.Equal("paul@exemple.invalid", contact.Email);
    }
}
