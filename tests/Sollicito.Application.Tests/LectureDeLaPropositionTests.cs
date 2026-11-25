using Sollicito.Application.IA;

namespace Sollicito.Application.Tests;

/// <summary>
/// La lecture de la réponse d'une extraction (#46, ADR-0018) : un objet JSON seul, aux cinq clés connues, chaque valeur
/// une chaîne ou <c>null</c>, admise par la règle de son champ au collage. Une faute écarte toute la réponse ; une date
/// hors bornes, ou une adresse absente du texte, n'écartent que leur champ. Les caractères piégés se construisent par
/// leur point de code, jamais écrits tels quels dans ce fichier. Données fictives.
/// </summary>
public sealed class LectureDeLaPropositionTests
{
    private const char Antislash = (char)92;

    private const string Adresse = "https://emplois.exemple/offres/42";

    private static readonly DateOnly Aujourdhui = new(2026, 11, 25);

    private static readonly string TexteDeLOffre =
        "Lumière Verte SA recrute un comptable à Liège.\nPubliée le 20 novembre 2026.\nPostulez sur " + Adresse + " avant le 15 décembre.";

    // Un objet aux cinq clés, chacune remplacée par la valeur JSON donnée telle quelle.
    private static string Objet(
        string entreprise = "\"Lumière Verte SA\"",
        string poste = "\"Comptable\"",
        string lieu = "\"Liège\"",
        string publieeLe = "\"2026-11-20\"",
        string adresse = "\"" + Adresse + "\"") =>
        $$"""{"entreprise":{{entreprise}},"poste":{{poste}},"lieu":{{lieu}},"publieeLe":{{publieeLe}},"adresse":{{adresse}}}""";

    private static IssueDeLExtraction Lire(string reponse) => LectureDeLaProposition.Lire(reponse, TexteDeLOffre, Aujourdhui);

    private static ChampsProposes Proposes(string reponse, int ecartes = 0)
    {
        var proposee = Assert.IsType<IssueDeLExtraction.Proposee>(Lire(reponse));
        Assert.Equal(ecartes, proposee.Ecartes.Count);
        return proposee.Champs;
    }

    private static void HorsSchema(string reponse, MotifHorsSchema motif, ChampDeLaProposition? champ = null)
    {
        var hors = Assert.IsType<IssueDeLExtraction.HorsSchema>(Lire(reponse));
        Assert.Equal(motif, hors.Motif);
        Assert.Equal(champ, hors.Champ);
    }

    [Fact]
    public void Une_reponse_complete_propose_ses_cinq_champs()
    {
        var champs = Proposes(Objet());

        Assert.Equal(new ChampsProposes("Lumière Verte SA", "Comptable", "Liège", "2026-11-20", Adresse), champs);
        Assert.Equal(5, champs.Nombre);
    }

    [Fact]
    public void Un_champ_null_vide_ou_de_blancs_n_est_pas_propose()
    {
        var champs = Proposes(Objet(lieu: "null", publieeLe: "\"\"", adresse: "\"   \""));

        Assert.Equal(new ChampsProposes("Lumière Verte SA", "Comptable", null, null, null), champs);
        Assert.Equal(2, champs.Nombre);
    }

    [Fact]
    public void Cinq_null_ne_proposent_rien()
    {
        var champs = Proposes(Objet("null", "null", "null", "null", "null"));

        Assert.Equal(0, champs.Nombre);
    }

    [Fact]
    public void Les_blancs_du_bord_d_une_valeur_sont_retires_comme_au_collage()
    {
        var champs = Proposes(Objet(entreprise: "\"  Lumière Verte SA \"", adresse: "\" " + Adresse + "\""));

        Assert.Equal("Lumière Verte SA", champs.Entreprise);
        Assert.Equal(Adresse, champs.Adresse);
    }

    [Fact]
    public void Une_valeur_porteuse_de_balises_se_propose_telle_quelle_comme_un_texte()
    {
        var champs = Proposes(Objet(poste: "\"<img src=x onerror=alert(1)><script>alert(2)</script>\""));

        Assert.Equal("<img src=x onerror=alert(1)><script>alert(2)</script>", champs.Poste);
    }

