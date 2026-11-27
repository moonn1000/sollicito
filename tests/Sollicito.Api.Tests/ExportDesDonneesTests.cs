using System.Net;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Export;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Export;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'export de toutes les données (#49, ADR-0020), par le service que la fenêtre hôte appelle en mémoire, sur une vraie base
/// SQLite dans un dossier temporaire, et vers un dossier temporaire : jamais vers un dossier réel. La base de référence a
/// été peuplée par le code de main (9ec81fa), par ses seules routes ; toutes ses données sont fictives.
/// </summary>
public sealed class ExportDesDonneesTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Le27 = new(2026, 11, 27, 13, 0, 0, TimeSpan.Zero);

    private readonly string _racine = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private string Donnees => Path.Combine(_racine, "donnees");

    private string Sortie => Path.Combine(_racine, "sortie");

    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Donnees);
        Directory.CreateDirectory(Sortie);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await SupprimerAsync(_racine);

    [Fact]
    public async Task Une_base_peuplee_par_main_s_exporte_en_entier_et_son_json_se_relit_champ_par_champ()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();
        var fichier = Path.Combine(Sortie, "sollicito-export-2026-11-27.json");

        Assert.Equal(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, fichier, remplacer: false, Annulation));

        var octets = await File.ReadAllBytesAsync(fichier, Annulation);
        // UTF-8 sans BOM.
        Assert.Equal((byte)'{', octets[0]);
        using var export = JsonDocument.Parse(octets);
        var racine = export.RootElement;
        Assert.Equal("sollicito-export", racine.GetProperty("format").GetString());
        Assert.Equal(1, racine.GetProperty("version").GetInt32());
        Assert.Equal("2026-11-27T13:00:00Z", racine.GetProperty("exporteLe").GetString());
        Assert.Equal(
            ["format", "version", "exporteLe", "entreprises", "contacts", "offres", "candidatures", "relances", "evenements"],
            racine.EnumerateObject().Select(champ => champ.Name));

        // Les comptes de la base de référence, écrits en dur.
        Assert.Equal(5, racine.GetProperty("entreprises").GetArrayLength());
        Assert.Equal(3, racine.GetProperty("contacts").GetArrayLength());
        Assert.Equal(6, racine.GetProperty("offres").GetArrayLength());
        Assert.Equal(4, racine.GetProperty("candidatures").GetArrayLength());
        Assert.Equal(3, racine.GetProperty("relances").GetArrayLength());
        Assert.Equal(9, racine.GetProperty("evenements").GetArrayLength());

        // Des valeurs écrites en dur, jamais relues par le code de l'export : accents, guillemets, sauts de ligne,
        // tabulation, émoji, débuts de formule, valeurs absentes, instants avec et sans fraction de seconde.
        var entreprise = racine.GetProperty("entreprises")[0];
        Assert.Equal(1, entreprise.GetProperty("id").GetInt32());
        Assert.Equal("Atelier Éole & Fils", entreprise.GetProperty("nom").GetString());
        Assert.Equal("atelier eole fils", entreprise.GetProperty("cle").GetString());
        Assert.Equal("＝Égal SA", racine.GetProperty("entreprises")[3].GetProperty("nom").GetString());

        var contact = racine.GetProperty("contacts")[0];
        Assert.Equal(1, contact.GetProperty("entrepriseId").GetInt32());
        Assert.Equal("Zoé Lefèvre-Dupré", contact.GetProperty("nom").GetString());
        Assert.Equal("zoe.lefevre@exemple.invalid", contact.GetProperty("email").GetString());

        var collee = racine.GetProperty("offres")[0];
        Assert.Equal("TexteColle", collee.GetProperty("source").GetString());
        Assert.Equal(1, collee.GetProperty("entrepriseId").GetInt32());
        Assert.Equal("Comptable « senior »", collee.GetProperty("poste").GetString());
        Assert.Equal("comptable senior", collee.GetProperty("posteNormalise").GetString());
        Assert.Equal("Liège", collee.GetProperty("lieu").GetString());
        Assert.Equal("2026-11-01", collee.GetProperty("publieeLe").GetString());
        Assert.Equal("https://emploi.exemple.invalid/offres/1?source=colle&id=42", collee.GetProperty("adresse").GetString());
        Assert.Equal(
            "Comptable « senior » (H/F/X)\nÀ Liège, temps plein.\n\nVous tiendrez la comptabilité ; un « + » apprécié.",
            collee.GetProperty("texte").GetString());
        Assert.Equal("313cd7fe0abfc693655dfebfde0f90b4a6babea8e0912f1cd62f4aaa7286fe10", collee.GetProperty("empreinte").GetString());
        Assert.Equal("2026-11-18T15:42:00.6234567Z", collee.GetProperty("ajouteeLe").GetString());

        var anonyme = racine.GetProperty("offres")[1];
        foreach (var absent in new[] { "entrepriseId", "lieu", "publieeLe", "adresse" })
        {
            Assert.Equal(JsonValueKind.Null, anonyme.GetProperty(absent).ValueKind);
        }

        var importee = racine.GetProperty("offres")[2];
        Assert.Equal("FichierCsv", importee.GetProperty("source").GetString());
        Assert.Equal("=Chef de projet", importee.GetProperty("poste").GetString());
        Assert.Equal("Texte importé\nsur deux lignes ; avec \"guillemets\"", importee.GetProperty("texte").GetString());
        Assert.Equal(JsonValueKind.Null, racine.GetProperty("offres")[3].GetProperty("texte").ValueKind);
        Assert.Equal("－Lieu plein", racine.GetProperty("offres")[4].GetProperty("lieu").GetString());

        var candidature = racine.GetProperty("candidatures")[0];
        Assert.Equal(JsonValueKind.Null, candidature.GetProperty("offreId").ValueKind);
        Assert.Equal("2026-11-02T08:15:00Z", candidature.GetProperty("envoyeeLe").GetString());
        Assert.Equal("Entretien", candidature.GetProperty("statut").GetString());
        Assert.Equal(1, racine.GetProperty("candidatures")[1].GetProperty("offreId").GetInt32());
        Assert.Equal("Développeur·euse .NET", racine.GetProperty("candidatures")[3].GetProperty("poste").GetString());

        Assert.Equal(
            ["1 1 1 2026-11-09 Faite", "2 1 - 2026-11-23 AFaire", "3 3 2 2026-11-12 Abandonnee"],
            racine.GetProperty("relances").EnumerateArray().Select(EnUneLigne));

        var evenements = racine.GetProperty("evenements");
        var cloture = evenements[0];
        Assert.Equal(["id", "candidatureId", "sorte", "survenuLe", "relanceId"], cloture.EnumerateObject().Select(champ => champ.Name));
        Assert.Equal("ClotureDeRelance", cloture.GetProperty("sorte").GetString());
        Assert.Equal("2026-11-18T13:31:00.1234567Z", cloture.GetProperty("survenuLe").GetString());
        Assert.Equal(1, cloture.GetProperty("relanceId").GetInt32());
        var correction = evenements[3];
        Assert.Equal(["id", "candidatureId", "sorte", "survenuLe", "ancien", "nouveau", "corrigeId"], correction.EnumerateObject().Select(champ => champ.Name));
        Assert.Equal("Proposition", correction.GetProperty("ancien").GetString());
        Assert.Equal("Entretien", correction.GetProperty("nouveau").GetString());
        Assert.Equal(3, correction.GetProperty("corrigeId").GetInt32());
        var etape = evenements[4];
        Assert.Equal(["id", "candidatureId", "sorte", "survenuLe", "type", "note", "corrigeId"], etape.EnumerateObject().Select(champ => champ.Name));
        Assert.Equal("2026-11-10T13:00:00Z", etape.GetProperty("survenuLe").GetString());
        Assert.Equal(
            "Premier entretien : accueil chaleureux.\nDeuxième ligne ; avec « guillemets », des \"guillemets droits\"\tet une tabulation. Merci 🙂",
            etape.GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, etape.GetProperty("corrigeId").ValueKind);
        Assert.Equal(5, evenements[5].GetProperty("corrigeId").GetInt32());
        Assert.Equal(JsonValueKind.Null, evenements[6].GetProperty("note").ValueKind);

        // Puis chaque colonne de chaque ligne de chaque table, lue par SQL dans la base elle-même.
        ComparerAuJsonColonneParColonne(new DossierDeLaBase(Donnees).Fichier, racine);
    }

    // Une relance en une ligne : identifiant, candidature, destinataire (« - » sans lui), échéance, état.
    private static string EnUneLigne(JsonElement relance)
    {
        var destinataire = relance.GetProperty("destinataireId");
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{relance.GetProperty("id").GetInt32()} {relance.GetProperty("candidatureId").GetInt32()} {(destinataire.ValueKind == JsonValueKind.Null ? "-" : destinataire.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture))} {relance.GetProperty("echeance").GetString()} {relance.GetProperty("etat").GetString()}");
    }

    [Fact]
    public async Task Ni_la_cle_d_IA_ni_le_jeton_ni_le_trousseau_ne_sont_dans_l_export()
    {
        const string Cle = "sk-ant-cle-fictive-export-49-ABCDEFGHIJKLMNOP";
        await using var api = await LancerSurLaBaseDeMainAsync();
        await api.ChoisirLIAAvecSuccesAsync("Claude");
        await api.EnregistrerLaCleAvecSuccesAsync(Cle);
        var dossier = new DossierDeLaBase(Donnees);
        Assert.True(File.Exists(dossier.FichierDeLaCle));
        var trousseau = Directory.GetFiles(dossier.Trousseau);
        Assert.NotEmpty(trousseau);
        var fichier = Path.Combine(Sortie, "export.json");

        Assert.Equal(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, fichier, remplacer: false, Annulation));

        var texte = await File.ReadAllTextAsync(fichier, Annulation);
        string[] interdits =
        [
            Cle,
            "cle-fictive-export-49",
            api.Jeton,
            .. Extraits(await File.ReadAllTextAsync(dossier.FichierDeLaCle, Annulation)),
            .. trousseau.Select(Path.GetFileNameWithoutExtension).OfType<string>(),
            .. trousseau.SelectMany(cle => Extraits(File.ReadAllText(cle))),
        ];
        foreach (var interdit in interdits)
        {
            Assert.DoesNotContain(interdit, texte, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Pendant_une_restauration_porte_fermee_l_export_est_refuse_et_n_ecrit_rien()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();
        var porte = api.Services.GetRequiredService<PorteDeLaBase>();
        Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, Annulation));
        try
        {
            Assert.Equal(IssueDeLExport.Occupe, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(Sortie, "export.json"), remplacer: false, Annulation));
        }
        finally
        {
            porte.Ouvrir();
        }

        Assert.Empty(Directory.GetFileSystemEntries(Sortie));
    }

    [Fact]
    public async Task Un_nom_sans_l_extension_du_format_ou_un_chemin_relatif_sont_refuses_sans_rien_ecrire()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();

        foreach (var nom in new[] { "export.txt", "export", "export.json.txt", "export.zip" })
        {
            Assert.Equal(IssueDeLExport.Extension, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(Sortie, nom), remplacer: false, Annulation));
        }

        Assert.Equal(IssueDeLExport.Impossible, await api.Export().ExporterAsync(FormatDExport.Json, "export.json", remplacer: false, Annulation));
        Assert.Empty(Directory.GetFileSystemEntries(Sortie));

        // L'extension se compare sans la casse.
        Assert.Equal(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(Sortie, "EXPORT.JSON"), remplacer: false, Annulation));
    }

    [Fact]
    public async Task Le_dossier_de_donnees_et_ses_sous_dossiers_sont_refuses_meme_avec_le_oui_a_l_ecrasement()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();
        var dossier = new DossierDeLaBase(Donnees);
        var base_ = await File.ReadAllBytesAsync(dossier.Fichier, Annulation);
        string[] cibles =
        [
            Path.Combine(Donnees, "export.json"),
            Path.Combine(dossier.DossierDesSauvegardes, "export.json"),
            Path.Combine(Donnees, "WebView2", "export.json"),
            Path.Combine(Sortie, "..", "donnees", "export.json"),
            Path.Combine(Donnees.ToUpperInvariant(), "export.json"),
            // Le nom même de la base, d'une extension que l'export n'écrit pas : refusé avant le dossier.
            dossier.Fichier,
        ];

        foreach (var cible in cibles)
        {
            Assert.NotEqual(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, cible, remplacer: true, Annulation));
        }

        Assert.Equal(IssueDeLExport.DossierDeDonnees, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(Donnees, "export.json"), remplacer: true, Annulation));
        Assert.False(File.Exists(Path.Combine(Donnees, "export.json")));
        Assert.DoesNotContain(Directory.EnumerateFiles(Donnees, "*", SearchOption.AllDirectories), nom => nom.EndsWith(ExportDesDonnees.SuffixeDuProvisoire, StringComparison.Ordinal));
        Assert.Equal(base_, await File.ReadAllBytesAsync(dossier.Fichier, Annulation));

        // Un dossier des sauvegardes choisi hors du dossier de données, comme un NAS ou OneDrive, reçoit un export.
        var choisi = Path.Combine(_racine, "Sauvegardes choisies");
        Directory.CreateDirectory(choisi);
        Assert.Equal(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(choisi, "export.json"), remplacer: false, Annulation));
    }

    [Fact]
    public async Task Un_fichier_existant_n_est_remplace_qu_avec_le_oui_de_l_utilisateur()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();
        var fichier = Path.Combine(Sortie, "export.json");
        await File.WriteAllTextAsync(fichier, "contenu d'avant", Annulation);

        // Sans le oui : un fichier apparu entre le dialogue et le renommage n'est pas remplacé.
        Assert.Equal(IssueDeLExport.Existe, await api.Export().ExporterAsync(FormatDExport.Json, fichier, remplacer: false, Annulation));
        Assert.Equal("contenu d'avant", await File.ReadAllTextAsync(fichier, Annulation));
        Assert.Equal(["export.json"], Fichiers(Sortie));

        // Avec le oui, il est remplacé, d'un seul renommage.
        Assert.Equal(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));
        using var export = JsonDocument.Parse(await File.ReadAllBytesAsync(fichier, Annulation));
        Assert.Equal("sollicito-export", export.RootElement.GetProperty("format").GetString());
        Assert.Equal(["export.json"], Fichiers(Sortie));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Un_fichier_en_lecture_seule_un_dossier_interdit_ou_disparu_ne_laissent_rien_et_le_disent()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();

        // Le fichier à remplacer est en lecture seule : accès refusé, et il reste intact.
        var lectureSeule = Path.Combine(Sortie, "lecture-seule.json");
        await File.WriteAllTextAsync(lectureSeule, "contenu d'avant", Annulation);
        File.SetAttributes(lectureSeule, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(IssueDeLExport.AccesRefuse, await api.Export().ExporterAsync(FormatDExport.Json, lectureSeule, remplacer: true, Annulation));
        }
        finally
        {
            File.SetAttributes(lectureSeule, FileAttributes.Normal);
        }

        Assert.Equal("contenu d'avant", await File.ReadAllTextAsync(lectureSeule, Annulation));
        Assert.Equal(["lecture-seule.json"], Fichiers(Sortie));

        // Un dossier où ce compte ne peut pas créer de fichier : accès refusé, et rien n'y paraît.
        var interdit = Path.Combine(_racine, "interdit");
        Directory.CreateDirectory(interdit);
        InterdireLaCreation(interdit, interdire: true);
        try
        {
            Assert.Equal(IssueDeLExport.AccesRefuse, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(interdit, "export.json"), remplacer: false, Annulation));
        }
        finally
        {
            InterdireLaCreation(interdit, interdire: false);
        }

        Assert.Empty(Directory.GetFileSystemEntries(interdit));

        // Un dossier disparu, comme une clé débranchée.
        Assert.Equal(
            IssueDeLExport.DossierIntrouvable,
            await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(_racine, "debranche", "export.json"), remplacer: false, Annulation));
        Assert.False(Directory.Exists(Path.Combine(_racine, "debranche")));
    }

    [Fact]
    public async Task Un_disque_plein_en_pleine_ecriture_ne_laisse_rien_et_garde_le_fichier_a_remplacer()
    {
        var etapes = new EtapesDeTest { Erreur = (EtapeDeLExport.CandidaturesEcrites, new IOException("Disque plein (essai).", unchecked((int)0x80070070))) };
        await using var api = await LancerSurLaBaseDeMainAsync(etapes.Utiliser);
        var fichier = Path.Combine(Sortie, "export.json");
        await File.WriteAllTextAsync(fichier, "contenu d'avant", Annulation);

        Assert.Equal(IssueDeLExport.PlaceManquante, await api.Export().ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));

        Assert.Contains(EtapeDeLExport.CandidaturesEcrites, etapes.Vues);
        Assert.Equal("contenu d'avant", await File.ReadAllTextAsync(fichier, Annulation));
        Assert.Equal(["export.json"], Fichiers(Sortie));
    }

    [Fact]
    public async Task L_annulation_en_plein_export_n_ecrit_rien()
    {
        using var fermeture = new CancellationTokenSource();
        var etapes = new EtapesDeTest { Action = etape => { if (etape == EtapeDeLExport.OffresEcrites) { fermeture.Cancel(); } } };
        await using var api = await LancerSurLaBaseDeMainAsync(etapes.Utiliser);

        Assert.Equal(IssueDeLExport.Annule, await api.Export().ExporterAsync(FormatDExport.Json, Path.Combine(Sortie, "export.json"), remplacer: false, fermeture.Token));

        Assert.Empty(Directory.GetFileSystemEntries(Sortie));
        Assert.DoesNotContain(EtapeDeLExport.FichierEcrit, etapes.Vues);
    }

    [Fact]
    public async Task Une_ecriture_validee_en_plein_export_n_y_parait_pas()
    {
        var etapes = new EtapesDeTest();
        await using var api = await LancerSurLaBaseDeMainAsync(etapes.Utiliser);
        // Les entreprises sont lues : une candidature, et son entreprise neuve, s'écrivent alors par l'API.
        etapes.Action = etape =>
        {
            if (etape == EtapeDeLExport.EntreprisesEcrites)
            {
                api.EnregistrerAvecSuccesAsync("Entreprise écrite en plein export", "Poste écrit en plein export", "2026-11-20T10:00").GetAwaiter().GetResult();
            }
        };
        var fichier = Path.Combine(Sortie, "export.json");

        Assert.Equal(IssueDeLExport.Exporte, await api.Export().ExporterAsync(FormatDExport.Json, fichier, remplacer: false, Annulation));

        var texte = await File.ReadAllTextAsync(fichier, Annulation);
        Assert.DoesNotContain("écrit en plein export", texte, StringComparison.Ordinal);
        using var export = JsonDocument.Parse(texte);
        Assert.Equal(4, export.RootElement.GetProperty("candidatures").GetArrayLength());
        Assert.Equal(5, (await api.ListerAsync()).Count);
    }

    [Fact]
    public async Task Aucune_route_n_exporte_et_la_documentation_openapi_n_en_decrit_aucune()
    {
        await using var api = await LancerSurLaBaseDeMainAsync();

        using var documentation = await api.EnvoyerAsync(HttpMethod.Get, "/api/openapi/v1.json", jeton: true);
        Assert.Equal(HttpStatusCode.OK, documentation.StatusCode);
        using var openApi = JsonDocument.Parse(await documentation.Content.ReadAsStringAsync(Annulation));
        Assert.DoesNotContain(openApi.RootElement.GetProperty("paths").EnumerateObject(), chemin => chemin.Name.Contains("export", StringComparison.OrdinalIgnoreCase));

        foreach (var methode in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            using var reponse = await api.EcrireAsync(methode, "/api/export", "{\"chemin\":\"C:\\\\export.json\"}");
            Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        }
    }

    private async Task<ApiLancee> LancerSurLaBaseDeMainAsync(Action<IWebHostBuilder>? ajuster = null)
    {
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Reference", "base-de-main-9ec81fa.db"),
            new DossierDeLaBase(Donnees).Fichier);
        var api = await LancerSurAsync(Donnees, new HorlogeDeTest(Le27), ajuster);
        await api.AttendreLaPremiereVerificationAsync();
        return api;
    }

    // Des morceaux reconnaissables d'un fichier de secrets : ses suites de lettres et de chiffres de plus de 16 caractères.
    private static IEnumerable<string> Extraits(string texte) =>
        System.Text.RegularExpressions.Regex.Matches(texte, "[A-Za-z0-9+/=_-]{16,}").Select(trouve => trouve.Value[..16]).Distinct(StringComparer.Ordinal);

    [SupportedOSPlatform("windows")]
    private static void InterdireLaCreation(string dossier, bool interdire)
    {
        var info = new DirectoryInfo(dossier);
        var droits = info.GetAccessControl();
        var regle = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            AccessControlType.Deny);
        if (interdire)
        {
            droits.AddAccessRule(regle);
        }
        else
        {
            droits.RemoveAccessRule(regle);
        }

        info.SetAccessControl(droits);
    }

    // Les colonnes de chaque table, et leur champ dans le JSON, écrits à la main, à part du code de l'export. Une table ou
    // une colonne que les migrations ajoutent, et qui manque ici, fait échouer la comparaison.
    private static readonly Dictionary<string, (string Tableau, Dictionary<string, string> Champs)> Correspondances = new(StringComparer.Ordinal)
    {
        ["Entreprises"] = ("entreprises", new(StringComparer.Ordinal) { ["Id"] = "id", ["Nom"] = "nom", ["Cle"] = "cle" }),
        ["Contacts"] = ("contacts", new(StringComparer.Ordinal) { ["Id"] = "id", ["EntrepriseId"] = "entrepriseId", ["Nom"] = "nom", ["Email"] = "email" }),
        ["Offres"] = ("offres", new(StringComparer.Ordinal)
        {
            ["Id"] = "id", ["Source"] = "source", ["Empreinte"] = "empreinte", ["EntrepriseId"] = "entrepriseId", ["Poste"] = "poste",
            ["PosteNormalise"] = "posteNormalise", ["Lieu"] = "lieu", ["PublieeLe"] = "publieeLe", ["Adresse"] = "adresse",
            ["Texte"] = "texte", ["AjouteeLe"] = "ajouteeLe",
        }),
        ["Candidatures"] = ("candidatures", new(StringComparer.Ordinal)
        {
            ["Id"] = "id", ["EntrepriseId"] = "entrepriseId", ["OffreId"] = "offreId", ["Poste"] = "poste", ["EnvoyeeLe"] = "envoyeeLe",
            ["Statut"] = "statut",
        }),
        ["Relances"] = ("relances", new(StringComparer.Ordinal)
        {
            ["Id"] = "id", ["CandidatureId"] = "candidatureId", ["DestinataireId"] = "destinataireId", ["Echeance"] = "echeance", ["Etat"] = "etat",
        }),
    };

    // Les événements : les colonnes communes, puis celles de chaque sorte ; les colonnes d'une autre sorte sont vides en base.
    private static readonly Dictionary<string, string> ColonnesCommunesDesEvenements = new(StringComparer.Ordinal)
    {
        ["Id"] = "id", ["CandidatureId"] = "candidatureId", ["Sorte"] = "sorte", ["SurvenuLe"] = "survenuLe",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> ColonnesDesSortes = new(StringComparer.Ordinal)
    {
        ["ChangementDeStatut"] = new(StringComparer.Ordinal) { ["Ancien"] = "ancien", ["Nouveau"] = "nouveau", ["CorrigeId"] = "corrigeId" },
        ["Etape"] = new(StringComparer.Ordinal) { ["Type"] = "type", ["Note"] = "note", ["EtapeCorrigeeId"] = "corrigeId" },
        ["ClotureDeRelance"] = new(StringComparer.Ordinal) { ["RelanceId"] = "relanceId" },
    };

    private static readonly string[] Instants = ["AjouteeLe", "EnvoyeeLe", "SurvenuLe"];

    private static void ComparerAuJsonColonneParColonne(string fichier, JsonElement export)
    {
        using var connexion = Ouvrir(fichier, SqliteOpenMode.ReadOnly);
        // Toutes les tables de l'utilisateur, hors de celles que SQLite et EF Core tiennent pour eux.
        var tables = Lignes(connexion, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\'")
            .Select(ligne => (string)ligne["name"]!)
            .Order(StringComparer.Ordinal);
        Assert.Equal(Correspondances.Keys.Append("EvenementsDeCandidature").Order(StringComparer.Ordinal), tables);

        foreach (var (table, (tableau, champs)) in Correspondances)
        {
            Assert.Equal(champs.Keys.Order(StringComparer.Ordinal), Colonnes(connexion, table).Order(StringComparer.Ordinal));
            var lignes = Lignes(connexion, $"SELECT * FROM \"{table}\" ORDER BY \"Id\"");
            var objets = export.GetProperty(tableau).EnumerateArray().ToArray();
            Assert.Equal(lignes.Count, objets.Length);
            for (var rang = 0; rang < lignes.Count; rang++)
            {
                Assert.Equal(champs.Count, objets[rang].EnumerateObject().Count());
                foreach (var (colonne, champ) in champs)
                {
                    Comparer(lignes[rang][colonne], objets[rang].GetProperty(champ), colonne);
                }
            }
        }

        var colonnesDesEvenements = ColonnesCommunesDesEvenements.Keys.Concat(ColonnesDesSortes.Values.SelectMany(sorte => sorte.Keys)).Order(StringComparer.Ordinal);
        Assert.Equal(colonnesDesEvenements, Colonnes(connexion, "EvenementsDeCandidature").Order(StringComparer.Ordinal));
        var evenements = Lignes(connexion, "SELECT * FROM \"EvenementsDeCandidature\" ORDER BY \"Id\"");
        var exportes = export.GetProperty("evenements").EnumerateArray().ToArray();
        Assert.Equal(evenements.Count, exportes.Length);
        for (var rang = 0; rang < evenements.Count; rang++)
        {
            var sorte = (string)evenements[rang]["Sorte"]!;
            var siens = ColonnesDesSortes[sorte];
            Assert.Equal(ColonnesCommunesDesEvenements.Count + siens.Count, exportes[rang].EnumerateObject().Count());
            foreach (var (colonne, champ) in ColonnesCommunesDesEvenements.Concat(siens))
            {
                Comparer(evenements[rang][colonne], exportes[rang].GetProperty(champ), colonne);
            }

            foreach (var autre in ColonnesDesSortes.Where(autre => autre.Key != sorte).SelectMany(autre => autre.Value.Keys))
            {
                Assert.Null(evenements[rang][autre]);
            }
        }
    }

    // Un entier, un texte, ou rien ; un instant de la base, « 2026-11-02 08:15:00 », s'exporte « 2026-11-02T08:15:00Z ».
    private static void Comparer(object? enBase, JsonElement exporte, string colonne)
    {
        switch (enBase)
        {
            case null:
                Assert.Equal(JsonValueKind.Null, exporte.ValueKind);
                break;
            case long entier:
                Assert.Equal(entier, exporte.GetInt64());
                break;
            case string texte when Instants.Contains(colonne):
                Assert.Equal(texte.Replace(' ', 'T') + "Z", exporte.GetString());
                break;
            case string texte:
                Assert.Equal(texte, exporte.GetString());
                break;
            default:
                Assert.Fail($"La colonne {colonne} porte une valeur d'un type inattendu.");
                break;
        }
    }

    private static string[] Colonnes(SqliteConnection connexion, string table) =>
        [.. Lignes(connexion, $"PRAGMA table_info(\"{table}\")").Select(ligne => (string)ligne["name"]!)];

    private static List<Dictionary<string, object?>> Lignes(SqliteConnection connexion, string requete)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        using var lecteur = commande.ExecuteReader();
        var lignes = new List<Dictionary<string, object?>>();
        while (lecteur.Read())
        {
            var ligne = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var colonne = 0; colonne < lecteur.FieldCount; colonne++)
            {
                ligne[lecteur.GetName(colonne)] = lecteur.IsDBNull(colonne) ? null : lecteur.GetValue(colonne);
            }

            lignes.Add(ligne);
        }

        return lignes;
    }

    /// <summary>Les étapes de l'export, vues par le test, qui peut y agir ou y lever une erreur.</summary>
    private sealed class EtapesDeTest : IEtapesDeLExport
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<EtapeDeLExport> _vues = new();

        public Action<EtapeDeLExport>? Action { get; set; }

        public (EtapeDeLExport Etape, Exception Erreur)? Erreur { get; init; }

        public IReadOnlyCollection<EtapeDeLExport> Vues => _vues;

        public void Atteinte(EtapeDeLExport etape)
        {
            _vues.Enqueue(etape);
            Action?.Invoke(etape);
            if (Erreur is { } erreur && erreur.Etape == etape)
            {
                throw erreur.Erreur;
            }
        }

        public void Utiliser(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.AddSingleton<IEtapesDeLExport>(this));
    }
}

/// <summary>Le service de l'export, que la fenêtre hôte appelle en mémoire.</summary>
internal static class AppelsDeLExport
{
    public static ExportDesDonnees Export(this ApiLancee api) => api.Services.GetRequiredService<ExportDesDonnees>();
}
