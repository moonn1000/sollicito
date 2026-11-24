using System.Net;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Offres;
using Sollicito.Domain.Offres;
using static Sollicito.Api.Tests.Outils.AppelsDeLImport;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'import d'un fichier CSV d'offres (#40, ADR-0015), par l'API sur une vraie base SQLite : l'aperçu qui n'écrit rien,
/// l'import en une seule transaction, le dédoublonnage commun contre la base et dans le fichier, l'accord ligne par
/// ligne, les entreprises, les refus du fichier et du corps, la concurrence et la panne. Toutes les données sont
/// fictives.
/// </summary>
public sealed class ImportCsvTests
{
    // Une base qui a déjà une offre collée : « Comptable », chez Lumière Verte SA, à Liège.
    private static async Task<(ApiLancee Api, int Collee)> LancerAsync()
    {
        var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var collee = Id(await api.CollerAvecSuccesAsync("Offre collée fictive.", "Comptable", entreprise: "Lumière Verte SA", lieu: "Liège"));
        return (api, collee);
    }

    // Un fichier qui rencontre la base et se rencontre lui-même, ligne par ligne :
    //  2 à importer ; 3 doublon possible de l'offre collée ; 4 en double de la ligne 2 ; 5 date refusée ; 6 mal formée ;
    //  7 vide ; 8 à importer, entreprise neuve ; 9 doublon possible de la ligne 8, même clé d'entreprise.
    private static readonly byte[] Fichier = Csv(
        "poste;entreprise;lieu;publieeLe;adresse\r\n"
        + "Chauffeur;Lumière Verte;Namur;15/11/2026;https://exemple.invalid/1\r\n"
        + "comptable;LUMIÈRE VERTE;Huy;;\r\n"
        + "Chauffeur;Lumière Verte;Namur;15/11/2026;https://exemple.invalid/1\r\n"
        + "Magasinier;Lumière Verte;Liège;31/02/2026;\r\n"
        + "Magasinier;Lumière Verte\r\n"
        + ";;;;\r\n"
        + "Brasseur;Brasserie Lune Rousse;Liège;2026-11-10;\r\n"
        + "Brasseur;Brasserie Lune Rousse SRL;Charleroi;;\r\n");

    [Fact]
    public async Task L_apercu_dit_le_sort_de_chaque_ligne_sans_rien_ecrire()
    {
        var (api, collee) = await LancerAsync();
        await using var _ = api;
        var entreprises = await api.EntreprisesEnBaseAsync();

        var apercu = await api.ExaminerAvecSuccesAsync(Fichier);

        Assert.Equal(
            new Dictionary<int, string>
            {
                [2] = "aImporter", [3] = "doublonPossible", [4] = "enDouble", [5] = "invalide", [6] = "invalide", [8] = "aImporter", [9] = "doublonPossible",
            },
            Sorts(apercu));
        Assert.Equal("Chauffeur", Ligne(apercu, 2)["poste"]!.GetValue<string>());
        Assert.Equal("Lumière Verte", Ligne(apercu, 2)["entreprise"]!.GetValue<string>());
        Assert.Equal(collee, Ligne(apercu, 3)["doublons"]![0]!["id"]!.GetValue<int>());
        Assert.Equal(2, Ligne(apercu, 4)["ligneDOrigine"]!.GetValue<int>());
        Assert.Equal([DateDePublication.IllisibleJourEnPremier], Ligne(apercu, 5)["refus"]!["publieeLe"]!.AsArray().Select(motif => motif!.GetValue<string>()));
        Assert.Null(Ligne(apercu, 5)["poste"]);
        Assert.Equal(["La ligne n'a pas autant de cellules que l'en-tête."], Ligne(apercu, 6)["refus"]!["ligne"]!.AsArray().Select(motif => motif!.GetValue<string>()));
        Assert.Equal([8], Ligne(apercu, 9)["lignesDuFichier"]!.AsArray().Select(numero => numero!.GetValue<int>()));
        Assert.Empty(Ligne(apercu, 9)["doublons"]!.AsArray());
        Assert.Equal(7, apercu["comptes"]!["lignes"]!.GetValue<int>());
        Assert.Equal(1, apercu["comptes"]!["lignesVides"]!.GetValue<int>());

        // Rien d'écrit : ni offre, ni entreprise.
        Assert.Equal(1, await api.CompterLesOffresAsync());
        Assert.Equal(entreprises, await api.EntreprisesEnBaseAsync());
    }