    public static TheoryData<string> EnveloppesAdmises => new()
    {
        "  \n" + Objet() + "\n\t ",
        "```json\n" + Objet() + "\n```",
        "```\n" + Objet() + "\n```",
        "```json\r\n" + Objet() + "\r\n```\r\n",
        "\n```json\n  " + Objet() + "  \n```  \n",
    };

    [Theory]
    [MemberData(nameof(EnveloppesAdmises))]
    public void Seuls_des_blancs_et_un_bloc_de_code_peuvent_entourer_l_objet(string reponse)
    {
        Assert.Equal(5, Proposes(reponse).Nombre);
    }

    public static TheoryData<string> JsonIllisibles => new()
    {
        "",
        "   ",
        "Voici les champs : " + Objet(),
        Objet() + " Bonne recherche !",
        Objet() + Objet(),
        "```json\n" + Objet(),
        "```python\n" + Objet() + "\n```",
        "```json " + Objet() + " ```",
        "```json\n" + Objet() + "\n```\nVoilà.",
        """{"entreprise":"Lumière Verte SA","poste":"Comptable","lieu":null,"publieeLe":null,"adresse":null,}""",
        """{"entreprise":"Lumière Verte SA","poste":"Comptable","lieu":null,"publieeLe":null,"adresse":null} // fin""",
        """{"entreprise":"Lumière Verte SA","poste":"Comptable","poste":"Autre","lieu":null,"publieeLe":null,"adresse":null}""",
        """{"entreprise":"Lumière Verte SA","poste":""",
        "{'entreprise':'Lumière Verte SA'}",
    };

    [Theory]
    [MemberData(nameof(JsonIllisibles))]
    public void Du_texte_autour_un_json_mal_forme_ou_une_propriete_repetee_ecartent_tout(string reponse)
    {
        HorsSchema(reponse, MotifHorsSchema.JsonIllisible);
    }

