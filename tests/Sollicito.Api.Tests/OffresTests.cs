using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Sollicito.Api.Tests.Outils;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les offres collées (#36, ADR-0013), par l'API sur une vraie base SQLite : la saisie, ses refus champ par champ, le
/// même texte recollé (règle 2), le doublon possible et l'accord (règle 3), la fiche, la correction, les entreprises
/// proposées et les deux bornes de taille. Toutes les données sont fictives ; l'horloge est arrêtée le 18/11/2026.
/// </summary>
public sealed class OffresTests
{
    private const string Texte = "Développeur .NET (H/F)\n\nMissions : <b>maintenir</b> l'application.\n\tProfil : « autonome » & curieux.";

    // Les titres des réponses, mot pour mot.
    private const string TitreDuRefus = "L'offre n'est pas enregistrée : corrigez les champs signalés.";
    private const string TitreDeLaCorrectionRefusee = "L'offre n'est pas modifiée : corrigez les champs signalés.";
    private const string TitreDejaEnregistree = "Cette offre est déjà enregistrée : rien n'a été ajouté.";
    private const string TitreDuDoublonPossible =
        "Doublon possible : une offre du même poste, dans la même entreprise, est déjà enregistrée. Gardez les deux, ou renoncez à la nouvelle.";
    private const string TitreDeLOffreIntrouvable = "Cette offre n'existe pas.";

    // La borne du corps d'une offre collée : 1 Mio.
    private const int BorneDuCorps = 1024 * 1024;

    [Fact]
    public async Task Une_offre_collee_s_enregistre_avec_son_texte_tel_quel_et_ses_champs()
    {
        await using var api = await LancerAsync();

        var fiche = await api.CollerAvecSuccesAsync(
            "\r\n" + Texte.Replace("\n", "\r\n", StringComparison.Ordinal) + "  ",
            " Développeur .NET (H/F) ",
            entreprise: "Lumière Verte SA",
            lieu: "Liège",
            publieeLe: "2026-11-18",
            adresse: " https://emploi.exemple.invalid/offres/123?source=fictive ");

        Assert.Equal("TexteColle", fiche["source"]!.GetValue<string>());
        Assert.Equal(Texte, fiche["texte"]!.GetValue<string>());
        Assert.Equal("Développeur .NET (H/F)", fiche["poste"]!.GetValue<string>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("Liège", fiche["lieu"]!.GetValue<string>());
        Assert.Equal("2026-11-18", fiche["publieeLe"]!.GetValue<string>());
        Assert.Equal("https://emploi.exemple.invalid/offres/123?source=fictive", fiche["adresse"]!.GetValue<string>());
        Assert.Equal("2026-11-18T13:30:00Z", fiche["ajouteeLe"]!.GetValue<string>());
        Assert.Empty(fiche["doublonsPossibles"]!.AsArray());
        Assert.Equal(fiche.ToJsonString(), (await api.LireLOffreAsync(Id(fiche))).ToJsonString());
    }

    [Fact]
    public async Task Une_offre_sans_entreprise_ni_champ_facultatif_s_enregistre()
    {
        await using var api = await LancerAsync();

        var fiche = await api.CollerAvecSuccesAsync("Offre anonyme fictive.", "Comptable", entreprise: "  ", lieu: "", publieeLe: " ", adresse: "");

        Assert.Null(fiche["entreprise"]);
        Assert.Null(fiche["lieu"]);
        Assert.Null(fiche["publieeLe"]);
        Assert.Null(fiche["adresse"]);
    }

    [Fact]
    public async Task L_entreprise_se_reconnait_a_sa_cle_sous_le_nom_de_sa_premiere_saisie_sans_renommage()
    {
        await using var api = await LancerAsync();
        var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T09:00");

        var fiche = await api.CollerAvecSuccesAsync("Offre fictive.", "Chauffeur", entreprise: "LUMIERE VERTE s.a.");

        Assert.Equal(Id(candidature["entreprise"]), Id(fiche["entreprise"]));
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Single(await api.ListerLesEntreprisesAsync());
    }

    public static TheoryData<string, string, string> ChampsRefuses() => new()
    {
        { "texte", Corps("   ", "Comptable"), "Collez le texte de l'offre." },
        { "texte", Corps(new string('a', 50_001), "Comptable"), "Le texte de l'offre ne doit pas dépasser 50 000 caractères." },
        { "texte", Corps("Ligne 1\fLigne 2\r\nLigne 3 \u0007", "Comptable"), "Le texte de l'offre contient, à la ligne 3, un caractère de contrôle ou invalide : retirez-le, puis réessayez." },
        { "texte", Corps("Offre \u202E fictive", "Comptable"), "Le texte de l'offre contient, à la ligne 1, un caractère de contrôle ou invalide : retirez-le, puis réessayez." },
        { "poste", Corps("Offre fictive.", "  "), "Indiquez le poste." },
        { "poste", Corps("Offre fictive.", "!!!"), "Le poste doit contenir au moins une lettre ou un chiffre." },
        { "poste", Corps("Offre fictive.", "Poste\nfictif"), "Le poste ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide." },
        { "entreprise", Corps("Offre fictive.", "Comptable", entreprise: "!!!"), "Le nom de l'entreprise doit contenir au moins une lettre ou un chiffre." },
        { "entreprise", Corps("Offre fictive.", "Comptable", entreprise: new string('e', 201)), "Le nom de l'entreprise ne doit pas dépasser 200 caractères." },
        { "lieu", Corps("Offre fictive.", "Comptable", lieu: "Lieu\tfictif"), "Le lieu ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide." },
        { "publieeLe", Corps("Offre fictive.", "Comptable", publieeLe: "18/11/2026"), "La date de publication s'écrit AAAA-MM-JJ, par exemple 2026-11-18." },
        { "publieeLe", Corps("Offre fictive.", "Comptable", publieeLe: "2026-11-19"), "La date de publication ne peut pas être après aujourd'hui." },
        { "publieeLe", Corps("Offre fictive.", "Comptable", publieeLe: "1999-12-31"), "La date de publication ne peut pas être avant le 1er janvier 2000." },
        { "adresse", Corps("Offre fictive.", "Comptable", adresse: "javascript:alert('fictif')"), "L'adresse de l'annonce doit être une adresse web complète, qui commence par http:// ou https://." },
        { "adresse", Corps("Offre fictive.", "Comptable", adresse: "https://nom:fictif@exemple.invalid/"), "L'adresse de l'annonce ne doit pas porter de nom d'utilisateur ni de mot de passe." },
        { "adresse", Corps("Offre fictive.", "Comptable", adresse: "https://exemple.invalid/" + new string('a', 2_000)), "L'adresse de l'annonce ne doit pas dépasser 2 000 caractères." },
    };

    [Theory]
    [MemberData(nameof(ChampsRefuses))]
    public async Task Un_champ_refuse_a_son_message_sans_reprendre_la_saisie_et_rien_ne_s_enregistre(string champ, string corps, string message)
    {
        await using var api = await LancerAsync();

        using var reponse = await api.EcrireAsync(HttpMethod.Post, Route, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var refus = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(TitreDuRefus, refus["title"]!.GetValue<string>());
        Assert.Equal([message], refus["errors"]![champ]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.DoesNotContain("fictif", refus.ToJsonString(), StringComparison.Ordinal);
        Assert.Empty(await api.ListerLesEntreprisesAsync());
        await AucuneOffreAsync(api);
    }

    [Theory]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","source":"TexteColle"}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","empreinte":"0000"}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","ajouteeLe":"2026-11-18T10:00:00Z"}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","texte":"Autre texte fictif."}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","poste":"Chauffeur"}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","garderLesDeux":"true"}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable","garderLesDeux":1}""")]
    [InlineData("""{"texte":42,"poste":"Comptable"}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":["Comptable"]}""")]
    [InlineData("""{"texte":"Offre fictive.","poste":"Comptable" """)]
    [InlineData("")]
    public async Task Un_corps_illisible_ou_etranger_champ_repete_compris_n_enregistre_rien(string corps)
    {
        await using var api = await LancerAsync();

        using var reponse = await api.EcrireAsync(HttpMethod.Post, Route, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        await AucuneOffreAsync(api);
    }

    [Fact]
    public async Task Un_corps_de_plus_de_1_Mio_est_refuse_par_413_et_un_corps_de_moins_passe_jusqu_a_la_validation()
    {
        await using var api = await LancerAsync();
        // 50 000 caractères échappés en \uXXXX : 300 Ko, sous la borne ; le texte est admis.
        var echappe = """{"poste":"Comptable","texte":" """ + string.Concat(Enumerable.Repeat("\\u00e9", 50_000)) + "\"}";

        using (var admis = await api.EcrireAsync(HttpMethod.Post, Route, echappe))
        {
            Assert.Equal(HttpStatusCode.Created, admis.StatusCode);
        }

        using var tropGros = await api.EcrireAsync(HttpMethod.Post, Route, Corps(new string('a', BorneDuCorps), "Chauffeur"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tropGros.StatusCode);
        // La correction, qui ne porte pas de texte, garde la borne par défaut, 256 Kio (#98, point 3 ; BornesDesCorpsTests).
        using var correction = await api.EcrireAsync(HttpMethod.Put, AdresseDeLOffre(1), Corps(texte: null, poste: new string('p', 1_100_000)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, correction.StatusCode);
    }

    [Fact]
    public async Task Le_meme_texte_recolle_est_reconnu_a_son_empreinte_et_rien_ne_s_enregistre_ni_ne_s_ecrase()
    {
        await using var api = await LancerAsync();
        var premiere = await api.CollerAvecSuccesAsync(Texte, "Développeur .NET", entreprise: "Lumière Verte SA", lieu: "Liège");

        // Même texte, autres blancs, autre casse, autres fins de ligne ; et d'autres champs, qui n'écrasent rien.
        using var reponse = await api.CollerAsync(
            "  " + Texte.ToUpperInvariant().Replace("\n", "\f", StringComparison.Ordinal).Replace(" ", "   ", StringComparison.Ordinal),
            "Chauffeur",
            entreprise: "Brasserie Lune Rousse",
            lieu: "Namur");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var refus = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal(TitreDejaEnregistree, refus["title"]!.GetValue<string>());
        Assert.Equal("dejaEnregistree", refus["motif"]!.GetValue<string>());
        Assert.Equal(Id(premiere), Id(refus["offre"]));
        Assert.Equal("2026-11-18T13:30:00Z", refus["offre"]!["ajouteeLe"]!.GetValue<string>());
        Assert.Equal(premiere.ToJsonString(), (await api.LireLOffreAsync(Id(premiere))).ToJsonString());
        // La seconde entreprise n'a pas été créée.
        Assert.Equal(["Lumière Verte SA"], (await api.ListerLesEntreprisesAsync()).Select(e => e!["nom"]!.GetValue<string>()));
        await AucuneOffreAsync(api, sauf: Id(premiere));
    }

    [Fact]
    public async Task Un_doublon_possible_est_refuse_sans_l_accord_puis_enregistre_avec_et_jamais_fusionne()
    {
        await using var api = await LancerAsync();
        var premiere = await api.CollerAvecSuccesAsync("Première offre fictive.", "Développeur .NET (H/F)", entreprise: "Lumière Verte SA", lieu: "Liège");

        using (var refusee = await api.CollerAsync("Seconde offre fictive.", "developpeur  .net h/f", entreprise: "LUMIERE VERTE", lieu: "Namur"))
        {
            Assert.Equal(HttpStatusCode.Conflict, refusee.StatusCode);
            var refus = await AppelsDesCandidatures.LireAsync(refusee);
            Assert.Equal(TitreDuDoublonPossible, refus["title"]!.GetValue<string>());
            Assert.Equal("doublonPossible", refus["motif"]!.GetValue<string>());
            var doublon = Assert.Single(refus["doublons"]!.AsArray())!;
            Assert.Equal(Id(premiere), Id(doublon));
            Assert.Equal("Développeur .NET (H/F)", doublon["poste"]!.GetValue<string>());
            Assert.Equal("Lumière Verte SA", doublon["entreprise"]!["nom"]!.GetValue<string>());
            Assert.Equal("Liège", doublon["lieu"]!.GetValue<string>());
            Assert.DoesNotContain("Seconde", refus.ToJsonString(), StringComparison.Ordinal);
            await AucuneOffreAsync(api, sauf: Id(premiere));
        }

        var seconde = await api.CollerAvecSuccesAsync("Seconde offre fictive.", "developpeur  .net h/f", entreprise: "LUMIERE VERTE", lieu: "Namur", garderLesDeux: true);

        Assert.NotEqual(Id(premiere), Id(seconde));
        Assert.Equal([Id(premiere)], Ids(seconde["doublonsPossibles"]));
        Assert.Equal([Id(seconde)], Ids((await api.LireLOffreAsync(Id(premiere)))["doublonsPossibles"]));
        Assert.Equal("Première offre fictive.", (await api.LireLOffreAsync(Id(premiere)))["texte"]!.GetValue<string>());
    }

    [Fact]
    public async Task L_accord_ne_passe_pas_la_regle_2_et_une_offre_sans_entreprise_n_est_jamais_comparee()
    {
        await using var api = await LancerAsync();
        await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA");

        using (var meme = await api.CollerAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA", garderLesDeux: true))
        {
            Assert.Equal("dejaEnregistree", (await AppelsDesCandidatures.LireAsync(meme))["motif"]!.GetValue<string>());
        }

        var anonyme = await api.CollerAvecSuccesAsync("Offre anonyme fictive.", "Comptable");
        var autreAnonyme = await api.CollerAvecSuccesAsync("Autre offre anonyme fictive.", "Comptable");
        Assert.Empty(anonyme["doublonsPossibles"]!.AsArray());
        Assert.Empty(autreAnonyme["doublonsPossibles"]!.AsArray());
        // Un autre poste de la même entreprise n'est pas un doublon possible.
        Assert.Empty((await api.CollerAvecSuccesAsync("Autre offre fictive.", "Comptable confirmé", entreprise: "Lumière Verte SA"))["doublonsPossibles"]!.AsArray());
    }

    [Fact]
    public async Task Dix_collages_simultanes_du_meme_texte_n_en_gardent_qu_un_et_dix_d_un_meme_poste_aussi()
    {
        await using var api = await LancerAsync();

        var memeTexte = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var reponse = await api.CollerAsync("Offre fictive simultanée.", "Comptable", entreprise: "Lumière Verte SA");
            return reponse.StatusCode;
        }));
        var memePoste = await Task.WhenAll(Enumerable.Range(0, 10).Select(async numero =>
        {
            using var reponse = await api.CollerAsync($"Offre fictive {numero}.", "Chauffeur", entreprise: "Brasserie Lune Rousse");
            return reponse.StatusCode;
        }));

        Assert.Equal(1, memeTexte.Count(statut => statut == HttpStatusCode.Created));
        Assert.Equal(9, memeTexte.Count(statut => statut == HttpStatusCode.Conflict));
        Assert.Equal(1, memePoste.Count(statut => statut == HttpStatusCode.Created));
        Assert.Equal(9, memePoste.Count(statut => statut == HttpStatusCode.Conflict));
        Assert.Equal(2, (await api.ListerLesEntreprisesAsync()).Count);
    }

    [Fact]
    public async Task Une_offre_inconnue_est_introuvable_a_la_lecture_comme_a_la_correction()
    {
        await using var api = await LancerAsync();

        using var lecture = await api.EnvoyerAsync(HttpMethod.Get, AdresseDeLOffre(999), jeton: true);
        using var correction = await api.CorrigerLOffreAsync(999, "Comptable");

        Assert.Equal(HttpStatusCode.NotFound, lecture.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, correction.StatusCode);
        Assert.Equal(TitreDeLOffreIntrouvable, (await AppelsDesCandidatures.LireAsync(lecture))["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Corriger_une_offre_change_ses_champs_jamais_son_texte_et_ne_renomme_aucune_entreprise()
    {
        await using var api = await LancerAsync();
        await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T09:00");
        var offre = await api.CollerAvecSuccesAsync(Texte, "Comptble", entreprise: "Lumiere Vert", lieu: "Liège", adresse: "https://exemple.invalid/1");

        using var reponse = await api.CorrigerLOffreAsync(Id(offre), "Comptable", entreprise: "lumière verte", publieeLe: "2026-11-17");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var fiche = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("Comptable", fiche["poste"]!.GetValue<string>());
        Assert.Equal("Lumière Verte SA", fiche["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Null(fiche["lieu"]);
        Assert.Null(fiche["adresse"]);
        Assert.Equal("2026-11-17", fiche["publieeLe"]!.GetValue<string>());
        Assert.Equal(Texte, fiche["texte"]!.GetValue<string>());
        // L'entreprise fautive, que plus rien ne désigne, n'est plus proposée ; elle reste en base.
        Assert.Equal(["Lumière Verte SA"], (await api.ListerLesEntreprisesAsync()).Select(e => e!["nom"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Une_correction_qui_porte_un_texte_ou_un_champ_inconnu_est_illisible_et_ne_change_rien()
    {
        await using var api = await LancerAsync();
        var offre = await api.CollerAvecSuccesAsync(Texte, "Comptable");

        foreach (var corps in new[]
        {
            """{"poste":"Chauffeur","texte":"Autre texte fictif."}""",
            """{"poste":"Chauffeur","source":"TexteColle"}""",
            """{"poste":"Chauffeur","poste":"Brasseur"}""",
        })
        {
            using var reponse = await api.EcrireAsync(HttpMethod.Put, AdresseDeLOffre(Id(offre)), corps);
            Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        }

        using (var refusee = await api.CorrigerLOffreAsync(Id(offre), "  "))
        {
            Assert.Equal(TitreDeLaCorrectionRefusee, (await AppelsDesCandidatures.LireAsync(refusee))["title"]!.GetValue<string>());
        }

        Assert.Equal(offre.ToJsonString(), (await api.LireLOffreAsync(Id(offre))).ToJsonString());
    }

    [Fact]
    public async Task Une_correction_vers_le_poste_d_une_autre_offre_de_l_entreprise_refait_la_regle_3()
    {
        await using var api = await LancerAsync();
        var premiere = await api.CollerAvecSuccesAsync("Première offre fictive.", "Comptable", entreprise: "Lumière Verte SA");
        var seconde = await api.CollerAvecSuccesAsync("Seconde offre fictive.", "Chauffeur", entreprise: "Lumière Verte SA");

        // Le lieu seul change : rien à comparer, même en double.
        using (var lieu = await api.CorrigerLOffreAsync(Id(seconde), "Chauffeur", entreprise: "Lumière Verte SA", lieu: "Namur"))
        {
            Assert.Equal(HttpStatusCode.OK, lieu.StatusCode);
        }

        using (var refusee = await api.CorrigerLOffreAsync(Id(seconde), "COMPTABLE", entreprise: "Lumière Verte SA"))
        {
            Assert.Equal(HttpStatusCode.Conflict, refusee.StatusCode);
            var refus = await AppelsDesCandidatures.LireAsync(refusee);
            Assert.Equal("doublonPossible", refus["motif"]!.GetValue<string>());
            Assert.Equal([Id(premiere)], Ids(refus["doublons"]));
            Assert.Equal("Chauffeur", (await api.LireLOffreAsync(Id(seconde)))["poste"]!.GetValue<string>());
        }

        using var gardee = await api.CorrigerLOffreAsync(Id(seconde), "COMPTABLE", entreprise: "Lumière Verte SA", garderLesDeux: true);
        Assert.Equal(HttpStatusCode.OK, gardee.StatusCode);
        Assert.Equal([Id(premiere)], Ids((await AppelsDesCandidatures.LireAsync(gardee))["doublonsPossibles"]));
        // Le doublon gardé ne se signale plus à chaque correction qui ne change ni l'entreprise ni le poste normalisé.
        using var lieuEncore = await api.CorrigerLOffreAsync(Id(seconde), "Comptable", entreprise: "Lumière Verte SA", lieu: "Liège");
        Assert.Equal(HttpStatusCode.OK, lieuEncore.StatusCode);
    }

    [Fact]
    public async Task Une_entreprise_que_seule_une_offre_designe_est_proposee_et_reconnue_par_la_saisie_d_une_candidature()
    {
        await using var api = await LancerAsync();
        var offre = await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Brasserie Lune Rousse");

        var proposees = await api.ListerLesEntreprisesAsync();

        Assert.Equal(["Brasserie Lune Rousse"], proposees.Select(e => e!["nom"]!.GetValue<string>()));
        var candidature = await api.EnregistrerAvecSuccesAsync("BRASSERIE LUNE ROUSSE", "Brasseuse", "2026-11-10T09:00");
        Assert.Equal(Id(offre["entreprise"]), Id(candidature["entreprise"]));
        // Supprimer la candidature laisse l'offre : l'entreprise reste proposée.
        using var suppression = await api.EcrireAsync(HttpMethod.Delete, AppelsDuSuivi.AdresseDeLaFiche(AppelsDuSuivi.IdDe(candidature)), corps: null);
        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
        Assert.Single(await api.ListerLesEntreprisesAsync());
    }

    [Fact]
    public async Task Un_refus_ne_cree_aucune_entreprise_lue_en_base_meme_une_que_rien_ne_designe()
    {
        // Suites de #36 (#98, point 11) : les entreprises proposées taisent une entreprise que rien ne désigne ; seule la
        // base dit qu'aucune ne s'est créée.
        await using var api = await LancerAsync();
        var offre = Id(await api.CollerAvecSuccesAsync("Offre fictive.", "Comptable", entreprise: "Lumière Verte SA"));
        await api.CollerAvecSuccesAsync("Offre fictive du même poste.", "Chauffeur", entreprise: "Lumière Verte SA");

        HttpStatusCode[] statuts =
        [
            // Un champ refusé, à l'enregistrement comme à la correction.
            await StatutAsync(api.CollerAsync("Autre offre fictive.", "Comptable", entreprise: "Atelier d'Ondine", publieeLe: "2099-01-01")),
            await StatutAsync(api.CorrigerLOffreAsync(offre, "Comptable", entreprise: "Atelier d'Ondine", publieeLe: "2099-01-01")),
            // Le même texte recollé (règle 2), avec une autre entreprise.
            await StatutAsync(api.CollerAsync("Offre fictive.", "Comptable", entreprise: "Brasserie Lune Rousse")),
            // Un doublon possible (règle 3), à l'enregistrement comme à la correction.
            await StatutAsync(api.CollerAsync("Troisième offre fictive.", "Chauffeur", entreprise: "LUMIERE VERTE S.A.")),
            await StatutAsync(api.CorrigerLOffreAsync(offre, "Chauffeur", entreprise: "Lumière Verte SA")),
        ];

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.Conflict], statuts);
        Assert.Equal(["Lumière Verte SA"], await api.EntreprisesEnBaseAsync());
    }

    private static async Task<HttpStatusCode> StatutAsync(Task<HttpResponseMessage> envoi)
    {
        using var reponse = await envoi;
        return reponse.StatusCode;
    }

    private static Task<ApiLancee> LancerAsync() => ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

    // Aucune offre de plus que celle-ci (ou aucune) : les identifiants suivants ne mènent à rien.
    private static async Task AucuneOffreAsync(ApiLancee api, int? sauf = null)
    {
        foreach (var id in Enumerable.Range(1, 12).Where(id => id != sauf))
        {
            using var reponse = await api.EnvoyerAsync(HttpMethod.Get, AdresseDeLOffre(id), jeton: true);
            Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        }
    }
}