    [Fact]
    public async Task L_import_enregistre_les_lignes_admises_et_les_doublons_gardes_en_une_fois()
    {
        var (api, collee) = await LancerAsync();
        await using var _ = api;

        var compteRendu = await api.ImporterAvecSuccesAsync(Fichier, 3);

        Assert.Equal(
            new Dictionary<int, string>
            {
                [2] = "importee", [3] = "importee", [4] = "enDouble", [5] = "invalide", [6] = "invalide", [8] = "importee", [9] = "doublonPossible",
            },
            Sorts(compteRendu));
        Assert.Equal(3, compteRendu["comptes"]!["importees"]!.GetValue<int>());
        Assert.Equal(collee, Ligne(compteRendu, 3)["doublons"]![0]!["id"]!.GetValue<int>());
        Assert.Equal(4, await api.CompterLesOffresAsync());

        // L'entreprise de l'offre collée est reconnue par sa clé, sans renommage ; l'entreprise neuve est créée une seule
        // fois, sous le nom de la première ligne importée qui la désigne.
        Assert.Equal(["Brasserie Lune Rousse", "Lumière Verte SA"], await api.EntreprisesEnBaseAsync());

        var fiche = await api.LireLOffreAsync(Ligne(compteRendu, 2)["offre"]!.GetValue<int>());
        Assert.Equal("FichierCsv", fiche["source"]!.GetValue<string>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("2026-11-15", fiche["publieeLe"]!.GetValue<string>());
        Assert.Equal("https://exemple.invalid/1", fiche["adresse"]!.GetValue<string>());
        Assert.Null(fiche["texte"]);

        // La fiche de la ligne gardée montre l'offre collée parmi ses doublons possibles, et réciproquement.
        Assert.Contains(collee, (await api.LireLOffreAsync(Ligne(compteRendu, 3)["offre"]!.GetValue<int>()))["doublonsPossibles"]!.AsArray().Select(offre => offre!["id"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Reimporter_le_meme_fichier_n_importe_rien_de_plus_et_un_fichier_complete_ses_seules_lignes_nouvelles()
    {
        var (api, _) = await LancerAsync();
        await using var __ = api;
        await api.ImporterAvecSuccesAsync(Fichier);

        var memeFichier = await api.ImporterAvecSuccesAsync(Fichier);
        Assert.Equal(0, memeFichier["comptes"]!["importees"]!.GetValue<int>());
        Assert.Equal("dejaEnregistree", Sorts(memeFichier)[2]);
        Assert.Equal("dejaEnregistree", Sorts(memeFichier)[8]);

        var complete = await api.ImporterAvecSuccesAsync(Csv(Encoding.UTF8.GetString(Fichier) + "Vendeur;Lune Rousse;Mons;;\r\n"));
        Assert.Equal("importee", Sorts(complete)[10]);
        Assert.Equal(1, complete["comptes"]!["importees"]!.GetValue<int>());
    }

    [Fact]
    public async Task Une_date_le_jour_d_abord_ou_en_aaaa_mm_jj_fait_la_meme_offre()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.ImporterAvecSuccesAsync(Csv("poste;publieeLe\nComptable;8/11/2026\n"));

        var autreForme = await api.ImporterAvecSuccesAsync(Csv("publieeLe;poste\n2026-11-08;Comptable\n"));

        Assert.Equal("dejaEnregistree", Sorts(autreForme)[2]);
        Assert.Equal(1, await api.CompterLesOffresAsync());
    }

    [Fact]
    public async Task Une_offre_collee_et_la_meme_ligne_importee_ne_se_reconnaissent_que_par_la_regle_3()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        await api.CollerAvecSuccesAsync("Comptable", "Comptable", entreprise: "Lumière Verte");

        var apercu = await api.ExaminerAvecSuccesAsync(Csv("poste;entreprise;texte\nComptable;Lumière Verte;Comptable\n"));

        Assert.Equal("doublonPossible", Sorts(apercu)[2]);
    }

    [Fact]
    public async Task Si_la_base_a_change_depuis_l_apercu_l_import_fait_foi()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var fichier = Csv("poste;entreprise\nComptable;Lumière Verte\nChauffeur;Lumière Verte\n");
        Assert.Equal("aImporter", Sorts(await api.ExaminerAvecSuccesAsync(fichier))[3]);

        // Entre l'aperçu et l'import, une offre collée fait de la ligne 3 un doublon possible : sans accord, elle est écartée.
        await api.CollerAvecSuccesAsync("Offre collée fictive.", "Chauffeur", entreprise: "Lumière Verte");
        var compteRendu = await api.ImporterAvecSuccesAsync(fichier);

        Assert.Equal("importee", Sorts(compteRendu)[2]);
        Assert.Equal("doublonPossible", Sorts(compteRendu)[3]);
        Assert.Equal(2, await api.CompterLesOffresAsync());
    }

    [Fact]
    public async Task Le_texte_d_une_ligne_est_garde_tel_quel_et_une_formule_reste_du_texte()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        var compteRendu = await api.ImporterAvecSuccesAsync(Csv("poste;lieu;texte\n=1+1;@SOMME(A1);\"<script>alert('fictif')</script>\r\nDeuxième ligne\"\n"));

        var fiche = await api.LireLOffreAsync(Ligne(compteRendu, 2)["offre"]!.GetValue<int>());
        Assert.Equal("=1+1", fiche["poste"]!.GetValue<string>());
        Assert.Equal("@SOMME(A1)", fiche["lieu"]!.GetValue<string>());
        Assert.Equal("<script>alert('fictif')</script>\nDeuxième ligne", fiche["texte"]!.GetValue<string>());
    }

    public static TheoryData<string, byte[], string> FichiersRefuses => new()
    {
        { "vide", [], "Le fichier est vide." },
        { "trop gros", new byte[(2 * 1024 * 1024) + 1], "Le fichier dépasse 2 Mo : découpez-le en plusieurs fichiers." },
        { "Windows-1252", [.. Encoding.ASCII.GetBytes("poste\nD"), 0xE9, .. Encoding.ASCII.GetBytes("veloppeur\n")], "Le fichier n'est pas en UTF-8 : enregistrez-le au format CSV UTF-8, puis réessayez." },
        { "UTF-16", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("poste\nComptable\n")], "Le fichier n'est pas en UTF-8 : enregistrez-le au format CSV UTF-8, puis réessayez." },
        { "colonne en trop", Csv("poste;salaire\nComptable;x\n"), "La colonne 2 de l'en-tête n'est pas une colonne du format." },
        { "colonne manquante", Csv("entreprise\nLumière Verte\n"), "La première ligne doit nommer les colonnes : la colonne poste y manque." },
        { "guillemet", Csv("poste;lieu\nComptable;\"Liège\n"), "Le fichier est mal formé à la ligne 2 : un guillemet n'est jamais fermé." },
        { "trop de lignes", Csv("poste\n" + string.Concat(Enumerable.Repeat("Comptable\n", 1_001))), "Le fichier porte plus de 1 000 lignes d'offres : découpez-le en plusieurs fichiers." },
        { "aucune offre", Csv("poste;lieu\n"), "Le fichier ne porte aucune offre sous son en-tête." },
        { "cellule trop longue", Csv("poste;lieu\nComptable;Liège\nChauffeur;" + new string('a', 201) + "\n"), "Ligne 3, colonne lieu : la cellule dépasse 200 caractères." },
    };

