using Sollicito.Application.Sauvegardes;

namespace Sollicito.Application.Tests;

/// <summary>
/// Ce que la restauration (#28) tire des noms des copies : la copie d'avant une restauration se reconnaît et se liste, la
/// copie brute d'une base abîmée ne se liste pas, les copies d'une autre installation se listent à part, et l'identifiant
/// d'une copie, que la page renvoie, ne porte aucun chemin.
/// </summary>
public sealed class RestaurationDUneCopieTests
{
    private const string Installation = "1a2b3c4d";
    private static readonly DateTimeOffset Instant = new(2026, 11, 18, 13, 30, 5, 123, TimeSpan.Zero);

    [Fact]
    public void La_copie_d_avant_une_restauration_porte_son_instant_et_se_reconnait()
    {
        var nom = NomsDesCopies.AvantRestauration(Instant);

        Assert.Equal("sollicito-avant-restauration-20261118-133005123.db", nom);
        var lue = Assert.IsType<CopieNommee.AvantRestauration>(NomsDesCopies.Lire(nom));
        Assert.Equal(new DateTime(2026, 11, 18, 13, 30, 5, 123, DateTimeKind.Utc), lue.Instant);
        Assert.Equal(DateTimeKind.Utc, lue.Instant.Kind);
        // Hors du motif des copies du jour et de celles d'avant une mise à jour : aucune rotation ne la voit.
        Assert.Empty(PlanDesCopies.CopiesDuJour([nom], Installation));
        Assert.Empty(PlanDesCopies.AEffacer([nom], Installation));
        Assert.Empty(PlanDesCopies.CopiesDAvantUneMiseAJour([nom]));
    }

    [Fact]
    public void La_copie_brute_d_une_base_abimee_ne_se_reconnait_pas_mais_son_provisoire_si()
    {
        var brute = NomsDesCopies.AvantRestaurationBrute(Instant);

        Assert.Equal("sollicito-avant-restauration-20261118-133005123-brute.db", brute);
        Assert.Null(NomsDesCopies.Lire(brute));
        Assert.Null(NomsDesCopies.Lire(brute + "-wal"));
        // Ses fichiers provisoires, et ceux d'une copie d'avant, se retirent au lancement suivant un arrêt.
        Assert.True(NomsDesCopies.EstUnProvisoireDe(NomsDesCopies.Provisoire(brute), installation: null));
        Assert.True(NomsDesCopies.EstUnProvisoireDe(NomsDesCopies.Provisoire(brute + "-wal"), installation: null));
        Assert.True(NomsDesCopies.EstUnProvisoireDe(NomsDesCopies.Provisoire(NomsDesCopies.AvantRestauration(Instant)), installation: null));
        Assert.False(NomsDesCopies.EstUnProvisoireDe(NomsDesCopies.AvantRestauration(Instant), installation: null));
    }

    [Fact]
    public void Les_copies_d_avant_une_restauration_et_celles_d_une_autre_installation_se_listent_a_part()
    {
        string[] noms =
        [
            NomsDesCopies.AvantRestauration(Instant),
            NomsDesCopies.AvantRestauration(Instant.AddDays(1)),
            NomsDesCopies.AvantRestaurationBrute(Instant.AddDays(2)),
            NomsDesCopies.DuJour(Installation, new DateOnly(2026, 11, 18)),
            NomsDesCopies.DuJour("9f8e7d6c", new DateOnly(2026, 11, 17)),
            NomsDesCopies.DuJour("9f8e7d6c", new DateOnly(2026, 11, 19)),
            "notes-personnelles.db",
        ];

        Assert.Equal([noms[1], noms[0]], PlanDesCopies.CopiesDAvantUneRestauration(noms).Select(copie => copie.Nom));
        Assert.Equal([noms[5], noms[4]], PlanDesCopies.CopiesDuJourDesAutresInstallations(noms, Installation).Select(copie => copie.Nom));
        Assert.Equal([noms[3]], PlanDesCopies.CopiesDuJour(noms, Installation).Select(copie => copie.Nom));
        // La rotation de cette installation ne touche jamais aux copies d'une autre.
        Assert.Empty(PlanDesCopies.AEffacer(noms, Installation));
    }

    [Fact]
    public void L_identifiant_d_une_copie_tient_en_seize_caracteres_hexadecimaux_et_change_avec_son_instant()
    {
        var faiteLe = new DateTime(2026, 11, 18, 13, 30, 0, DateTimeKind.Utc);
        var nom = NomsDesCopies.DuJour(Installation, new DateOnly(2026, 11, 18));

        var identifiant = IdentifiantsDesCopies.De(LieuDUneCopie.DossierDesCopies, nom, faiteLe);

        Assert.Matches("^[0-9a-f]{16}$", identifiant);
        Assert.True(IdentifiantsDesCopies.AEnLaForme(identifiant));
        Assert.Equal(identifiant, IdentifiantsDesCopies.De(LieuDUneCopie.DossierDesCopies, nom, faiteLe));
        Assert.NotEqual(identifiant, IdentifiantsDesCopies.De(LieuDUneCopie.DossierDesCopies, nom, faiteLe.AddTicks(1)));
        Assert.NotEqual(identifiant, IdentifiantsDesCopies.De(LieuDUneCopie.DossierParDefaut, nom, faiteLe));
        Assert.DoesNotContain("sollicito", identifiant, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789abcdeg")]
    [InlineData(@"C:\Sauvegardes\copie")]
    [InlineData("sollicito-1a2b3c4d-2026-11-18.db")]
    [InlineData(" 0123456789abcdef")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_texte_d_une_autre_forme_n_est_pas_un_identifiant(string? texte)
    {
        Assert.False(IdentifiantsDesCopies.AEnLaForme(texte));
    }
}
