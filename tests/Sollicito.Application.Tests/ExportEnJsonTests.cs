using System.Text;
using System.Text.Json;
using Sollicito.Application.Export;

namespace Sollicito.Application.Tests;

/// <summary>
/// L'écrivain de l'export JSON (#49, ADR-0020), sur un instantané factice : la forme du document, l'ordre des champs, les
/// valeurs absentes, les instants, les champs de chaque sorte d'événement, et une écriture qui suit la lecture sans tenir
/// une table en mémoire. Toutes les données sont fictives.
/// </summary>
public sealed class ExportEnJsonTests
{
    private static readonly DateTime Le27 = new(2026, 11, 27, 13, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_document_porte_son_format_puis_chaque_entite_dans_l_ordre_des_liens_et_chaque_valeur_telle_quelle()
    {
        var instantane = new InstantaneFactice
        {
            Entreprises = [new(1, "Atelier Éole & Fils", "atelier eole fils")],
            Contacts = [new(2, 1, "Zoé Lefèvre", "zoe@exemple.invalid")],
            Offres =
            [
                new(3, "FichierCsv", "ab12", null, "=SOMME(A1)", "somme a1", null, new DateOnly(2026, 11, 1), null, "Ligne 1\nLigne 2\t« fin » \"droits\" <b> + \\ 🙂", Instant(2026, 11, 18, 13, 31, 0, 1_234_567)),
            ],
            Candidatures = [new(4, 1, 3, "Comptable", Instant(2026, 11, 2, 8, 15, 0, 0), "Envoyee")],
            Relances = [],
            Evenements =
            [
                new(5, 4, "ChangementDeStatut", Instant(2026, 11, 18, 13, 36, 0, 1_000_000), "Envoyee", "Entretien", null, null, null, null, null),
                new(6, 4, "Etape", Instant(2026, 11, 10, 13, 0, 0, 0), null, null, null, "Appel", null, 7, null),
                new(7, 4, "ClotureDeRelance", Instant(2026, 11, 18, 14, 0, 0, 0), null, null, null, null, null, null, 8),
            ],
        };
        using var flux = new MemoryStream();

        var comptes = await EcrivainJson.EcrireAsync(instantane, flux, Le27, etapes: null, Annulation);

        Assert.Equal(new ComptesDeLExport(1, 1, 1, 1, 0, 3), comptes);
        var octets = flux.ToArray();
        // UTF-8 sans BOM, et des fins de ligne LF.
        Assert.Equal((byte)'{', octets[0]);
        Assert.Equal(
            """
            {
              "format": "sollicito-export",
              "version": 1,
              "exporteLe": "2026-11-27T13:00:00Z",
              "entreprises": [
                {
                  "id": 1,
                  "nom": "Atelier Éole & Fils",
                  "cle": "atelier eole fils"
                }
              ],
              "contacts": [
                {
                  "id": 2,
                  "entrepriseId": 1,
                  "nom": "Zoé Lefèvre",
                  "email": "zoe@exemple.invalid"
                }
              ],
              "offres": [
                {
                  "id": 3,
                  "source": "FichierCsv",
                  "entrepriseId": null,
                  "poste": "=SOMME(A1)",
                  "posteNormalise": "somme a1",
                  "lieu": null,
                  "publieeLe": "2026-11-01",
                  "adresse": null,
                  "texte": "Ligne 1\nLigne 2\t« fin » \"droits\" <b> + \\ \uD83D\uDE42",
                  "empreinte": "ab12",
                  "ajouteeLe": "2026-11-18T13:31:00.1234567Z"
                }
              ],
              "candidatures": [
                {
                  "id": 4,
                  "entrepriseId": 1,
                  "offreId": 3,
                  "poste": "Comptable",
                  "envoyeeLe": "2026-11-02T08:15:00Z",
                  "statut": "Envoyee"
                }
              ],
              "relances": [],
              "evenements": [
                {
                  "id": 5,
                  "candidatureId": 4,
                  "sorte": "ChangementDeStatut",
                  "survenuLe": "2026-11-18T13:36:00.1Z",
                  "ancien": "Envoyee",
                  "nouveau": "Entretien",
                  "corrigeId": null
                },
                {
                  "id": 6,
                  "candidatureId": 4,
                  "sorte": "Etape",
                  "survenuLe": "2026-11-10T13:00:00Z",
                  "type": "Appel",
                  "note": null,
                  "corrigeId": 7
                },
                {
                  "id": 7,
                  "candidatureId": 4,
                  "sorte": "ClotureDeRelance",
                  "survenuLe": "2026-11-18T14:00:00Z",
                  "relanceId": 8
                }
              ]
            }
            """.ReplaceLineEndings("\n"),
            Encoding.UTF8.GetString(octets));

        // Relu, le texte de l'offre est celui de la base, au caractère près.
        using var relu = JsonDocument.Parse(octets);
        Assert.Equal(instantane.Offres[0].Texte, relu.RootElement.GetProperty("offres")[0].GetProperty("texte").GetString());
    }

    [Fact]
    public async Task Une_relance_et_ses_valeurs_absentes_s_ecrivent_et_chaque_table_ecrite_se_signale()
    {
        var instantane = new InstantaneFactice
        {
            Relances = [new(1, 2, null, new DateOnly(2026, 11, 23), "AFaire"), new(2, 2, 3, new DateOnly(2026, 11, 9), "Faite")],
        };
        var vues = new List<EtapeDeLExport>();
        using var flux = new MemoryStream();

        await EcrivainJson.EcrireAsync(instantane, flux, Le27, vues.Add, Annulation);

        using var relu = JsonDocument.Parse(flux.ToArray());
        var relances = relu.RootElement.GetProperty("relances");
        Assert.Equal(JsonValueKind.Null, relances[0].GetProperty("destinataireId").ValueKind);
        Assert.Equal("2026-11-23", relances[0].GetProperty("echeance").GetString());
        Assert.Equal(3, relances[1].GetProperty("destinataireId").GetInt32());
        Assert.Equal(
            [
                EtapeDeLExport.EntreprisesEcrites, EtapeDeLExport.ContactsEcrits, EtapeDeLExport.OffresEcrites,
                EtapeDeLExport.CandidaturesEcrites, EtapeDeLExport.RelancesEcrites, EtapeDeLExport.EvenementsEcrits,
            ],
            vues);
    }

    [Fact]
    public async Task Une_sorte_d_evenement_que_Sollicito_n_ecrit_pas_fait_echouer_l_export()
    {
        var instantane = new InstantaneFactice
        {
            Evenements = [new(1, 1, "Inconnue", Instant(2026, 11, 18, 13, 0, 0, 0), null, null, null, null, null, null, null)],
        };
        using var flux = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => EcrivainJson.EcrireAsync(instantane, flux, Le27, etapes: null, Annulation));
    }

