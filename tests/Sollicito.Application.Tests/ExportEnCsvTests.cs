using System.IO.Compression;
using System.Text;
using Sollicito.Application.Export;

namespace Sollicito.Application.Tests;

/// <summary>
/// L'écrivain de l'export CSV (#49, ADR-0020), sur un instantané factice : la neutralisation des onze débuts d'OWASP, les
/// guillemets de la RFC 4180, l'archive et ses fichiers, l'UTF-8 avec BOM, le séparateur, les fins de ligne, et une
/// écriture qui suit la lecture. Toutes les données sont fictives.
/// </summary>
public sealed class ExportEnCsvTests
{
    private static readonly DateTime Le27 = new(2026, 11, 27, 13, 0, 0, DateTimeKind.Utc);

    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+32 4 123 45 67", "'+32 4 123 45 67")]
    [InlineData("-5", "'-5")]
    [InlineData("@SOMME(A1:A2)", "'@SOMME(A1:A2)")]
    [InlineData("\t=1+1", "'\t=1+1")]
    [InlineData("\r=1+1", "\"'\r=1+1\"")]
    [InlineData("\n=1+1", "\"'\n=1+1\"")]
    [InlineData("＝1+1", "'＝1+1")]
    [InlineData("＋1", "'＋1")]
    [InlineData("－1", "'－1")]
    [InlineData("＠SOMME(A1)", "'＠SOMME(A1)")]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    public void Une_cellule_qui_commence_comme_une_formule_recoit_une_apostrophe_en_tete(string valeur, string attendue)
    {
        Assert.Equal(attendue, EcrivainCsv.Cellule(valeur));
    }

    [Theory]
    [InlineData("Comptable", "Comptable")]
    [InlineData("a=b", "a=b")]
    [InlineData("'=déjà une apostrophe", "'=déjà une apostrophe")]
    [InlineData(" =1+1", " =1+1")]
    [InlineData("Éole & Fils « senior »", "Éole & Fils « senior »")]
    [InlineData("2026-11-02T08:15:00Z", "2026-11-02T08:15:00Z")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Une_autre_cellule_garde_sa_valeur(string? valeur, string attendue)
    {
        Assert.Equal(attendue, EcrivainCsv.Cellule(valeur));
    }

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("dit \"oui\"", "\"dit \"\"oui\"\"\"")]
    [InlineData("ligne 1\nligne 2", "\"ligne 1\nligne 2\"")]
    [InlineData("ligne 1\r\nligne 2", "\"ligne 1\r\nligne 2\"")]
    [InlineData("=1+2\";=1+2", "\"'=1+2\"\";=1+2\"")]
    [InlineData("a,b", "a,b")]
    public void Une_cellule_qui_porte_le_separateur_un_guillemet_ou_une_fin_de_ligne_s_entoure_de_guillemets(string valeur, string attendue)
    {
        Assert.Equal(attendue, EcrivainCsv.Cellule(valeur));
    }

