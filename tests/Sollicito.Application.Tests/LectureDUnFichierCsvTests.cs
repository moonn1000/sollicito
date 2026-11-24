using System.Text;
using Sollicito.Application.Offres;

namespace Sollicito.Application.Tests;

/// <summary>
/// La lecture d'un fichier CSV d'offres (#40, ADR-0015 ; <c>docs/import-csv.md</c>) : l'encodage, le séparateur, les
/// guillemets, les fins de ligne, l'en-tête, les lignes et les bornes. Un refus ne cite jamais une cellule. Toutes les
/// données sont fictives.
/// </summary>
public sealed class LectureDUnFichierCsvTests
{
    private const string Fictif = "Dupont-Fictif";

    [Fact]
    public void Un_fichier_utf8_se_lit_de_meme_avec_ou_sans_bom()
    {
        var sans = Lu("poste;entreprise\nDéveloppeur;Lumière Verte\n");
        var avec = Lu([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("poste;entreprise\nDéveloppeur;Lumière Verte\n")]);

        Assert.Equal(Cellules(sans), Cellules(avec));
        Assert.Equal("Développeur", Assert.Single(avec.Lignes).Cellules![ColonneDuFichier.Poste]);
    }

    [Fact]
    public void Un_fichier_windows_1252_accentue_est_refuse_comme_tout_autre_encodage()
    {
        // « Développeur » en Windows-1252 : l'octet E9 seul n'est pas de l'UTF-8.
        byte[] ansi = [.. Encoding.ASCII.GetBytes("poste\nD"), 0xE9, .. Encoding.ASCII.GetBytes("veloppeur\n")];

        AssertRefus(LectureDUnFichierCsv.Lire(ansi), SorteDeRefusDuFichier.Encodage, LectureDUnFichierCsv.MessageEncodage);
    }

