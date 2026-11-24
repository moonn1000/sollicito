using System.Text;
using Sollicito.Application.Offres;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le banc de tests de contrat des connecteurs d'offres (ADR-0001, D6 et D11 ; #34, absorbée par #36 ; ADR-0015) : chaque
/// adaptateur en hérite, et le passe sur des entrées fictives. Un connecteur ne valide rien, c'est l'affaire du cœur ; il
/// donne sa source à chaque offre, annonce les champs de son empreinte, et passe ses champs tels qu'il les a reçus : ni
/// retouchés, ni lus comme du HTML. Le pas de lecture du cœur (<see cref="LectureDUneOffre"/>) juge ensuite ce qu'il en
/// fait (#98, point 16).
/// </summary>
/// <typeparam name="TEntree">Ce que la source donne au connecteur.</typeparam>
public abstract class ContratDUnConnecteurDOffres<TEntree>
{
    /// <summary>Aujourd'hui, pour le pas de lecture.</summary>
    protected static readonly DateOnly Aujourdhui = new(2026, 11, 24);

    /// <summary>Des champs piégés : balises, script, entités, accents, blancs de bord, sauts de ligne, ponctuation.</summary>
    public static readonly ChampsRecus Pieges = new(
        "  <h1>Offre</h1>\r\n<script>alert('fictif')</script>\n&amp; « Œuvres » \t",
        " <img src=x onerror=alert(1)> SA ",
        "Développeur <b>.NET</b> (H/F)",
        "Liège & Namur",
        "2026-11-18",
        "javascript:alert('fictif')");

    /// <summary>Les mêmes champs piégés, que le cœur admet : l'adresse y est une adresse web.</summary>
    public static readonly ChampsRecus PiegesAdmis = Pieges with { Adresse = "https://exemple.invalid/offre?id=1" };

    /// <summary>Le connecteur essayé.</summary>
    protected abstract IConnecteurDOffres<TEntree> Connecteur { get; }

    /// <summary>La source qu'il doit donner.</summary>
    protected abstract SourceDOffre SourceAttendue { get; }

    /// <summary>Une entrée de la source qui porte ces champs, pour une seule offre.</summary>
    protected abstract TEntree EntreeQuiPorte(ChampsRecus champs);

    /// <summary>Les champs de l'empreinte que le connecteur doit annoncer.</summary>
    protected abstract IReadOnlyList<ChampDeLEmpreinte> ChampsDeLEmpreinteAttendus { get; }

    /// <summary>Une entrée vide : rien de saisi, ou une ligne sans aucune cellule remplie.</summary>
    protected abstract TEntree EntreeVide();

    [Fact]
    public void Chaque_offre_recue_porte_la_source_du_connecteur()
    {
        var offres = Connecteur.Recevoir(EntreeQuiPorte(Pieges));

        Assert.Equal(SourceAttendue, Connecteur.Source);
        Assert.NotEmpty(offres);
        Assert.All(offres, offre => Assert.Equal(SourceAttendue, offre.Source));
    }

    [Fact]
    public void Les_champs_passent_tels_quels_ni_retouches_ni_lus_comme_du_html()
    {
        var offre = Assert.Single(Connecteur.Recevoir(EntreeQuiPorte(Pieges)));

        Assert.Equal(Pieges, offre.Champs);
    }

    [Fact]
    public void Les_champs_de_l_empreinte_sont_ceux_du_format_de_la_source()
    {
        var offre = Assert.Single(Connecteur.Recevoir(EntreeQuiPorte(Pieges)));

        Assert.Equal(ChampsDeLEmpreinteAttendus, offre.ChampsDeLEmpreinte);
    }

