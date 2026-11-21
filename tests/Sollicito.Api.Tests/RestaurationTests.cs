using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Sollicito.Api.Relances;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// La restauration d'une copie gardée (#28, ADR-0012), appelée comme la fenêtre hôte l'appelle, après le oui : la copie
/// remplace la base, la base d'avant se copie d'abord, et rien ne change quand la copie est abîmée, étrangère, plus
/// récente, introuvable ou changée. Porte fermée, l'API refuse les routes qui touchent la base. Tout se passe dans des
/// dossiers temporaires, jamais dans celui de l'utilisateur ; données fictives.
/// </summary>
public sealed class RestaurationTests : IAsyncLifetime
{
    private static readonly DateOnly Le18 = new(2026, 11, 18);

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
    private readonly List<string> _autresDossiers = [];

    private DossierDeLaBase DossierDeLaBase => new(_dossier);

    private string ParDefaut => DossierDeLaBase.DossierDesSauvegardes;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await SupprimerAsync(_dossier);
        foreach (var dossier in _autresDossiers)
        {
            await SupprimerAsync(dossier);
        }
    }

    [Fact]
    public async Task Une_copie_du_jour_restauree_remplace_la_base_et_la_base_d_avant_se_copie_puis_se_restaure_a_son_tour()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        await api.SauvegarderMaintenantAsync();
        await api.EnregistrerAvecSuccesAsync("Beta Logistique fictif", "Chauffeur B", "2026-11-03T09:00");
        var copieDuJour = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
        var empreinteDeLaCopie = Empreinte(copieDuJour);
        var derniereCopie = api.Sauvegardes().Etat.DerniereCopie;

        Assert.Equal(IssueDeLaRestauration.Restauree, await api.RestaurerAsync(api.Copie(api.CopieDuJour(Le18))));

        // La base est celle de la copie, relue par une nouvelle requête, en mode WAL.
        Assert.Equal(["Comptable A"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()));
        Assert.Equal("wal", ModeDuJournal(DossierDeLaBase.Fichier));
        Assert.True(EstSaine(DossierDeLaBase.Fichier));
        // La copie d'avant, vérifiée, garde la base telle qu'elle était, et se liste à part ; ni la copie restaurée, ni la
        // dernière copie, ni les réglages n'ont changé, et aucune copie de travail ne reste.
        var avant = Assert.Single(api.Sauvegardes().Etat.Copies, copie => copie.Sorte == SorteDeCopie.AvantRestauration);
        Assert.Matches("^sollicito-avant-restauration-[0-9]{8}-[0-9]{9}\\.db$", avant.Nom);
        Assert.True(EstSaine(Path.Combine(ParDefaut, avant.Nom)));
        Assert.Equal(["Comptable A", "Chauffeur B"], PostesDe(Path.Combine(ParDefaut, avant.Nom)));
        Assert.Equal(empreinteDeLaCopie, Empreinte(copieDuJour));
        Assert.Equal(derniereCopie, api.Sauvegardes().Etat.DerniereCopie);
        Assert.False(File.Exists(DossierDeLaBase.FichierDesReglages));
        Assert.DoesNotContain(Fichiers(_dossier), nom => nom.Contains(".restauration", StringComparison.Ordinal));

        // Revenir en arrière : la copie d'avant se restaure comme une autre.
        Assert.Equal(IssueDeLaRestauration.Restauree, await api.RestaurerAsync(avant));
        Assert.Equal(["Comptable A", "Chauffeur B"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(2, api.Sauvegardes().Etat.Copies.Count(copie => copie.Sorte == SorteDeCopie.AvantRestauration));
    }

    [Fact]
    public async Task La_copie_d_une_autre_installation_se_restaure_sans_changer_la_rotation_la_derniere_copie_ni_l_alerte()
    {
        var autre = NouveauDossier();
        string copieDeLAutre;
        await using (var ailleurs = await LancerSurAsync(autre, new HorlogeDeTest()))
        {
            await ailleurs.AttendreLaPremiereVerificationAsync();
            await ailleurs.EnregistrerAvecSuccesAsync("Poste lointain fictif", "Analyste X", "2026-11-04T09:00");
            await ailleurs.SauvegarderMaintenantAsync();
            copieDeLAutre = ailleurs.CopieDuJour(Le18);
        }

        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        File.Copy(Path.Combine(new DossierDeLaBase(autre).DossierDesSauvegardes, copieDeLAutre), Path.Combine(ParDefaut, copieDeLAutre));
        await api.SauvegarderMaintenantAsync();
        var lAutre = api.Copie(copieDeLAutre);
        Assert.Equal(SorteDeCopie.DuJourDUneAutreInstallation, lAutre.Sorte);
        var empreinteDeLAutre = Empreinte(Path.Combine(ParDefaut, copieDeLAutre));
        var derniereCopie = api.Sauvegardes().Etat.DerniereCopie;

        Assert.Equal(IssueDeLaRestauration.Restauree, await api.RestaurerAsync(lAutre));

        Assert.Equal(["Analyste X"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()));
        Assert.Equal(derniereCopie, api.Sauvegardes().Etat.DerniereCopie);
        Assert.Null(api.Sauvegardes().Etat.Echec);
        Assert.Equal(empreinteDeLAutre, Empreinte(Path.Combine(ParDefaut, copieDeLAutre)));
        // La base restaurée ne change pas l'installation : la copie du jour suivante garde le nom de celle-ci.
        await api.SauvegarderMaintenantAsync();
        Assert.Equal(["Analyste X"], PostesDe(Path.Combine(ParDefaut, api.CopieDuJour(Le18))));
        Assert.Equal(empreinteDeLAutre, Empreinte(Path.Combine(ParDefaut, copieDeLAutre)));
    }

    [Fact]
    public async Task Une_copie_plus_ancienne_que_l_exe_se_migre_sur_la_copie_de_travail_puis_remplace_la_base()
    {
        var ancien = NouveauDossier();
        await BaseDe12.PeuplerAsync(new DossierDeLaBase(ancien));
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        var nom = NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 10));
        File.Copy(new DossierDeLaBase(ancien).Fichier, Path.Combine(ParDefaut, nom));
        await api.SauvegarderMaintenantAsync();

        Assert.Equal(IssueDeLaRestauration.Restauree, await api.RestaurerAsync(api.Copie(nom)));

        Assert.Equal(["Brasseuse", "Comptable"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.Equal(BaseDe12.DerniereMigration, MigrationsDe(DossierDeLaBase.Fichier)[^1]);
        // La sauvegarde elle-même n'a pas été ouverte : elle reste à la migration de #12.
        Assert.Equal(BaseDe12.Migration, MigrationsDe(Path.Combine(ParDefaut, nom))[^1]);
    }

    [Fact]
    public async Task Une_base_abimee_se_remplace_et_ses_fichiers_se_gardent_tels_quels_hors_de_la_liste()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        await api.SauvegarderMaintenantAsync();
        for (var numero = 1; numero <= 30; numero++)
        {
            await api.EnregistrerAvecSuccesAsync($"Entreprise fictive {numero}", $"Poste fictif {numero} au long intitulé", "2026-11-03T09:00");
        }

        var copie = api.Copie(api.CopieDuJour(Le18));
        var abimee = Abimer(DossierDeLaBase.Fichier);

        var preparation = await api.Restauration().PreparerAsync(copie.Id, TestContext.Current.CancellationToken);
        Assert.Equal(EtatDeLaBase.Abimee, preparation.Demande!.EtatDeLaBase);
        Assert.Equal(IssueDeLaRestauration.RestaureeDepuisUneBaseAbimee, await api.Restauration().RestaurerAsync(preparation.Demande, TestContext.Current.CancellationToken));

        Assert.True(EstSaine(DossierDeLaBase.Fichier));
        Assert.Equal(["Comptable A"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()));
        var brute = Assert.Single(Fichiers(ParDefaut), nom => nom.EndsWith("-brute.db", StringComparison.Ordinal));
        Assert.Equal(abimee, Octets(Path.Combine(ParDefaut, brute)));
        Assert.DoesNotContain(api.Sauvegardes().Etat.Copies, gardee => gardee.Nom == brute || gardee.Sorte == SorteDeCopie.AvantRestauration);
    }

    [Fact]
    public async Task Une_copie_reecrite_ou_disparue_depuis_la_liste_est_refusee_sans_rien_changer()
    {
        var cle = NouveauDossier();
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");

        // Réécrite entre la liste et le oui : une copie du jour refaite.
        var reecrite = await PreparerAsync(api, api.Copie(api.CopieDuJour(Le18)));
        await api.SauvegarderMaintenantAsync();
        Assert.Equal(IssueDeLaRestauration.Changee, await api.Restauration().RestaurerAsync(reecrite, TestContext.Current.CancellationToken));

        // Effacée, puis sur une clé débranchée.
        var effacee = await PreparerAsync(api, api.Copie(api.CopieDuJour(Le18)));
        await EffacerAsync(Path.Combine(ParDefaut, api.CopieDuJour(Le18)));
        Assert.Equal(IssueDeLaRestauration.Introuvable, await api.Restauration().RestaurerAsync(effacee, TestContext.Current.CancellationToken));
        Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(cle, TestContext.Current.CancellationToken));
        var surLaCle = await PreparerAsync(api, Assert.Single(api.Sauvegardes().Etat.Copies, copie => copie.Lieu == LieuDUneCopie.DossierDesCopies));
        await SupprimerAsync(cle);
        Assert.Equal(IssueDeLaRestauration.Introuvable, await api.Restauration().RestaurerAsync(surLaCle, TestContext.Current.CancellationToken));

        // Un identifiant inconnu, ou d'une autre forme, est refusé avant tout dialogue.
        foreach (var identifiant in new[] { "0000000000000000", "0123456789ABCDEF", @"C:\Sauvegardes\copie.db", "" })
        {
            var refus = await api.Restauration().PreparerAsync(identifiant, TestContext.Current.CancellationToken);
            Assert.Equal((null, IssueDeLaRestauration.Introuvable), (refus.Demande, refus.Refus));
        }

        Assert.Equal(["Comptable A"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()));
        Assert.DoesNotContain(Fichiers(ParDefaut), nom => nom.StartsWith("sollicito-avant-restauration-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_copie_abimee_etrangere_ou_plus_recente_n_est_jamais_restauree()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        await api.SauvegarderMaintenantAsync();
        var saine = Path.Combine(ParDefaut, api.CopieDuJour(Le18));
        var abimee = NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 10));
        var etrangere = NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 11));
        var plusRecente = NomsDesCopies.DuJour(api.Sauvegardes().Installation, new DateOnly(2026, 11, 12));
        await File.WriteAllBytesAsync(Path.Combine(ParDefaut, abimee), Abime(Octets(saine)), TestContext.Current.CancellationToken);
        using (var connexion = Ouvrir(Path.Combine(ParDefaut, etrangere), Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate))
        {
            connexion.Executer("CREATE TABLE Notes (Id INTEGER PRIMARY KEY, Texte TEXT)");
        }

        File.Copy(saine, Path.Combine(ParDefaut, plusRecente));
        using (var connexion = Ouvrir(Path.Combine(ParDefaut, plusRecente)))
        {
            connexion.Executer("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29991231000000_UneVersionAVenir', '10.0.15')");
        }

        await api.SauvegarderMaintenantAsync();

        Assert.Equal(IssueDeLaRestauration.Abimee, await api.RestaurerAsync(api.Copie(abimee)));
        Assert.Equal(IssueDeLaRestauration.Etrangere, await api.RestaurerAsync(api.Copie(etrangere)));
        Assert.Equal(IssueDeLaRestauration.PlusRecente, await api.RestaurerAsync(api.Copie(plusRecente)));

        Assert.Equal(["Comptable A"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()));
        Assert.DoesNotContain(Fichiers(ParDefaut), nom => nom.StartsWith("sollicito-avant-restauration-", StringComparison.Ordinal));
        Assert.DoesNotContain(Fichiers(_dossier), nom => nom.Contains(".restauration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_fichier_verrouille_ou_une_place_qui_manque_a_chaque_etape_ne_change_rien_et_ne_laisse_aucun_reste()
    {
        var etapes = new EtapesDeTest();
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest(), AppelsDeLaRestauration.AvecLesEtapes(etapes));
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        await api.SauvegarderMaintenantAsync();
        await api.EnregistrerAvecSuccesAsync("Beta Logistique fictif", "Chauffeur B", "2026-11-03T09:00");
        var copie = api.Copie(api.CopieDuJour(Le18));

        // La sauvegarde tenue sans partage par un autre programme.
        using (new FileStream(Path.Combine(ParDefaut, copie.Nom), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(IssueDeLaRestauration.Impossible, await api.RestaurerAsync(copie));
        }

        // La place qui manque, injectée à chaque étape où l'on écrit : la copie de travail, la copie d'avant, le remplacement.
        (EtapeDeLaRestauration Etape, IssueDeLaRestauration Issue)[] cas =
        [
            (EtapeDeLaRestauration.CopieDeTravailEcrite, IssueDeLaRestauration.Impossible),
            (EtapeDeLaRestauration.CopieDAvantEcrite, IssueDeLaRestauration.CopieDAvantImpossible),
            (EtapeDeLaRestauration.PendantLeRemplacement, IssueDeLaRestauration.Impossible),
        ];
        foreach (var (etape, issue) in cas)
        {
            etapes.Arret = etape;
            etapes.Erreur = new IOException("Disque plein (essai).", unchecked((int)0x80070070));
            Assert.Equal(issue, await api.RestaurerAsync(copie));
        }

        // Le remplacement interrompu entre deux tranches s'est défait : la base est celle d'avant, entière.
        Assert.Contains(EtapeDeLaRestauration.PendantLeRemplacement, etapes.Vues);
        Assert.True(EstSaine(DossierDeLaBase.Fichier));
        Assert.Equal(["Chauffeur B", "Comptable A"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Fichiers(_dossier), nom => nom.Contains(".restauration", StringComparison.Ordinal));
        Assert.DoesNotContain(Fichiers(ParDefaut), nom => nom.EndsWith(".partielle", StringComparison.Ordinal));
        // Seule la copie d'avant du dernier cas, faite avant le remplacement manqué, reste : vérifiée, entière.
        var avant = Assert.Single(Fichiers(ParDefaut), nom => nom.StartsWith("sollicito-avant-restauration-", StringComparison.Ordinal));
        Assert.Equal(["Comptable A", "Chauffeur B"], PostesDe(Path.Combine(ParDefaut, avant)));
    }

    [Fact]
    public async Task Porte_fermee_l_api_refuse_les_routes_qui_touchent_la_base_et_les_brouillons_mais_l_etat_se_lit()
    {
        var etapes = new EtapesDeTest { Arret = EtapeDeLaRestauration.PorteFermee };
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest(), AppelsDeLaRestauration.AvecLesEtapes(etapes));
        await api.AttendreLaPremiereVerificationAsync();
        var candidature = await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        var fiche = await api.PlanifierAvecSuccesAsync(candidature["id"]!.GetValue<int>());
        var relance = Assert.Single(fiche["relances"]!.AsArray())!;
        await api.SauvegarderMaintenantAsync();
        var copie = api.Copie(api.CopieDuJour(Le18));

        var restauration = Task.Run(() => api.RestaurerAsync(copie));
        await etapes.Atteinte.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        using (var ecriture = await api.EnregistrerAsync("Pendant fictif", "Refusé", "2026-11-05T09:00"))
        using (var lecture = await api.EnvoyerAsync(HttpMethod.Get, "/api/candidatures", jeton: true))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ecriture.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, lecture.StatusCode);
            Assert.Equal(PorteDeLaBase.TitreDuRefus, (await AppelsDesCandidatures.LireAsync(ecriture))["title"]!.GetValue<string>());
        }

        foreach (var chemin in new[] { "/api/etat", "/api/sauvegardes", "/api/reglages/relances", "/api/openapi/v1.json" })
        {
            using var reponse = await api.EnvoyerAsync(HttpMethod.Get, chemin, jeton: true);
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        }

        Assert.Null(await api.Services.GetRequiredService<BrouillonsDesRelances>().PreparerAsync(relance["id"]!.GetValue<int>(), TestContext.Current.CancellationToken));
        Assert.Equal(IssueDeLaRestauration.Occupee, await api.RestaurerAsync(copie));

        etapes.Relacher();
        Assert.Equal(IssueDeLaRestauration.Restauree, await restauration.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        using var apres = await api.EnregistrerAsync("Après fictif", "Admis", "2026-11-05T09:00");
        Assert.Equal(HttpStatusCode.Created, apres.StatusCode);
        Assert.NotNull(await api.Services.GetRequiredService<BrouillonsDesRelances>().PreparerAsync(relance["id"]!.GetValue<int>(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Une_ecriture_admise_avant_la_fermeture_de_la_porte_entre_dans_la_copie_d_avant()
    {
        var etapes = new EtapesDeTest { Arret = EtapeDeLaRestauration.CopieDeTravailVerifiee };
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest(), AppelsDeLaRestauration.AvecLesEtapes(etapes));
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        await api.SauvegarderMaintenantAsync();
        var copie = api.Copie(api.CopieDuJour(Le18));

        var restauration = Task.Run(() => api.RestaurerAsync(copie));
        await etapes.Atteinte.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await api.EnregistrerAvecSuccesAsync("Pendant fictif", "Admis avant la porte", "2026-11-05T09:00");
        etapes.Relacher();

        Assert.Equal(IssueDeLaRestauration.Restauree, await restauration.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        var avant = Assert.Single(Fichiers(ParDefaut), nom => nom.StartsWith("sollicito-avant-restauration-", StringComparison.Ordinal));
        Assert.Equal(["Comptable A", "Admis avant la porte"], PostesDe(Path.Combine(ParDefaut, avant)));
        Assert.Equal(["Comptable A"], (await api.ListerAsync()).Select(candidature => candidature!["poste"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Un_etat_de_la_base_qui_n_est_plus_celui_du_dialogue_fait_refuser_sans_rien_changer()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.EnregistrerAvecSuccesAsync("Atelier Azur fictif", "Comptable A", "2026-11-02T09:00");
        await api.SauvegarderMaintenantAsync();
        await api.EnregistrerAvecSuccesAsync("Beta Logistique fictif", "Chauffeur B", "2026-11-03T09:00");
        var demande = await PreparerAsync(api, api.Copie(api.CopieDuJour(Le18)));
        Assert.Equal(EtatDeLaBase.Saine, demande.EtatDeLaBase);

        Assert.Equal(IssueDeLaRestauration.BaseChangee, await api.Restauration().RestaurerAsync(demande with { EtatDeLaBase = EtatDeLaBase.Abimee }, TestContext.Current.CancellationToken));

        Assert.Equal(2, (await api.ListerAsync()).Count);
        Assert.DoesNotContain(Fichiers(ParDefaut), nom => nom.StartsWith("sollicito-avant-restauration-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Les_restes_d_une_restauration_interrompue_se_retirent_au_lancement_et_les_copies_finies_restent()
    {
        await using (var api = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await api.AttendreLaPremiereVerificationAsync();
        }

        var instant = HorlogeDeTest.Reference;
        string[] restes =
        [
            Path.Combine(_dossier, RestesDUnArret.NomDUneCopieARestaurer()),
            Path.Combine(_dossier, RestesDUnArret.NomDUneCopieARestaurer() + "-journal"),
            Path.Combine(ParDefaut, NomsDesCopies.Provisoire(NomsDesCopies.AvantRestauration(instant))),
            Path.Combine(ParDefaut, NomsDesCopies.Provisoire(NomsDesCopies.AvantRestaurationBrute(instant))),
            Path.Combine(ParDefaut, NomsDesCopies.Provisoire(NomsDesCopies.AvantRestaurationBrute(instant) + "-wal")),
        ];
        string[] autres =
        [
            Path.Combine(ParDefaut, NomsDesCopies.AvantRestauration(instant)),
            Path.Combine(ParDefaut, NomsDesCopies.AvantRestaurationBrute(instant)),
            Path.Combine(ParDefaut, "notes-personnelles.txt"),
            Path.Combine(_dossier, "sollicito.db.restauration"),
        ];
        foreach (var fichier in restes.Concat(autres))
        {
            await File.WriteAllTextAsync(fichier, "fictif", TestContext.Current.CancellationToken);
        }

        // L'API construite sans être démarrée, comme dans DossierDeLApiSeuleTests, ne retire rien.
        await using (var construite = ApiLocale.ConstruirePourLaFenetre(new NullFileProvider(), _dossier))
        {
        }

        Assert.All(restes.Concat(autres), fichier => Assert.True(File.Exists(fichier), fichier));

        // Le lancement les retire, sous le verrou du dossier, et laisse les copies finies et ce qui n'est pas à Sollicito.
        await using (var relancee = await LancerSurAsync(_dossier, new HorlogeDeTest()))
        {
            await relancee.AttendreLaPremiereVerificationAsync();
        }

        Assert.All(restes, fichier => Assert.False(File.Exists(fichier), fichier));
        Assert.All(autres, fichier => Assert.True(File.Exists(fichier), fichier));
    }

    private static async Task<DemandeDeRestauration> PreparerAsync(ApiLancee api, CopieGardee copie) =>
        (await api.Restauration().PreparerAsync(copie.Id, TestContext.Current.CancellationToken)).Demande!;

    private string NouveauDossier()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        _autresDossiers.Add(dossier);
        return dossier;
    }

    // Abîme une page du milieu de la base, pas son en-tête, et rend ses octets abîmés.
    private static byte[] Abimer(string fichier)
    {
        var octets = Abime(Octets(fichier));
        File.WriteAllBytes(fichier, octets);
        return octets;
    }

    private static byte[] Abime(byte[] octets)
    {
        var page = Math.Max(2, octets.Length / 4096 / 2);
        Array.Fill(octets, (byte)0x5A, (page - 1) * 4096, 4096);
        return octets;
    }

    private static string Empreinte(string fichier) => Convert.ToHexString(SHA256.HashData(Octets(fichier)));
}

/// <summary>La porte de la base (#28) : fermée, elle attend que ce qui est entré ressorte, dans un délai borné.</summary>
public sealed class PorteDeLaBaseTests
{
    [Fact]
    public async Task Fermee_la_porte_attend_que_ce_qui_est_entre_ressorte_et_ne_laisse_plus_rien_entrer()
    {
        var porte = new PorteDeLaBase();
        Assert.True(porte.EssayerDEntrer());

        var fermeture = porte.FermerAsync(TimeSpan.FromSeconds(30), TimeProvider.System, TestContext.Current.CancellationToken);
        Assert.False(fermeture.IsCompleted);
        Assert.False(porte.EssayerDEntrer());

        porte.Sortir();
        Assert.True(await fermeture);
        Assert.True(porte.EstFermee);
        porte.Ouvrir();
        Assert.True(porte.EssayerDEntrer());
    }

    [Fact]
    public async Task Ce_qui_ne_ressort_pas_dans_le_delai_fait_rouvrir_la_porte_sans_rien_changer()
    {
        var porte = new PorteDeLaBase();
        Assert.True(porte.EssayerDEntrer());

        Assert.False(await porte.FermerAsync(TimeSpan.FromMilliseconds(50), TimeProvider.System, TestContext.Current.CancellationToken));

        Assert.False(porte.EstFermee);
        Assert.True(porte.EssayerDEntrer());
    }
}