    [Fact]
    public void Un_echappement_de_substitut_isole_dans_une_valeur_ou_une_cle_ecarte_tout()
    {
        var substitut = Antislash + "ud800";
        HorsSchema(Objet(poste: "\"Comptable" + substitut + "\""), MotifHorsSchema.JsonIllisible);
        HorsSchema("{\"poste" + substitut + "\":null}", MotifHorsSchema.JsonIllisible);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"Comptable\"")]
    [InlineData("null")]
    [InlineData("42")]
    public void Autre_chose_qu_un_objet_ecarte_tout(string reponse)
    {
        HorsSchema(reponse, MotifHorsSchema.PasUnObjet);
    }

    public static TheoryData<string> ClesInconnues => new()
    {
        """{"entreprise":null,"poste":null,"lieu":null,"publieeLe":null,"adresse":null,"salaire":"3 000 €"}""",
        """{"entreprise":null,"Poste":null,"lieu":null,"publieeLe":null,"adresse":null}""",
        """{"entreprise":null,"poste ":null,"lieu":null,"publieeLe":null,"adresse":null}""",
        """{"entreprise":null,"poste":null,"lieu":null,"publiee_le":null,"adresse":null}""",
    };

    [Theory]
    [MemberData(nameof(ClesInconnues))]
    public void Une_cle_inconnue_ou_ecrite_autrement_ecarte_tout(string reponse)
    {
        HorsSchema(reponse, MotifHorsSchema.CleInconnue);
    }

    [Fact]
    public void Une_cle_absente_ecarte_tout()
    {
        HorsSchema("""{"entreprise":"Lumière Verte SA","poste":"Comptable","lieu":null,"publieeLe":null}""", MotifHorsSchema.CleAbsente);
        HorsSchema("{}", MotifHorsSchema.CleAbsente);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{\"nom\":\"Lumière Verte SA\"}")]
    [InlineData("[\"Lumière Verte SA\"]")]
    public void Une_valeur_qui_n_est_ni_une_chaine_ni_null_ecarte_tout(string valeur)
    {
        HorsSchema(Objet(entreprise: valeur), MotifHorsSchema.TypeInattendu);
    }

    public static TheoryData<string, ChampDeLaProposition> ValeursRefusees => new()
    {
        { Objet(entreprise: Chaine(new string('a', 201))), ChampDeLaProposition.Entreprise },
        { Objet(entreprise: Chaine("---")), ChampDeLaProposition.Entreprise },
        { Objet(poste: Chaine("!!!")), ChampDeLaProposition.Poste },
        { Objet(poste: Chaine(new string('p', 201))), ChampDeLaProposition.Poste },
        { Objet(poste: "\"Comptable" + Antislash + "nH/F\""), ChampDeLaProposition.Poste },
        { Objet(poste: "\"Comptable" + Antislash + "tH/F\""), ChampDeLaProposition.Poste },
        { Objet(poste: Chaine("Comptable" + (char)0x202E + "F/H")), ChampDeLaProposition.Poste },
        { Objet(poste: Chaine("Comptable" + (char)0x2066 + "F/H")), ChampDeLaProposition.Poste },
        { Objet(poste: Chaine("Comptable" + (char)0xFFFF)), ChampDeLaProposition.Poste },
        { Objet(poste: "\"Comptable" + Antislash + "u0007\""), ChampDeLaProposition.Poste },
        { Objet(lieu: Chaine(new string('l', 201))), ChampDeLaProposition.Lieu },
        { Objet(lieu: Chaine("Liège" + (char)0x202B)), ChampDeLaProposition.Lieu },
        { Objet(publieeLe: Chaine("20/11/2026")), ChampDeLaProposition.PublieeLe },
        { Objet(publieeLe: Chaine("2026-13-01")), ChampDeLaProposition.PublieeLe },
        { Objet(publieeLe: Chaine("2026-02-30")), ChampDeLaProposition.PublieeLe },
        { Objet(publieeLe: Chaine("il y a trois jours")), ChampDeLaProposition.PublieeLe },
        { Objet(publieeLe: Chaine("2026-11-20T10:00:00")), ChampDeLaProposition.PublieeLe },
        { Objet(adresse: Chaine("javascript:alert(1)")), ChampDeLaProposition.Adresse },
        { Objet(adresse: Chaine("data:text/html,<script>alert(1)</script>")), ChampDeLaProposition.Adresse },
        { Objet(adresse: Chaine("file:///C:/Windows/win.ini")), ChampDeLaProposition.Adresse },
        { Objet(adresse: Chaine("/offres/42")), ChampDeLaProposition.Adresse },
        { Objet(adresse: Chaine("https://nom:motdepasse@emplois.exemple/offres/42")), ChampDeLaProposition.Adresse },
        { Objet(adresse: Chaine("https://emplois.exemple/offres/4 2")), ChampDeLaProposition.Adresse },
        { Objet(adresse: Chaine("https://emplois.exemple/" + new string('a', 1977))), ChampDeLaProposition.Adresse },
    };

    [Theory]
    [MemberData(nameof(ValeursRefusees))]
    public void Une_valeur_que_le_collage_refuserait_ecarte_toute_la_reponse(string reponse, ChampDeLaProposition champ)
    {
        HorsSchema(reponse, MotifHorsSchema.ChampRefuse, champ);
    }

    [Fact]
    public void Une_adresse_javascript_ecarte_tout_meme_quand_le_texte_la_porte()
    {
        var texte = "Postulez ici : javascript:alert(1)";

        var hors = Assert.IsType<IssueDeLExtraction.HorsSchema>(
            LectureDeLaProposition.Lire(Objet(adresse: Chaine("javascript:alert(1)")), texte, Aujourdhui));

        Assert.Equal(ChampDeLaProposition.Adresse, hors.Champ);
    }

    [Fact]
    public void Les_bornes_du_collage_restent_admises()
    {
        var champs = Proposes(Objet(
            entreprise: Chaine(new string('e', 200)),
            poste: Chaine(new string('p', 200)),
            lieu: Chaine(new string('l', 200)),
            publieeLe: Chaine("2026-11-25"),
            adresse: "null"));

        Assert.Equal(4, champs.Nombre);
        Assert.Equal("2026-11-25", champs.PublieeLe);
    }

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2026-11-26")]
    [InlineData("2030-01-15")]
    public void Une_date_hors_bornes_n_ecarte_que_son_champ(string date)
    {
        var proposee = Assert.IsType<IssueDeLExtraction.Proposee>(Lire(Objet(publieeLe: Chaine(date))));

        Assert.Null(proposee.Champs.PublieeLe);
        Assert.Equal(4, proposee.Champs.Nombre);
        Assert.Equal(new ChampEcarte(ChampDeLaProposition.PublieeLe, MotifDeLEcart.DateHorsBornes), Assert.Single(proposee.Ecartes));
    }

    [Theory]
    [InlineData("https://ailleurs.exemple/offres/42")]
    [InlineData("https://EMPLOIS.exemple/offres/42")]
    [InlineData("https://emplois.exemple/offres/4")]
    public void Une_adresse_absente_du_texte_de_l_offre_n_ecarte_que_son_champ(string adresse)
    {
        var proposee = Assert.IsType<IssueDeLExtraction.Proposee>(Lire(Objet(adresse: Chaine(adresse + "/autre"))));

        Assert.Null(proposee.Champs.Adresse);
        Assert.Equal(4, proposee.Champs.Nombre);
        Assert.Equal(new ChampEcarte(ChampDeLaProposition.Adresse, MotifDeLEcart.AdresseAbsenteDuTexte), Assert.Single(proposee.Ecartes));
    }

    [Theory]
    [InlineData("https://emplois.exemple/offres/4")]
    [InlineData("https://emplois.exemple")]
    [InlineData("https://emplois.exemple/")]
    public void Un_debut_d_adresse_du_texte_n_est_pas_propose(string adresse)
    {
        var proposee = Assert.IsType<IssueDeLExtraction.Proposee>(Lire(Objet(adresse: Chaine(adresse))));

        Assert.Null(proposee.Champs.Adresse);
        Assert.Equal(new ChampEcarte(ChampDeLaProposition.Adresse, MotifDeLEcart.AdresseAbsenteDuTexte), Assert.Single(proposee.Ecartes));
    }

    [Theory]
    [InlineData("Postulez sur https://emplois.exemple/offres/42")]
    [InlineData("Postulez sur https://emplois.exemple/offres/42.")]
    [InlineData("(https://emplois.exemple/offres/42)")]
    [InlineData("« https://emplois.exemple/offres/42 »")]
    [InlineData("https://emplois.exemple/offres/4 puis https://emplois.exemple/offres/42\n")]
    public void Une_adresse_entiere_du_texte_est_proposee_quelle_que_soit_la_ponctuation_qui_la_suit(string texte)
    {
        var proposee = Assert.IsType<IssueDeLExtraction.Proposee>(LectureDeLaProposition.Lire(Objet(), texte, Aujourdhui));

        Assert.Equal(Adresse, proposee.Champs.Adresse);
    }

    [Fact]
    public void Une_faute_ailleurs_ecarte_tout_meme_avec_un_champ_deja_ecarte()
    {
        HorsSchema(Objet(publieeLe: Chaine("1999-12-31"), adresse: Chaine("javascript:alert(1)")), MotifHorsSchema.ChampRefuse, ChampDeLaProposition.Adresse);
    }

    [Fact]
    public void Le_texte_d_une_issue_ne_montre_aucune_valeur()
    {
        var proposee = Assert.IsType<IssueDeLExtraction.Proposee>(Lire(Objet()));

        Assert.DoesNotContain("Lumière", proposee.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Lumière", proposee.Champs.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("emplois", proposee.Champs.ToString(), StringComparison.Ordinal);
    }

    // Une chaîne JSON qui ne porte ni guillemet ni antislash : ses caractères y sont écrits tels quels.
    private static string Chaine(string valeur) => "\"" + valeur + "\"";
}