    [Fact]
    public async Task L_ecriture_suit_la_lecture_par_un_tampon_borne_sans_tenir_une_table_en_memoire()
    {
        // 300 offres de 50 000 caractères, la borne du texte d'une offre : 15 Mo de texte, lus ligne à ligne.
        const int Nombre = 300;
        var texte = new string('a', 50_000);
        var lues = 0;
        var instantane = new InstantaneFactice
        {
            LectureDesOffres = () => Offres(Nombre, texte, () => lues++),
        };
        using var flux = new FluxQuiNoteSesEcritures(() => lues);

        var comptes = await EcrivainJson.EcrireAsync(instantane, flux, Le27, etapes: null, Annulation);

        Assert.Equal(Nombre, comptes.Offres);
        Assert.NotEmpty(flux.Ecritures);
        // La première écriture tombe bien avant la dernière lecture : rien n'attend la fin de la table.
        Assert.True(flux.Ecritures[0].LignesLues < Nombre / 2, $"Première écriture après {flux.Ecritures[0].LignesLues} offres lues.");
        // Aucune écriture ne dépasse le tampon et une ligne.
        Assert.All(flux.Ecritures, ecriture => Assert.True(ecriture.Octets <= FormatDeLExport.Tampon + 51_000, $"Une écriture de {ecriture.Octets} octets."));
    }

    [Theory]
    [InlineData(0, "2026-11-02T08:15:00Z")]
    [InlineData(1_000_000, "2026-11-02T08:15:00.1Z")]
    [InlineData(1_234_567, "2026-11-02T08:15:00.1234567Z")]
    [InlineData(1_230_000, "2026-11-02T08:15:00.123Z")]
    public void Un_instant_s_ecrit_en_ISO_8601_UTC_sa_fraction_de_seconde_seulement_si_elle_n_est_pas_nulle(int ticks, string attendu)
    {
        Assert.Equal(attendu, FormatDeLExport.Instant(Instant(2026, 11, 2, 8, 15, 0, ticks)));
    }

    [Fact]
    public void Un_instant_qui_n_est_pas_en_UTC_est_refuse_et_les_noms_et_extensions_suivent_le_format()
    {
        Assert.Throws<ArgumentException>(() => FormatDeLExport.Instant(new DateTime(2026, 11, 2, 8, 15, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => FormatDeLExport.Instant(new DateTime(2026, 11, 2, 8, 15, 0, DateTimeKind.Unspecified)));
        Assert.Equal("2026-11-09", FormatDeLExport.Date(new DateOnly(2026, 11, 9)));
        Assert.Equal("sollicito-export-2026-01-05.json", FormatDeLExport.NomPropose(FormatDExport.Json, new DateOnly(2026, 1, 5)));
        Assert.True(FormatDeLExport.AExtensionDe(FormatDExport.Json, @"C:\Export\sollicito.JSON"));
        Assert.False(FormatDeLExport.AExtensionDe(FormatDExport.Json, @"C:\Export\sollicito.json.txt"));
        Assert.False(FormatDeLExport.AExtensionDe(FormatDExport.Json, @"C:\Export\sollicito"));
    }

    private static DateTime Instant(int annee, int mois, int jour, int heure, int minute, int seconde, int ticks) =>
        new DateTime(annee, mois, jour, heure, minute, seconde, DateTimeKind.Utc).AddTicks(ticks);

    private static async IAsyncEnumerable<OffreExportee> Offres(int nombre, string texte, Action lue)
    {
        for (var id = 1; id <= nombre; id++)
        {
            await Task.Yield();
            lue();
            yield return new OffreExportee(id, "TexteColle", "ab", null, "Poste", "poste", null, null, null, texte, Instant(2026, 11, 18, 13, 0, 0, 0));
        }
    }
}
