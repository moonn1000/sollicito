using Sollicito.Application.Sauvegardes;

namespace Sollicito.Application.Tests;

/// <summary>
/// Les noms des copies (#26) : Sollicito ne reconnaît que ceux qu'il écrit, et chaque installation nomme ses copies du
/// jour par un identifiant qui ne révèle ni le poste ni le compte.
/// </summary>
public sealed class NomsDesCopiesTests
{
    private const string Installation = "1a2b3c4d";

    [Fact]
    public void L_identifiant_d_une_installation_tient_en_huit_caracteres_hexadecimaux_et_ne_cite_ni_le_poste_ni_le_dossier()
    {
        var identifiant = NomsDesCopies.Installation("POSTE-FICTIF", @"C:\Users\Compte.Fictif\AppData\Local\Sollicito");

        Assert.Matches("^[0-9a-f]{8}$", identifiant);
        Assert.DoesNotContain("fictif", identifiant, StringComparison.OrdinalIgnoreCase);
        // Le même poste et le même dossier donnent le même identifiant, barre finale ou non.
        Assert.Equal(identifiant, NomsDesCopies.Installation("POSTE-FICTIF", @"C:\Users\Compte.Fictif\AppData\Local\Sollicito\"));
    }

    [Fact]
    public void Deux_postes_ou_deux_comptes_ont_des_identifiants_differents()
    {
        var reference = NomsDesCopies.Installation("POSTE-FICTIF", @"C:\Users\Compte.Fictif\AppData\Local\Sollicito");

        Assert.NotEqual(reference, NomsDesCopies.Installation("AUTRE-POSTE", @"C:\Users\Compte.Fictif\AppData\Local\Sollicito"));
        Assert.NotEqual(reference, NomsDesCopies.Installation("POSTE-FICTIF", @"C:\Users\Autre.Compte\AppData\Local\Sollicito"));
    }

    [Fact]
    public void La_copie_du_jour_porte_l_installation_et_le_jour()
    {
        var nom = NomsDesCopies.DuJour(Installation, new DateOnly(2026, 11, 18));

        Assert.Equal("sollicito-1a2b3c4d-2026-11-18.db", nom);
        Assert.Equal(new CopieNommee.DuJour(nom, Installation, new DateOnly(2026, 11, 18)), NomsDesCopies.Lire(nom));
    }

    [Fact]
    public void La_copie_d_avant_une_mise_a_jour_porte_sa_migration()
    {
        var nom = NomsDesCopies.AvantMiseAJour("20261120164027_CorrectionsDesEtapes");

        Assert.Equal("sollicito-avant-20261120164027_CorrectionsDesEtapes.db", nom);
        Assert.Equal(new CopieNommee.AvantMiseAJour(nom, "20261120164027_CorrectionsDesEtapes"), NomsDesCopies.Lire(nom));
    }

    [Theory]
    [InlineData("1A2B3C4D")]
    [InlineData("1a2b3c4")]
    [InlineData("1a2b3c4d5")]
    [InlineData("../../x1")]
    public void Un_identifiant_d_installation_mal_forme_est_refuse(string installation)
    {
        Assert.Throws<ArgumentException>(() => NomsDesCopies.DuJour(installation, new DateOnly(2026, 11, 18)));
    }

    [Theory]
    [InlineData("CorrectionsDesEtapes")]
    [InlineData("20261120164027_..\\Corrections")]
    [InlineData("")]
    public void Une_migration_mal_formee_est_refusee(string migration)
    {
        Assert.Throws<ArgumentException>(() => NomsDesCopies.AvantMiseAJour(migration));
    }

    [Theory]
    [InlineData("sollicito.db")]
    [InlineData("reglages.json")]
    [InlineData("photo-de-vacances.jpg")]
    [InlineData("sollicito-1a2b3c4d-2026-11-18.db.0a1b2c3d.partielle")]
    [InlineData("sollicito-1a2b3c4d-2026-13-40.db")]
    [InlineData("sollicito-1A2B3C4D-2026-11-18.db")]
    [InlineData("sollicito-1a2b3c4d-2026-11-18.db.bak")]
    [InlineData("copie de sollicito-1a2b3c4d-2026-11-18.db")]
    [InlineData("sollicito-avant-.db")]
    public void Un_autre_nom_ne_designe_aucune_copie(string nom)
    {
        Assert.Null(NomsDesCopies.Lire(nom));
    }

    [Fact]
    public void Un_nom_provisoire_ne_designe_aucune_copie_et_se_reconnait_a_son_installation()
    {
        var provisoire = NomsDesCopies.Provisoire(NomsDesCopies.DuJour(Installation, new DateOnly(2026, 11, 18)));

        Assert.Matches(@"^sollicito-1a2b3c4d-2026-11-18\.db\.[0-9a-f]{8}\.partielle$", provisoire);
        Assert.Null(NomsDesCopies.Lire(provisoire));
        Assert.True(NomsDesCopies.EstUnProvisoireDe(provisoire, Installation));
        Assert.False(NomsDesCopies.EstUnProvisoireDe(provisoire, "99999999"));
        Assert.False(NomsDesCopies.EstUnProvisoireDe(NomsDesCopies.DuJour(Installation, new DateOnly(2026, 11, 18)), Installation));
        Assert.True(NomsDesCopies.EstUnProvisoireDe(NomsDesCopies.Provisoire(NomsDesCopies.AvantMiseAJour("20261120164027_CorrectionsDesEtapes")), installation: null));
    }
}