    [Fact]
    public void Le_coeur_calcule_l_empreinte_sur_les_champs_annonces_tels_qu_il_les_a_lus()
    {
        var offre = Assert.Single(Connecteur.Recevoir(EntreeQuiPorte(PiegesAdmis)));

        Assert.True(LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out var admise, out var refus), string.Join(", ", refus.Keys));
        var lus = new Dictionary<ChampDeLEmpreinte, string>
        {
            [ChampDeLEmpreinte.Texte] = admise.Texte ?? "",
            [ChampDeLEmpreinte.Poste] = admise.Champs.Poste,
            [ChampDeLEmpreinte.Entreprise] = admise.NomDeLEntreprise ?? "",
            [ChampDeLEmpreinte.Lieu] = admise.Champs.Lieu ?? "",
            [ChampDeLEmpreinte.PublieeLe] = admise.Champs.PublieeLe is { } date ? DateDePublication.FormeCanonique(date) : "",
            [ChampDeLEmpreinte.Adresse] = admise.Champs.Adresse ?? "",
        };
        Assert.Equal(EmpreinteDUneOffre.De([.. ChampsDeLEmpreinteAttendus.Select(champ => lus[champ])]), admise.Empreinte);
    }

    [Fact]
    public void Une_entree_vide_ne_fait_aucune_offre_que_le_coeur_accepte()
    {
        var offres = Connecteur.Recevoir(EntreeVide());

        Assert.All(offres, offre =>
        {
            Assert.Equal(SourceAttendue, offre.Source);
            Assert.False(LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out var admise, out var refus));
            Assert.Null(admise);
            Assert.NotEmpty(refus);
        });
    }
}

/// <summary>Le premier connecteur, l'offre collée (#36), au banc de contrat.</summary>
public sealed class ConnecteurDuTexteColleTests : ContratDUnConnecteurDOffres<SaisieDUneOffre>
{
    protected override IConnecteurDOffres<SaisieDUneOffre> Connecteur { get; } = new ConnecteurDuTexteColle();

    protected override SourceDOffre SourceAttendue => SourceDOffre.TexteColle;

    protected override SaisieDUneOffre EntreeQuiPorte(ChampsRecus champs) =>
        new(champs.Texte, champs.Entreprise, champs.Poste, champs.Lieu, champs.PublieeLe, champs.Adresse);

    // Un seul champ : le texte collé.
    protected override IReadOnlyList<ChampDeLEmpreinte> ChampsDeLEmpreinteAttendus => [ChampDeLEmpreinte.Texte];

    protected override SaisieDUneOffre EntreeVide() => new(null, null, null, null, null, null);

    [Fact]
    public void L_accord_garder_les_deux_n_entre_pas_dans_l_offre_recue()
    {
        var offre = Assert.Single(Connecteur.Recevoir(new SaisieDUneOffre("Offre fictive.", null, "Comptable", null, null, null, GarderLesDeux: true)));

        Assert.Equal(new ChampsRecus("Offre fictive.", null, "Comptable", null, null, null), offre.Champs);
    }

    [Fact]
    public void Un_texte_absent_est_refuse_par_le_coeur_qui_l_exige()
    {
        var offre = Assert.Single(Connecteur.Recevoir(new SaisieDUneOffre(null, null, "Comptable", null, null, null)));

        Assert.Null(offre.Champs.Texte);
        Assert.False(LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out _, out var refus));
        Assert.Equal([TexteDUneOffre.Manquant], refus[SaisieDUneOffre.Champs.Texte]);
    }

    [Fact]
    public void L_empreinte_d_une_offre_collee_reste_celle_de_son_texte_tel_que_colle()
    {
        // Le cœur prend désormais le texte lu (#40) : l'empreinte des offres déjà enregistrées par #36 ne change pas.
        const string colle = "  Développeur .NET\r\n\r\nBruxelles\u2028Temps plein\f ";
        var offre = Assert.Single(Connecteur.Recevoir(new SaisieDUneOffre(colle, null, "Comptable", null, null, null)));

        Assert.True(LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out var admise, out _));
        Assert.Equal(EmpreinteDUneOffre.De([colle]), admise.Empreinte);
    }
}

/// <summary>Le second connecteur, le fichier CSV (#40), au banc de contrat.</summary>
public sealed class ConnecteurDuFichierCsvTests : ContratDUnConnecteurDOffres<FichierCsv>
{
    private static readonly ConnecteurDuFichierCsv LeConnecteur = new();

    protected override IConnecteurDOffres<FichierCsv> Connecteur => LeConnecteur;

    protected override SourceDOffre SourceAttendue => SourceDOffre.FichierCsv;

    // Un vrai fichier, lu par le lecteur : chaque cellule entre guillemets, qui gardent blancs de bord et sauts de ligne.
    protected override FichierCsv EntreeQuiPorte(ChampsRecus champs) =>
        Lire("poste;entreprise;lieu;publieeLe;adresse;texte\r\n"
            + string.Join(';', new[] { champs.Poste, champs.Entreprise, champs.Lieu, champs.PublieeLe, champs.Adresse, champs.Texte }.Select(Entre)) + "\r\n");