    [Theory]
    [MemberData(nameof(FichiersRefuses))]
    public async Task Un_fichier_hors_du_format_est_refuse_en_entier_sans_rien_importer(string cas, byte[] contenu, string motif)
    {
        Assert.NotEmpty(cas);
        var (api, _) = await LancerAsync();
        await using var __ = api;

        foreach (var reponse in new[] { await api.ExaminerAsync(contenu), await api.ImporterAsync(contenu) })
        {
            using (reponse)
            {
                Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
                var probleme = await AppelsDesCandidatures.LireAsync(reponse);
                Assert.Equal([motif], probleme["errors"]!["contenu"]!.AsArray().Select(message => message!.GetValue<string>()));
            }
        }

        Assert.Equal(1, await api.CompterLesOffresAsync());
        Assert.Equal(["Lumière Verte SA"], await api.EntreprisesEnBaseAsync());
    }

    [Fact]
    public async Task Un_corps_de_plus_de_3_mio_est_refuse_avant_d_etre_lu()
    {
        var (api, _) = await LancerAsync();
        await using var __ = api;

        using var reponse = await api.ImporterAsync(new byte[2_400_000]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, reponse.StatusCode);
        Assert.Equal(1, await api.CompterLesOffresAsync());
    }

    public static TheoryData<string, string> CorpsEtrangers => new()
    {
        { RouteDeLApercu, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","nom":"offres.csv"}""" },
        { RouteDeLApercu, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","garder":[2]}""" },
        { RouteDeLImport, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","chemin":"C:\\offres.csv"}""" },
        { RouteDeLImport, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","contenu":"cG9zdGUKQ29tcHRhYmxlCg=="}""" },
        { RouteDeLImport, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","Contenu":"cG9zdGUKQ29tcHRhYmxlCg=="}""" },
        { RouteDeLImport, """{"contenu":42}""" },
        { RouteDeLImport, """{"contenu":"pas du base64 !"}""" },
        { RouteDeLImport, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","garder":"2"}""" },
        { RouteDeLImport, """{"contenu":"cG9zdGUKQ29tcHRhYmxlCg==","garder":["2"]}""" },
    };

