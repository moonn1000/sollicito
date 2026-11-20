using Sollicito.Domain.Entreprises;

namespace Sollicito.Domain.Tests;

/// <summary>Une entreprise se crée d'un nom saisi, et se reconnaît à la clé de ce nom (ADR-0005).</summary>
public sealed class EntrepriseTests
{
    [Fact]
    public void Une_nouvelle_entreprise_garde_le_nom_saisi_et_sa_cle()
    {
        var entreprise = Entreprise.Nouvelle("  Lumière Verte SA ");

        Assert.Equal("Lumière Verte SA", entreprise.Nom);
        Assert.Equal("lumiere verte", entreprise.Cle);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("- . -")]
    public void Un_nom_sans_lettre_ni_chiffre_est_refuse(string saisie)
    {
        Assert.False(Entreprise.EssayerDeLireLeNom(saisie, out _, out var erreur));

        Assert.Equal("Le nom de l'entreprise doit contenir au moins une lettre ou un chiffre.", erreur);
        Assert.Throws<ArgumentException>(() => Entreprise.Nouvelle(saisie));
    }

    [Theory]
    [InlineData(null, "Indiquez l'entreprise.")]
    [InlineData("  ", "Indiquez l'entreprise.")]
    [InlineData("Lumière\nVerte", "Le nom de l'entreprise ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide.")]
    public void Un_nom_absent_ou_invalide_est_refuse_avec_un_message_lisible(string? saisie, string message)
    {
        Assert.False(Entreprise.EssayerDeLireLeNom(saisie, out _, out var erreur));

        Assert.Equal(message, erreur);
    }

    [Fact]
    public void Un_nom_trop_long_est_refuse()
    {
        Assert.False(Entreprise.EssayerDeLireLeNom(new string('L', 201), out _, out var erreur));

        Assert.Equal("Le nom de l'entreprise ne doit pas dépasser 200 caractères.", erreur);
    }

    [Fact]
    public void Deux_noms_de_meme_cle_designent_la_meme_entreprise()
    {
        Assert.Equal(Entreprise.Nouvelle("Lumière Verte SA").Cle, Entreprise.Nouvelle("LUMIÈRE VERTE S.A.").Cle);
        Assert.NotEqual(Entreprise.Nouvelle("Lumière Verte").Cle, Entreprise.Nouvelle("Lumière Verte Belgique").Cle);
    }

    [Fact]
    public void Le_nom_affiche_se_corrige_par_un_nom_de_meme_cle()
    {
        var entreprise = Entreprise.Nouvelle("Lumiere Verte");

        entreprise.CorrigerLeNom("  Lumière Verte SA ");

        Assert.Equal(("Lumière Verte SA", "lumiere verte"), (entreprise.Nom, entreprise.Cle));
    }

    [Theory]
    [InlineData("Lumière Verte Belgique")]
    [InlineData("Brasserie Lune Rousse")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Un_nom_d_une_autre_cle_ou_refuse_ne_corrige_pas_le_nom(string saisie)
    {
        var entreprise = Entreprise.Nouvelle("Lumière Verte SA");

        Assert.Throws<ArgumentException>(() => entreprise.CorrigerLeNom(saisie));

        Assert.Equal("Lumière Verte SA", entreprise.Nom);
    }
}