    // Les six champs du format, dans son ordre.
    protected override IReadOnlyList<ChampDeLEmpreinte> ChampsDeLEmpreinteAttendus =>
        [ChampDeLEmpreinte.Poste, ChampDeLEmpreinte.Entreprise, ChampDeLEmpreinte.Lieu, ChampDeLEmpreinte.PublieeLe, ChampDeLEmpreinte.Adresse, ChampDeLEmpreinte.Texte];

    // Une ligne dont aucune cellule n'est remplie : le lecteur l'ignore, et le cœur la refuserait.
    protected override FichierCsv EntreeVide() =>
        new([ColonneDuFichier.Poste], [new LigneDuFichier(2, new Dictionary<ColonneDuFichier, string> { [ColonneDuFichier.Poste] = "" })], 0);

    [Fact]
    public void L_ordre_des_colonnes_du_fichier_ne_change_ni_les_champs_ni_l_empreinte()
    {
        var dansLOrdre = Assert.Single(LeConnecteur.Recevoir(Lire("poste;entreprise;lieu\nComptable;Lumière Verte;Liège\n")));
        var melange = Assert.Single(LeConnecteur.Recevoir(Lire("lieu;poste;entreprise\nLiège;Comptable;Lumière Verte\n")));

        Assert.Equal(dansLOrdre, melange);
        Assert.True(LectureDUneOffre.EssayerDeLire(dansLOrdre, Aujourdhui, out var premiere, out _));
        Assert.True(LectureDUneOffre.EssayerDeLire(melange, Aujourdhui, out var seconde, out _));
        Assert.Equal(premiere.Empreinte, seconde.Empreinte);
    }

    [Fact]
    public void Une_colonne_absente_donne_un_champ_absent()
    {
        var offre = Assert.Single(LeConnecteur.Recevoir(Lire("poste\nComptable\n")));

        Assert.Equal(new ChampsRecus(null, null, "Comptable", null, null, null), offre.Champs);
    }

    [Fact]
    public void Une_ligne_mal_formee_ne_fait_aucune_offre_et_les_autres_gardent_leur_numero()
    {
        var lignes = LeConnecteur.RecevoirLesLignes(Lire("poste;lieu\nComptable;Liège\nChauffeur\n\nMagasinier;Namur\n"));

        Assert.Equal([2, 5], lignes.Select(ligne => ligne.Numero));
        Assert.Equal(["Comptable", "Magasinier"], lignes.Select(ligne => ligne.Offre.Champs.Poste));
    }

    [Fact]
    public void Une_meme_ligne_datee_sous_ses_deux_formes_n_a_qu_une_empreinte()
    {
        var iso = Assert.Single(LeConnecteur.Recevoir(Lire("poste;publieeLe\nComptable;2026-11-08\n")));
        var jourEnPremier = Assert.Single(LeConnecteur.Recevoir(Lire("poste;publieeLe\nComptable;8/11/2026\n")));

        Assert.Equal("8/11/2026", jourEnPremier.Champs.PublieeLe);
        Assert.True(LectureDUneOffre.EssayerDeLire(iso, Aujourdhui, out var premiere, out _));
        Assert.True(LectureDUneOffre.EssayerDeLire(jourEnPremier, Aujourdhui, out var seconde, out _));
        Assert.Equal(premiere.Empreinte, seconde.Empreinte);
        Assert.Equal(new DateOnly(2026, 11, 8), seconde.Champs.PublieeLe);
    }

    [Fact]
    public void Une_cellule_texte_vide_fait_une_offre_sans_texte()
    {
        var offre = Assert.Single(LeConnecteur.Recevoir(Lire("poste;texte\nComptable;   \n")));

        Assert.Equal("   ", offre.Champs.Texte);
        Assert.True(LectureDUneOffre.EssayerDeLire(offre, Aujourdhui, out var admise, out _));
        Assert.Null(admise.Texte);
    }

    private static string Entre(string? cellule) => "\"" + (cellule ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static FichierCsv Lire(string texte) =>
        Assert.IsType<LectureDuFichier.Lu>(LectureDUnFichierCsv.Lire(Encoding.UTF8.GetBytes(texte))).Fichier;
}