    [Theory]
    [MemberData(nameof(CorpsEtrangers))]
    public async Task Un_corps_etranger_est_illisible_et_n_importe_rien(string route, string corps)
    {
        var (api, _) = await LancerAsync();
        await using var __ = api;

        using var reponse = await api.EcrireAsync(HttpMethod.Post, route, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(1, await api.CompterLesOffresAsync());
    }

    [Theory]
    [InlineData(99)]
    [InlineData(1)]
    [InlineData(3, 3)]
    public async Task Des_lignes_gardees_mal_designees_refusent_l_import(params int[] garder)
    {
        var (api, _) = await LancerAsync();
        await using var __ = api;

        using var reponse = await api.ImporterAsync(Fichier, garder);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(
            ["Les lignes gardées doivent être des lignes d'offres du fichier, chacune une fois."],
            (await AppelsDesCandidatures.LireAsync(reponse))["errors"]!["garder"]!.AsArray().Select(message => message!.GetValue<string>()));
        Assert.Equal(1, await api.CompterLesOffresAsync());
    }

    [Fact]
    public async Task Une_ligne_gardee_qui_n_est_pas_un_doublon_possible_s_importe_comme_les_autres()
    {
        var (api, _) = await LancerAsync();
        await using var __ = api;

        var compteRendu = await api.ImporterAvecSuccesAsync(Fichier, 2, 5);

        Assert.Equal("importee", Sorts(compteRendu)[2]);
        Assert.Equal("invalide", Sorts(compteRendu)[5]);
        Assert.Equal("doublonPossible", Sorts(compteRendu)[3]);
    }

    [Fact]
    public async Task Deux_imports_simultanes_du_meme_fichier_n_importent_chaque_offre_qu_une_fois()
    {
        var (api, _) = await LancerAsync();
        await using var __ = api;

        var reponses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.ImporterAsync(Fichier, 3, 9)));