    [Fact]
    public async Task L_archive_porte_un_fichier_par_entite_en_UTF8_avec_BOM_au_point_virgule_et_aux_fins_de_ligne_CRLF()
    {
        var instantane = new InstantaneFactice
        {
            Entreprises = [new(1, "-Moins SA", "moins"), new(2, "Atelier Éole & Fils", "atelier eole fils")],
            Contacts = [new(3, 2, "Zoé ; Lefèvre", "zoe@exemple.invalid")],
            Offres =
            [
                new(4, "FichierCsv", "ab12", null, "=SOMME(A1)", "somme a1", "＠Lieu", new DateOnly(2026, 11, 1), null, "Ligne 1\nLigne \"2\"", Le27.AddTicks(1_234_567)),
            ],
            Candidatures = [new(5, 2, 4, "Comptable", new DateTime(2026, 11, 2, 8, 15, 0, DateTimeKind.Utc), "Envoyee")],
            Relances = [new(6, 5, null, new DateOnly(2026, 11, 9), "AFaire")],
            Evenements =
            [
                new(7, 5, "ChangementDeStatut", Le27, "Envoyee", "Entretien", 8, null, null, null, null),
                new(8, 5, "Etape", Le27, null, null, null, "Appel", "+32 4 note", 9, null),
                new(9, 5, "ClotureDeRelance", Le27, null, null, null, null, null, null, 6),
            ],
        };
        using var flux = new MemoryStream();

        var comptes = await EcrivainCsv.EcrireAsync(instantane, flux, Le27, Bruxelles, etapes: null, Annulation);

        Assert.Equal(new ComptesDeLExport(2, 1, 1, 1, 1, 3), comptes);
        flux.Position = 0;
        using var archive = new ZipArchive(flux, ZipArchiveMode.Read);
        Assert.Equal(EcrivainCsv.Fichiers, archive.Entries.Select(entree => entree.FullName));
        var fichiers = archive.Entries.ToDictionary(entree => entree.FullName, Lire);
        foreach (var entree in archive.Entries)
        {
            // L'instant de l'export, à l'heure de Bruxelles : 14:00 le 27/11.
            Assert.Equal(new DateTime(2026, 11, 27, 14, 0, 0), entree.LastWriteTime.DateTime);
        }

        Assert.Equal(["format;version;exporteLe", "sollicito-export-csv;1;2026-11-27T13:00:00Z", ""], fichiers["export.csv"]);
        Assert.Equal(["id;nom;cle", "1;'-Moins SA;moins", "2;Atelier Éole & Fils;atelier eole fils", ""], fichiers["entreprises.csv"]);
        Assert.Equal(["id;entrepriseId;nom;email", "3;2;\"Zoé ; Lefèvre\";zoe@exemple.invalid", ""], fichiers["contacts.csv"]);
        Assert.Equal(
            [
                "id;source;entrepriseId;poste;posteNormalise;lieu;publieeLe;adresse;texte;empreinte;ajouteeLe",
                "4;FichierCsv;;'=SOMME(A1);somme a1;'＠Lieu;2026-11-01;;\"Ligne 1\nLigne \"\"2\"\"\";ab12;2026-11-27T13:00:00.1234567Z",
                "",
            ],
            fichiers["offres.csv"]);
        Assert.Equal(["id;entrepriseId;offreId;poste;envoyeeLe;statut", "5;2;4;Comptable;2026-11-02T08:15:00Z;Envoyee", ""], fichiers["candidatures.csv"]);
        Assert.Equal(["id;candidatureId;destinataireId;echeance;etat", "6;5;;2026-11-09;AFaire", ""], fichiers["relances.csv"]);
        Assert.Equal(
            [
                "id;candidatureId;sorte;survenuLe;ancien;nouveau;corrigeId;type;note;relanceId",
                "7;5;ChangementDeStatut;2026-11-27T13:00:00Z;Envoyee;Entretien;8;;;",
                "8;5;Etape;2026-11-27T13:00:00Z;;;9;Appel;'+32 4 note;",
                "9;5;ClotureDeRelance;2026-11-27T13:00:00Z;;;;;;6",
                "",
            ],
            fichiers["evenements.csv"]);
    }

    [Fact]
    public async Task Une_sorte_d_evenement_que_Sollicito_n_ecrit_pas_fait_echouer_l_export_CSV()
    {
        var instantane = new InstantaneFactice
        {
            Evenements = [new(1, 1, "Inconnue", Le27, null, null, null, null, null, null, null)],
        };
        using var flux = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => EcrivainCsv.EcrireAsync(instantane, flux, Le27, Bruxelles, etapes: null, Annulation));
    }

    [Fact]
    public async Task L_archive_s_ecrit_au_fil_de_la_lecture_sans_tenir_une_table_en_memoire()
    {
        // 300 offres de 50 000 lettres tirées au hasard, qui se compriment mal : 15 Mo de texte, lus ligne à ligne.
        const int Nombre = 300;
        var hasard = new Random(49);
        var texte = string.Create(50_000, hasard, (lettres, tirage) =>
        {
            for (var rang = 0; rang < lettres.Length; rang++)
            {
                lettres[rang] = (char)('a' + tirage.Next(26));
            }
        });
        var lues = 0;
        var instantane = new InstantaneFactice { LectureDesOffres = () => Offres(Nombre, texte, () => lues++) };
        using var flux = new FluxQuiNoteSesEcritures(() => lues);

        var comptes = await EcrivainCsv.EcrireAsync(instantane, flux, Le27, Bruxelles, etapes: null, Annulation);

        Assert.Equal(Nombre, comptes.Offres);
        Assert.NotEmpty(flux.Ecritures);
        Assert.True(flux.Ecritures.Exists(ecriture => ecriture.LignesLues < Nombre / 2), "Aucune écriture avant la moitié de la lecture.");
        Assert.All(flux.Ecritures, ecriture => Assert.True(ecriture.Octets <= 4 * FormatDeLExport.Tampon, $"Une écriture de {ecriture.Octets} octets."));
    }

    // Le texte d'une entrée de l'archive, coupé à chaque CRLF : ses octets commencent par le BOM de l'UTF-8.
    private static string[] Lire(ZipArchiveEntry entree)
    {
        using var lecture = entree.Open();
        using var memoire = new MemoryStream();
        lecture.CopyTo(memoire);
        var octets = memoire.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], octets[..3]);
        return Encoding.UTF8.GetString(octets, 3, octets.Length - 3).Split("\r\n");
    }

    private static async IAsyncEnumerable<OffreExportee> Offres(int nombre, string texte, Action lue)
    {
        for (var id = 1; id <= nombre; id++)
        {
            await Task.Yield();
            lue();
            yield return new OffreExportee(id, "TexteColle", "ab", null, "Poste", "poste", null, null, null, texte, Le27);
        }
    }
}
