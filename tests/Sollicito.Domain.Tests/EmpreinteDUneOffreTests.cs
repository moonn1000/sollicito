using Sollicito.Domain.Offres;

namespace Sollicito.Domain.Tests;

/// <summary>
/// L'empreinte d'une offre (règle 2) et son poste normalisé (règle 3) : modèle de domaine, « Dédoublonner les offres » ;
/// ADR-0013. Les caractères qui ne s'écrivent pas en clair dans le code (séparateurs de ligne, contrôles) s'y écrivent
/// par leur point de code.
/// </summary>
public sealed class EmpreinteDUneOffreTests
{
    private static readonly string SeparateurDeLigne = ((char)0x2028).ToString();
    private static readonly string SeparateurDeParagraphe = ((char)0x2029).ToString();
    private static readonly string Insecable = ((char)0x00A0).ToString();
    private static readonly string Fine = ((char)0x202F).ToString();
    private static readonly string Ligature = ((char)0xFB01).ToString();
    private static readonly string SeparateurDUnite = ((char)0x001F).ToString();

    [Fact]
    public void L_empreinte_est_le_sha_256_en_hexadecimal_minuscule_du_texte_normalise()
    {
        // Valeur relevée par hashlib (Python), sur « développeur .net bruxelles » codé en UTF-8.
        Assert.Equal(
            "a8c3391fb70d5a5b3cf05348d1f0f5f5f0016530e2b603cdd8a0bc4cdbe8b5f2",
            EmpreinteDUneOffre.De(["  Développeur .NET\r\n\r\n\tBruxelles  "]));
    }

    [Fact]
    public void Les_blancs_et_les_fins_de_ligne_de_toute_sorte_ne_changent_pas_l_empreinte()
    {
        var reference = EmpreinteDUneOffre.De(["développeur .net bruxelles"]);

        string[] variantes =
        [
            "Développeur .NET Bruxelles",
            "DÉVELOPPEUR   .NET\nBRUXELLES",
            "Développeur\t.NET\r\nBruxelles\n",
            "Développeur" + Insecable + ".NET" + Fine + "Bruxelles",
            "Développeur" + SeparateurDeLigne + ".NET" + SeparateurDeParagraphe + "Bruxelles",
            "Développeur\f.NET\vBruxelles",
            "Développeur" + (char)0x0085 + ".NET Bruxelles",
        ];

        Assert.All(variantes, variante => Assert.Equal(reference, EmpreinteDUneOffre.De([variante])));
    }

    [Fact]
    public void Les_accents_restent_et_la_normalisation_nfkc_rapproche_les_formes_compatibles()
    {
        Assert.NotEqual(EmpreinteDUneOffre.De(["Développeur"]), EmpreinteDUneOffre.De(["Developpeur"]));
        // « e » suivi de l'accent aigu combinant se recompose en « é ».
        Assert.Equal(EmpreinteDUneOffre.De(["Développeur"]), EmpreinteDUneOffre.De(["De" + (char)0x0301 + "veloppeur"]));
        Assert.Equal(EmpreinteDUneOffre.De(["profil"]), EmpreinteDUneOffre.De(["pro" + Ligature + "l"]));
    }

    [Fact]
    public void Les_parties_se_joignent_par_le_separateur_d_unite_et_une_partie_vide_garde_sa_place()
    {
        // Valeur relevée par hashlib (Python), sur « a », U+001F, « b ».
        Assert.Equal("f04cdced9736a69da6103f08a4daaf8c485dd481217d218a1b4993c8c3968e13", EmpreinteDUneOffre.De(["A", " b "]));
        Assert.NotEqual(EmpreinteDUneOffre.De(["a", "b"]), EmpreinteDUneOffre.De(["a b"]));
        Assert.NotEqual(EmpreinteDUneOffre.De(["a", "b"]), EmpreinteDUneOffre.De(["a", "", "b"]));
        Assert.False(char.IsWhiteSpace(EmpreinteDUneOffre.Separateur));
    }

    [Fact]
    public void Une_partie_perd_ses_caracteres_de_controle_autres_que_les_blancs_separateur_d_unite_compris()
    {
        Assert.Equal(EmpreinteDUneOffre.De(["ab", "c"]), EmpreinteDUneOffre.De(["a" + (char)0x0001 + "b", "c" + SeparateurDUnite]));
        Assert.Equal(EmpreinteDUneOffre.De(["a", "b"]), EmpreinteDUneOffre.De(["a" + SeparateurDUnite, "b"]));
    }

    [Fact]
    public void Une_empreinte_a_la_forme_de_64_chiffres_hexadecimaux_minuscules()
    {
        var empreinte = EmpreinteDUneOffre.De(["Texte fictif"]);

        Assert.True(EmpreinteDUneOffre.APourForme(empreinte));
        Assert.False(EmpreinteDUneOffre.APourForme(empreinte.ToUpperInvariant()));
        Assert.False(EmpreinteDUneOffre.APourForme(empreinte[..63]));
        Assert.False(EmpreinteDUneOffre.APourForme(null));
        Assert.Throws<ArgumentException>(() => EmpreinteDUneOffre.De([]));
    }

    [Theory]
    [InlineData("Développeur .NET (H/F)", "developpeur net h f")]
    [InlineData("developpeur  .net h/f", "developpeur net h f")]
    [InlineData("Chargée de l’accueil", "chargee de l accueil")]
    [InlineData("Œnologue SA", "oenologue sa")]
    [InlineData("C++", "c")]
    [InlineData("!!!", "")]
    public void Le_poste_se_normalise_comme_le_nom_d_une_entreprise_sans_retirer_de_forme_juridique(string poste, string attendu)
    {
        Assert.Equal(attendu, PosteNormalise.De(poste));
    }
}