    [Fact]
    public void Un_fichier_utf16_est_refuse_quel_que_soit_son_bom()
    {
        AssertRefus(LectureDUnFichierCsv.Lire(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("poste\nComptable\n")).ToArray()), SorteDeRefusDuFichier.Encodage, LectureDUnFichierCsv.MessageEncodage);
        AssertRefus(LectureDUnFichierCsv.Lire(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("poste\nComptable\n")).ToArray()), SorteDeRefusDuFichier.Encodage, LectureDUnFichierCsv.MessageEncodage);
    }

    [Fact]
    public void Un_fichier_windows_1252_sans_accent_passe_ses_octets_etant_de_l_utf8()
    {
        var fichier = Lu(Encoding.ASCII.GetBytes("poste\nComptable\n"));

        Assert.Equal("Comptable", Assert.Single(fichier.Lignes).Cellules![ColonneDuFichier.Poste]);
    }

    [Theory]
    [InlineData(";")]
    [InlineData(",")]
    public void Le_separateur_se_lit_sur_l_en_tete(string separateur)
    {
        var fichier = Lu($"poste{separateur}lieu\nComptable{separateur}Liège\n");

        var ligne = Assert.Single(fichier.Lignes);
        Assert.Equal("Comptable", ligne.Cellules![ColonneDuFichier.Poste]);
        Assert.Equal("Liège", ligne.Cellules[ColonneDuFichier.Lieu]);
    }

    [Fact]
    public void Un_en_tete_qui_porte_les_deux_separateurs_est_refuse()
    {
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste;lieu,texte\nComptable;Liège,x\n")), SorteDeRefusDuFichier.EnTete, LectureDUnFichierCsv.MessageDeuxSeparateurs);
    }

    [Fact]
    public void Un_en_tete_d_une_seule_colonne_n_a_pas_de_separateur()
    {
        var fichier = Lu("poste\nComptable; senior, Liège\n");

        Assert.Equal("Comptable; senior, Liège", Assert.Single(fichier.Lignes).Cellules![ColonneDuFichier.Poste]);
    }

    [Fact]
    public void Une_cellule_entre_guillemets_porte_separateurs_guillemets_doubles_et_sauts_de_ligne_tels_quels()
    {
        var fichier = Lu("poste;texte;lieu\n\"Comptable; senior\";\"Première ligne\r\n« citée » \"\"ici\"\"\nfin \";Liège\nChauffeur;;Namur\n");

        Assert.Equal(2, fichier.Lignes.Count);
        var premiere = fichier.Lignes[0];
        Assert.Equal(2, premiere.Numero);
        Assert.Equal("Comptable; senior", premiere.Cellules![ColonneDuFichier.Poste]);
        Assert.Equal("Première ligne\r\n« citée » \"ici\"\nfin ", premiere.Cellules[ColonneDuFichier.Texte]);
        // Une cellule sur plusieurs lignes ne décale pas la suite : la ligne suivante est la 3, comme dans un tableur.
        Assert.Equal(3, fichier.Lignes[1].Numero);
    }

    [Theory]
    [InlineData("poste;lieu\nCompt\"able;Liège\n", 2)]
    [InlineData("poste;lieu\nComptable;Liège\n\"Chauffeur\" en chef;Namur\n", 3)]
    public void Un_guillemet_mal_place_refuse_le_fichier_avec_sa_ligne(string texte, int ligne)
    {
        AssertRefus(LectureDUnFichierCsv.Lire(Octets(texte)), SorteDeRefusDuFichier.Structure, LectureDUnFichierCsv.MessageGuillemetMalPlace(ligne));
    }

    [Fact]
    public void Un_guillemet_jamais_ferme_refuse_le_fichier_avec_la_ligne_ou_il_s_ouvre()
    {
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste;lieu\nComptable;Liège\n\"Chauffeur;Namur\nMagasinier;Huy\n")), SorteDeRefusDuFichier.Structure, LectureDUnFichierCsv.MessageGuillemetNonFerme(3));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Les_fins_de_ligne_crlf_lf_et_cr_seul_se_lisent_et_la_derniere_est_facultative(string fin)
    {
        var avecLaDerniere = Lu($"poste{fin}Comptable{fin}Chauffeur{fin}");
        var sansLaDerniere = Lu($"poste{fin}Comptable{fin}Chauffeur");

        Assert.Equal([2, 3], avecLaDerniere.Lignes.Select(ligne => ligne.Numero));
        Assert.Equal(Cellules(avecLaDerniere), Cellules(sansLaDerniere));
        Assert.Equal(0, avecLaDerniere.LignesVides);
    }

    [Fact]
    public void Les_noms_de_l_en_tete_se_lisent_sans_la_casse_ni_les_blancs_de_bord_et_entre_guillemets()
    {
        var fichier = Lu("\" POSTE \";Entreprise;PUBLIEELE;  adresse \nComptable;Lumière Verte;18/11/2026;https://exemple.invalid/\n");

        Assert.Equal([ColonneDuFichier.Poste, ColonneDuFichier.Entreprise, ColonneDuFichier.PublieeLe, ColonneDuFichier.Adresse], fichier.Colonnes);
    }

    [Fact]
    public void Une_colonne_inconnue_vide_ou_repetee_refuse_le_fichier_par_son_rang_jamais_par_son_nom()
    {
        AssertRefus(LectureDUnFichierCsv.Lire(Octets($"poste;{Fictif}\nComptable;x\n")), SorteDeRefusDuFichier.EnTete, LectureDUnFichierCsv.MessageColonneInconnue(2));
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste;;lieu\nComptable;;Liège\n")), SorteDeRefusDuFichier.EnTete, LectureDUnFichierCsv.MessageColonneVide(2));
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste;lieu;Poste\nComptable;Liège;x\n")), SorteDeRefusDuFichier.EnTete, LectureDUnFichierCsv.MessageColonneRepetee(3));
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("entreprise;lieu\nLumière Verte;Liège\n")), SorteDeRefusDuFichier.EnTete, LectureDUnFichierCsv.MessagePosteManquant);
    }

    [Fact]
    public void Une_ligne_vide_ou_sans_cellule_remplie_est_ignoree_et_comptee()
    {
        var fichier = Lu("poste;lieu\n\nComptable;Liège\n;\n  ;  \nChauffeur;Namur\n");

        Assert.Equal([3, 6], fichier.Lignes.Select(ligne => ligne.Numero));
        Assert.Equal(3, fichier.LignesVides);
    }

    [Fact]
    public void Une_ligne_qui_n_a_pas_autant_de_cellules_que_l_en_tete_est_lue_sans_cellules()
    {
        var fichier = Lu("poste;lieu\nComptable\nChauffeur;Namur;en trop\nMagasinier;Huy\n");

        Assert.All(fichier.Lignes.Take(2), ligne => Assert.Null(ligne.Cellules));
        Assert.NotNull(fichier.Lignes[2].Cellules);
    }

    [Fact]
    public void Un_fichier_vide_ou_sans_ligne_d_offre_est_refuse()
    {
        AssertRefus(LectureDUnFichierCsv.Lire([]), SorteDeRefusDuFichier.Vide, LectureDUnFichierCsv.MessageVide);
        AssertRefus(LectureDUnFichierCsv.Lire([0xEF, 0xBB, 0xBF]), SorteDeRefusDuFichier.Vide, LectureDUnFichierCsv.MessageVide);
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste;lieu\n")), SorteDeRefusDuFichier.AucuneOffre, LectureDUnFichierCsv.MessageAucuneOffre);
        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste;lieu\n\n;\n")), SorteDeRefusDuFichier.AucuneOffre, LectureDUnFichierCsv.MessageAucuneOffre);
    }

    [Fact]
    public void Un_fichier_de_plus_de_2_mo_est_refuse_avant_d_etre_lu()
    {
        var juste = Octets("poste;texte\n" + string.Concat(Enumerable.Repeat("Comptable;" + new string('a', 49_000) + "\n", 42)));
        Assert.True(juste.Length <= LectureDUnFichierCsv.TailleMaximale);
        Assert.IsType<LectureDuFichier.Lu>(LectureDUnFichierCsv.Lire(juste));

        AssertRefus(LectureDUnFichierCsv.Lire(new byte[LectureDUnFichierCsv.TailleMaximale + 1]), SorteDeRefusDuFichier.Taille, LectureDUnFichierCsv.MessageTaille);
    }

    [Fact]
    public void Mille_lignes_sous_l_en_tete_passent_et_une_de_plus_refuse_le_fichier_lignes_vides_comprises()
    {
        var mille = Lu("poste\n" + string.Concat(Enumerable.Repeat("Comptable\n", 999)) + "\n");
        Assert.Equal(999, mille.Lignes.Count);
        Assert.Equal(1, mille.LignesVides);

        AssertRefus(LectureDUnFichierCsv.Lire(Octets("poste\n" + string.Concat(Enumerable.Repeat("Comptable\n", 1_000)) + "\nComptable\n")), SorteDeRefusDuFichier.Lignes, LectureDUnFichierCsv.MessageLignes);
    }

    [Theory]
    [InlineData("poste", 200, ColonneDuFichier.Poste)]
    [InlineData("entreprise", 200, ColonneDuFichier.Entreprise)]
    [InlineData("lieu", 200, ColonneDuFichier.Lieu)]
    [InlineData("adresse", 2_000, ColonneDuFichier.Adresse)]
    [InlineData("texte", 50_000, ColonneDuFichier.Texte)]
    public void Une_cellule_au_dela_de_la_borne_de_son_champ_refuse_tout_le_fichier(string colonne, int borne, ColonneDuFichier attendue)
    {
        var enTete = colonne == "poste" ? "poste" : "poste;" + colonne;
        string Ligne(string cellule) => colonne == "poste" ? $"\"{cellule}\"" : $"Comptable;\"{cellule}\"";

        // À la borne, blancs de bord en plus, la cellule passe ; au-delà, le fichier est refusé, avec la ligne et la colonne.
        Lu($"{enTete}\n{Ligne("  " + new string('a', borne) + "  ")}\n");
        AssertRefus(
            LectureDUnFichierCsv.Lire(Octets($"{enTete}\nChauffeur{(colonne == "poste" ? "" : ";")}\n{Ligne(new string('a', borne + 1))}\n")),
            SorteDeRefusDuFichier.Borne,
            LectureDUnFichierCsv.MessageBorne(3, attendue));
    }

    [Fact]
    public void Le_texte_se_mesure_fins_de_ligne_ramenees_comme_le_fait_sa_regle()
    {
        // Chaque CRLF compte un caractère, comme dans la règle du texte : 49 999 en tout, sous la borne, quand le fichier en
        // porte 75 998.
        Lu("poste;texte\nComptable;\"" + string.Concat(Enumerable.Repeat("a\r\n", 24_999)) + "a\"\n");
    }

    [Fact]
    public void Rien_n_est_interprete_une_formule_reste_le_texte_de_sa_cellule()
    {
        var fichier = Lu("poste;lieu;entreprise\n=1+1;+32 2 000 00 00;@SOMME(A1)\n-Comptable;＠x;\"=HYPERLINK(\"\"http://exemple.invalid\"\")\"\n");

        Assert.Equal(["=1+1", "-Comptable"], fichier.Lignes.Select(ligne => ligne.Cellules![ColonneDuFichier.Poste]));
        Assert.Equal("＠x", fichier.Lignes[1].Cellules![ColonneDuFichier.Lieu]);
        Assert.Equal("=HYPERLINK(\"http://exemple.invalid\")", fichier.Lignes[1].Cellules![ColonneDuFichier.Entreprise]);
    }

    private static FichierCsv Lu(string texte) => Lu(Octets(texte));

    private static FichierCsv Lu(byte[] octets)
    {
        var lecture = LectureDUnFichierCsv.Lire(octets);
        if (lecture is LectureDuFichier.Refuse refus)
        {
            Assert.Fail($"Fichier refusé : {refus.Message}");
        }

        return Assert.IsType<LectureDuFichier.Lu>(lecture).Fichier;
    }

    private static byte[] Octets(string texte) => Encoding.UTF8.GetBytes(texte);

    private static IEnumerable<IReadOnlyDictionary<ColonneDuFichier, string>?> Cellules(FichierCsv fichier) => fichier.Lignes.Select(ligne => ligne.Cellules);

    private static void AssertRefus(LectureDuFichier lecture, SorteDeRefusDuFichier sorte, string message)
    {
        var refus = Assert.IsType<LectureDuFichier.Refuse>(lecture);
        Assert.Equal(sorte, refus.Sorte);
        Assert.Equal(message, refus.Message);
        Assert.DoesNotContain(Fictif, refus.Message, StringComparison.OrdinalIgnoreCase);
    }
}
