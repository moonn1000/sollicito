using Sollicito.Domain.Offres;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Les champs d'une offre (#36, ADR-0013) : le poste requis, avec une lettre ou un chiffre ; le lieu, la date de
/// publication et l'adresse de l'annonce facultatifs ; puis l'offre elle-même, son import et sa correction.
/// </summary>
public sealed class ChampsDUneOffreTests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 22);

    private static readonly DateTime Maintenant = new(2026, 11, 22, 9, 30, 0, DateTimeKind.Utc);

    private static readonly string Empreinte = EmpreinteDUneOffre.De(["Texte fictif"]);

    [Theory]
    [InlineData("!!!")]
    [InlineData("€ € €")]
    public void Un_poste_sans_lettre_ni_chiffre_est_refuse(string poste)
    {
        Assert.False(Offre.EssayerDeLireLePoste(poste, out var lu, out var erreur));

        Assert.Null(lu);
        Assert.Equal(Offre.PosteSansLettre, erreur);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_lieu_une_date_et_une_adresse_vides_ne_font_rien_et_sont_admis(string? vide)
    {
        Assert.True(ChampsDUneOffre.EssayerDeLire("Comptable", vide, vide, vide, Aujourdhui, out var champs, out var refus));

        Assert.True(refus.Aucun);
        Assert.Null(champs!.Lieu);
        Assert.Null(champs.PublieeLe);
        Assert.Null(champs.Adresse);
    }

    [Theory]
    [InlineData("2026-11-22", "2026-11-22")]
    [InlineData(" 2000-01-01 ", "2000-01-01")]
    [InlineData("2026-02-28", "2026-02-28")]
    public void Une_date_de_publication_s_ecrit_AAAA_MM_JJ_du_1er_janvier_2000_a_aujourd_hui(string saisie, string attendue)
    {
        Assert.True(DateDePublication.EssayerDeLire(saisie, Aujourdhui, out var date, out var erreur), erreur);

        Assert.Equal(DateOnly.ParseExact(attendue, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), date);
    }

    [Theory]
    [InlineData("22/11/2026", DateDePublication.Illisible)]
    [InlineData("2026-11-2", DateDePublication.Illisible)]
    [InlineData("2026-11-22T10:00", DateDePublication.Illisible)]
    [InlineData("2026-02-30", DateDePublication.Illisible)]
    [InlineData("hier", DateDePublication.Illisible)]
    [InlineData("2026-11-23", DateDePublication.Future)]
    [InlineData("2062-11-22", DateDePublication.Future)]
    [InlineData("1999-12-31", DateDePublication.TropAncienne)]
    public void Une_date_illisible_future_ou_trop_ancienne_est_refusee(string saisie, string motif)
    {
        Assert.False(DateDePublication.EssayerDeLire(saisie, Aujourdhui, out var date, out var erreur));

        Assert.Null(date);
        Assert.Equal(motif, erreur);
    }

    [Theory]
    [InlineData("https://exemple.invalid/offres/123?source=fictive#detail")]
    [InlineData("http://exemple.invalid/")]
    [InlineData("HTTPS://Exemple.Invalid:8443/offre")]
    [InlineData("https://emploi.exemple.invalid/offre/développeur-é")]
    [InlineData("https://[::1]:8080/")]
    public void Une_adresse_absolue_en_http_ou_https_est_gardee_telle_que_saisie(string saisie)
    {
        Assert.True(AdresseDUneAnnonce.EssayerDeLire("  " + saisie + " ", out var adresse, out var erreur), erreur);

        Assert.Equal(saisie, adresse);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("mailto:contact@exemple.invalid")]
    [InlineData("ftp://exemple.invalid/offre")]
    [InlineData("file:///C:/offre.html")]
    [InlineData("//exemple.invalid/offre")]
    [InlineData("exemple.invalid/offre")]
    [InlineData("/offres/12")]
    [InlineData("https://")]
    [InlineData("https:///offre")]
    [InlineData("http:exemple.invalid")]
    [InlineData("https://exemple.invalid/une offre")]
    [InlineData("https://exemple.invalid/\toffre")]
    [InlineData("https://exemple.invalid/\noffre")]
    [InlineData("https://exemple.invalid/\u0001")]
    public void Une_adresse_qui_n_est_pas_une_adresse_web_complete_est_refusee(string saisie)
    {
        Assert.False(AdresseDUneAnnonce.EssayerDeLire(saisie, out var adresse, out var erreur));

        Assert.Null(adresse);
        Assert.Equal(AdresseDUneAnnonce.Invalide, erreur);
    }

    [Theory]
    [InlineData("https://nom@exemple.invalid/offre")]
    [InlineData("https://nom:motdepasse@exemple.invalid/offre")]
    [InlineData("http://banque.exemple.invalid@autre.exemple.invalid/")]
    public void Une_adresse_qui_porte_des_identifiants_est_refusee(string saisie)
    {
        Assert.False(AdresseDUneAnnonce.EssayerDeLire(saisie, out _, out var erreur));

        Assert.Equal(AdresseDUneAnnonce.AvecIdentifiants, erreur);
    }

    [Fact]
    public void Une_adresse_de_la_longueur_maximale_est_admise_un_caractere_de_plus_est_refuse()
    {
        const string debut = "https://exemple.invalid/";
        var auMaximum = debut + new string('a', AdresseDUneAnnonce.LongueurMaximale - debut.Length);

        Assert.True(AdresseDUneAnnonce.EssayerDeLire(auMaximum, out _, out _));
        Assert.False(AdresseDUneAnnonce.EssayerDeLire(auMaximum + "a", out _, out var erreur));
        Assert.Equal(AdresseDUneAnnonce.TropLongue, erreur);
    }

    [Fact]
    public void Les_refus_se_donnent_champ_par_champ_sans_reprendre_le_texte_saisi()
    {
        Assert.False(ChampsDUneOffre.EssayerDeLire("Poste\nfictif", "Lieu\tfictif", "2026-13-01", "javascript:fictif", Aujourdhui, out var champs, out var refus));

        Assert.Null(champs);
        Assert.Equal("Le poste ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide.", refus.Poste);
        Assert.Equal("Le lieu ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide.", refus.Lieu);
        Assert.Equal(DateDePublication.Illisible, refus.PublieeLe);
        Assert.Equal(AdresseDUneAnnonce.Invalide, refus.Adresse);
        Assert.DoesNotContain(new[] { refus.Poste, refus.Lieu, refus.PublieeLe, refus.Adresse }, motif => motif!.Contains("fictif", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_offre_importee_garde_ses_champs_son_texte_et_son_poste_normalise()
    {
        var champs = Champs("Développeur .NET (H/F)", "Liège", "2026-11-18", "https://exemple.invalid/offre");

        var offre = Offre.Importer(SourceDOffre.TexteColle, Empreinte, 3, champs, "Texte fictif", Maintenant);

        Assert.Equal(SourceDOffre.TexteColle, offre.Source);
        Assert.Equal(Empreinte, offre.Empreinte);
        Assert.Equal(3, offre.EntrepriseId);
        Assert.Equal("Développeur .NET (H/F)", offre.Poste);
        Assert.Equal("developpeur net h f", offre.PosteNormalise);
        Assert.Equal("Liège", offre.Lieu);
        Assert.Equal(new DateOnly(2026, 11, 18), offre.PublieeLe);
        Assert.Equal("https://exemple.invalid/offre", offre.Adresse);
        Assert.Equal("Texte fictif", offre.Texte);
        Assert.Equal(Maintenant, offre.AjouteeLe);
    }

    [Fact]
    public void Une_offre_ne_s_importe_qu_avec_une_empreinte_un_texte_retouche_et_un_instant_en_UTC()
    {
        var champs = Champs("Comptable");

        Assert.Throws<ArgumentException>(() => Offre.Importer(SourceDOffre.TexteColle, Empreinte.ToUpperInvariant(), null, champs, null, Maintenant));
        Assert.Throws<ArgumentException>(() => Offre.Importer(SourceDOffre.TexteColle, Empreinte, null, champs, " Texte fictif ", Maintenant));
        Assert.Throws<ArgumentException>(() => Offre.Importer(SourceDOffre.TexteColle, Empreinte, null, champs, "Texte" + (char)0x0001, Maintenant));
        Assert.Throws<ArgumentException>(() => Offre.Importer(SourceDOffre.TexteColle, Empreinte, null, champs, null, Maintenant.ToLocalTime()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Offre.Importer(SourceDOffre.TexteColle, Empreinte, 0, champs, null, Maintenant));
        Assert.Throws<ArgumentOutOfRangeException>(() => Offre.Importer((SourceDOffre)42, Empreinte, null, champs, null, Maintenant));
    }

    [Fact]
    public void Corriger_change_les_champs_et_le_poste_normalise_jamais_le_texte_la_source_ni_l_empreinte()
    {
        var offre = Offre.Importer(SourceDOffre.TexteColle, Empreinte, 3, Champs("Comptable", "Namur"), "Texte fictif", Maintenant);

        offre.Corriger(null, Champs("Comptable confirmé"));

        Assert.Null(offre.EntrepriseId);
        Assert.Equal("Comptable confirmé", offre.Poste);
        Assert.Equal("comptable confirme", offre.PosteNormalise);
        Assert.Null(offre.Lieu);
        Assert.Equal("Texte fictif", offre.Texte);
        Assert.Equal(SourceDOffre.TexteColle, offre.Source);
        Assert.Equal(Empreinte, offre.Empreinte);
    }

    [Fact]
    public void Seul_un_changement_d_entreprise_ou_de_poste_normalise_refait_la_comparaison_de_la_regle_3()
    {
        var offre = Offre.Importer(SourceDOffre.TexteColle, Empreinte, 3, Champs("Développeur .NET"), "Texte fictif", Maintenant);

        Assert.False(offre.ChangeraitLeDoublonnage(3, Champs("DÉVELOPPEUR  .net", "Liège")));
        Assert.True(offre.ChangeraitLeDoublonnage(4, Champs("Développeur .NET")));
        Assert.True(offre.ChangeraitLeDoublonnage(null, Champs("Développeur .NET")));
        Assert.True(offre.ChangeraitLeDoublonnage(3, Champs("Développeur Java")));
    }

    private static ChampsDUneOffre Champs(string poste, string? lieu = null, string? publieeLe = null, string? adresse = null)
    {
        Assert.True(ChampsDUneOffre.EssayerDeLire(poste, lieu, publieeLe, adresse, Aujourdhui, out var champs, out var refus), refus.ToString());
        return champs;
    }
}