        Assert.All(reponses, reponse => Assert.Equal(HttpStatusCode.OK, reponse.StatusCode));
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }

        // L'offre collée, puis les lignes 2, 3, 8 et 9 : une seule fois chacune.
        Assert.Equal(5, await api.CompterLesOffresAsync());
        Assert.Equal(["Brasserie Lune Rousse", "Lumière Verte SA"], await api.EntreprisesEnBaseAsync());
    }

    [Fact]
    public async Task Une_panne_au_milieu_de_l_import_ne_laisse_ni_offre_ni_entreprise()
    {
        await using var api = await ApiLancee.LancerAsync(builder =>
        {
            new HorlogeDeTest().Utiliser(builder);
            builder.ConfigureTestServices(DepotQuiTombeAvantDeValider.Remplacer);
        });
        await api.CollerAvecSuccesAsync("Offre collée fictive.", "Comptable", entreprise: "Lumière Verte SA");

        using var reponse = await api.ImporterAsync(Fichier, 3);

        Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        Assert.Equal(1, await api.CompterLesOffresAsync());
        Assert.Equal(["Lumière Verte SA"], await api.EntreprisesEnBaseAsync());
    }

    /// <summary>
    /// Le vrai dépôt des offres, dont l'écriture tombe en panne après avoir ajouté les offres d'un lot, avant d'être
    /// validée : entreprises neuves et offres sont écrites dans la transaction, puis abandonnées.
    /// </summary>
    private sealed class DepotQuiTombeAvantDeValider(IDepotDesOffres reel) : IDepotDesOffres
    {
        public static void Remplacer(IServiceCollection services)
        {
            var origine = services.Last(service => service.ServiceType == typeof(IDepotDesOffres));
            services.AddScoped<IDepotDesOffres>(fournisseur =>
                new DepotQuiTombeAvantDeValider((IDepotDesOffres)ActivatorUtilities.CreateInstance(fournisseur, origine.ImplementationType!)));
        }

        public async Task<IEcritureDesOffres> CommencerUneEcritureAsync(CancellationToken annulation) =>
            new Ecriture(await reel.CommencerUneEcritureAsync(annulation));

        public Task<FicheDUneOffre?> LireAsync(int id, CancellationToken annulation) => reel.LireAsync(id, annulation);

        public Task<IReadOnlyList<OffreListee>> ListerAsync(CancellationToken annulation) => reel.ListerAsync(annulation);

        private sealed class Ecriture(IEcritureDesOffres reelle) : IEcritureDesOffres
        {
            private bool _lotAjoute;

            public Task<Domain.Entreprises.Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation) => reelle.TrouverLEntrepriseAsync(cle, annulation);

            public Task<OffreDejaEnregistree?> TrouverParEmpreinteAsync(SourceDOffre source, string empreinte, CancellationToken annulation) => reelle.TrouverParEmpreinteAsync(source, empreinte, annulation);

            public Task<IReadOnlyList<OffreSignalee>> ListerLesDoublonsPossiblesAsync(int entrepriseId, string posteNormalise, int? sauf, CancellationToken annulation) =>
                reelle.ListerLesDoublonsPossiblesAsync(entrepriseId, posteNormalise, sauf, annulation);

            public Task<Offre?> TrouverLOffreAsync(int id, CancellationToken annulation) => reelle.TrouverLOffreAsync(id, annulation);

            public Task AjouterAsync(Domain.Entreprises.Entreprise entreprise, CancellationToken annulation) => reelle.AjouterAsync(entreprise, annulation);

            public Task AjouterAsync(Offre offre, CancellationToken annulation) => reelle.AjouterAsync(offre, annulation);

            public async Task AjouterAsync(IReadOnlyList<Offre> offres, CancellationToken annulation)
            {
                await reelle.AjouterAsync(offres, annulation);
                _lotAjoute = true;
            }

            public Task ValiderAsync(CancellationToken annulation) =>
                _lotAjoute ? throw new InvalidOperationException("Panne simulée avant de valider l'import.") : reelle.ValiderAsync(annulation);

            public ValueTask DisposeAsync() => reelle.DisposeAsync();
        }
    }
}
