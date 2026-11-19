using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests;

/// <summary>
/// Enregistrer une candidature et la retrouver dans le tableau de bord (#11), par les routes de l'API, sur une vraie
/// base SQLite créée par les migrations dans un dossier temporaire. Toutes les données sont fictives, et l'horloge est
/// arrêtée au 18/11/2026 à 14:30, heure de Bruxelles.
/// </summary>
public sealed class CandidaturesTests
{
    [Fact]
    public async Task Au_premier_lancement_les_migrations_creent_la_base_dans_le_dossier_de_donnees()
    {
        await using var api = await LancerAsync();

        Assert.True(File.Exists(Path.Combine(api.DossierDeDonnees, DossierDeLaBase.NomDuFichier)));
        await using var portee = api.Services.CreateAsyncScope();
        var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
        var appliquees = await baseDeSollicito.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(appliquees);
        Assert.Equal(baseDeSollicito.Database.GetMigrations(), appliquees);
        // Le modèle n'a changé sans migration : la base des migrations est bien celle du code.
        Assert.False(baseDeSollicito.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Les_tests_creent_leur_base_dans_un_dossier_temporaire_jamais_dans_celui_de_l_utilisateur()
    {
        await using var api = await LancerAsync();

        var fichier = api.Services.GetRequiredService<DossierDeLaBase>().Fichier;

        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), fichier, StringComparison.OrdinalIgnoreCase);
        Assert.False(fichier.StartsWith(Donnees.DossierDeDonnees.DeLUtilisateur() + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Une_candidature_s_enregistre_au_statut_Envoyee_avec_son_envoi_en_UTC()
    {
        await using var api = await LancerAsync();

        var candidature = await api.EnregistrerAvecSuccesAsync("  Lumière Verte SA ", " Développeuse .NET ", "2026-03-27T23:30");

        Assert.True(candidature["id"]!.GetValue<int>() > 0);
        Assert.Equal("Lumière Verte SA", candidature["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal("Développeuse .NET", candidature["poste"]!.GetValue<string>());
        // Saisie à 23:30, heure d'hiver de Bruxelles : 22:30 UTC, que l'API rend avec son « Z ».
        Assert.Equal("2026-03-27T22:30:00Z", candidature["envoyeeLe"]!.GetValue<string>());
        Assert.Equal("Envoyee", candidature["statut"]!.GetValue<string>());
        Assert.Equal(candidature.ToJsonString(), Assert.Single(await api.ListerAsync())!.ToJsonString());
    }

    [Theory]
    [InlineData("2026-11-18T14:30", "2026-11-18T13:30:00Z")]
    [InlineData("2026-07-01T14:30", "2026-07-01T12:30:00Z")]
    [InlineData("2026-07-01T14:30:45", "2026-07-01T12:30:45Z")]
    // Une heure qui existe deux fois, au retour à l'heure d'hiver, se lit à l'heure d'hiver (UTC+1).
    [InlineData("2026-10-25T02:30", "2026-10-25T01:30:00Z")]
    public async Task L_heure_saisie_se_lit_dans_le_fuseau_du_poste(string saisie, string attendu)
    {
        await using var api = await LancerAsync();

        var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", saisie);

        Assert.Equal(attendu, candidature["envoyeeLe"]!.GetValue<string>());
    }

    [Fact]
    public async Task Le_tableau_de_bord_liste_les_candidatures_les_plus_recentes_d_abord()
    {
        await using var api = await LancerAsync();
        await api.EnregistrerAvecSuccesAsync("Brasserie Lune Rousse", "Brasseuse", "2026-03-02T09:00");
        await api.EnregistrerAvecSuccesAsync("Les Amis du Parc ASBL", "Animateur", "2026-11-18T14:30");
        await api.EnregistrerAvecSuccesAsync("L’Atelier d’Ondine", "Couturière", "2026-06-15T16:45");
        // Même instant d'envoi qu'une autre : la dernière enregistrée vient d'abord.
        await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-06-15T16:45");

        var liste = await api.ListerAsync();

        Assert.Equal(
            ["Les Amis du Parc ASBL", "Lumière Verte SA", "L’Atelier d’Ondine", "Brasserie Lune Rousse"],
            liste.Select(candidature => candidature!["entreprise"]!["nom"]!.GetValue<string>()));
        Assert.Equal(
            ["Animateur", "Comptable", "Couturière", "Brasseuse"],
            liste.Select(candidature => candidature!["poste"]!.GetValue<string>()));
        Assert.All(liste, candidature => Assert.Equal("Envoyee", candidature!["statut"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Une_entreprise_deja_enregistree_est_reprise_sans_doublon()
    {
        await using var api = await LancerAsync();

        var premiere = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
        var deuxieme = await api.EnregistrerAvecSuccesAsync("LUMIÈRE VERTE S.A.", "Assistant", "2026-11-03T10:00");
        var troisieme = await api.EnregistrerAvecSuccesAsync("Lumière Verte NV/SA", "Magasinier", "2026-11-04T10:00");
        var autre = await api.EnregistrerAvecSuccesAsync("Lumière Verte Belgique", "Comptable", "2026-11-05T10:00");

        var idDeLaPremiere = premiere["entreprise"]!["id"]!.GetValue<int>();
        Assert.Equal(idDeLaPremiere, deuxieme["entreprise"]!["id"]!.GetValue<int>());
        Assert.Equal(idDeLaPremiere, troisieme["entreprise"]!["id"]!.GetValue<int>());
        // Le nom affiché reste celui de la première saisie.
        Assert.Equal("Lumière Verte SA", troisieme["entreprise"]!["nom"]!.GetValue<string>());
        // Une autre clé, une autre entreprise.
        Assert.NotEqual(idDeLaPremiere, autre["entreprise"]!["id"]!.GetValue<int>());

        var entreprises = await api.ListerLesEntreprisesAsync();
        Assert.Equal(["Lumière Verte SA", "Lumière Verte Belgique"], entreprises.Select(entreprise => entreprise!["nom"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Des_enregistrements_simultanes_d_une_meme_entreprise_n_en_creent_qu_une()
    {
        await using var api = await LancerAsync();
        string[] noms = ["Brasserie Lune Rousse S.R.L.", "BRASSERIE LUNE ROUSSE", "Brasserie Lune Rousse SRL", "brasserie lune rousse"];

        var reponses = await Task.WhenAll(Enumerable.Range(0, 12).Select(numero =>
            api.EnregistrerAsync(noms[numero % noms.Length], $"Poste {numero}", "2026-11-10T09:00")));

        Assert.All(reponses, reponse => Assert.Equal(HttpStatusCode.Created, reponse.StatusCode));
        Assert.Single(await api.ListerLesEntreprisesAsync());
        Assert.Equal(12, (await api.ListerAsync()).Count);
        foreach (var reponse in reponses)
        {
            reponse.Dispose();
        }
    }

    public static TheoryData<string, string?, string> SaisiesRefusees => new()
    {
        { "entreprise", null, "Indiquez l'entreprise." },
        { "entreprise", "   ", "Indiquez l'entreprise." },
        { "entreprise", new string('L', 201), "Le nom de l'entreprise ne doit pas dépasser 200 caractères." },
        { "entreprise", "!!!", "Le nom de l'entreprise doit contenir au moins une lettre ou un chiffre." },
        { "entreprise", "Lumière\tVerte", "Le nom de l'entreprise ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide." },
        { "poste", null, "Indiquez le poste." },
        { "poste", "", "Indiquez le poste." },
        { "poste", new string('p', 201), "Le poste ne doit pas dépasser 200 caractères." },
        { "poste", "Comptable\nsenior", "Le poste ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide." },
        { "envoyeeLe", null, "Indiquez la date et l'heure d'envoi." },
        { "envoyeeLe", " ", "Indiquez la date et l'heure d'envoi." },
        { "envoyeeLe", "27/03/2026 23:30", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30." },
        { "envoyeeLe", "2026-03-27", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30." },
        { "envoyeeLe", "2026-03-27T23:30Z", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30." },
        { "envoyeeLe", "2026-03-27T23:30+01:00", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30." },
        { "envoyeeLe", "2026-02-30T10:00", "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30." },
        { "envoyeeLe", "2026-11-18T14:31", "La date et l'heure d'envoi ne peuvent pas être dans le futur." },
        { "envoyeeLe", "2030-01-01T00:00", "La date et l'heure d'envoi ne peuvent pas être dans le futur." },
        { "envoyeeLe", "2026-03-29T02:30", "Cette heure n'existe pas : ce jour-là, au passage à l'heure d'été, l'horloge avance d'une heure." },
        { "envoyeeLe", "1999-12-31T23:59", "La date d'envoi ne peut pas être antérieure au 1er janvier 2000." },
    };

    [Theory]
    [MemberData(nameof(SaisiesRefusees))]
    public async Task Une_saisie_invalide_est_refusee_champ_par_champ_avec_un_message_lisible(string champ, string? valeur, string message)
    {
        await using var api = await LancerAsync();
        var saisie = new Dictionary<string, string?>
        {
            ["entreprise"] = "Lumière Verte SA",
            ["poste"] = "Comptable",
            ["envoyeeLe"] = "2026-11-02T10:00",
        };
        saisie[champ] = valeur;

        using var reponse = await api.EnregistrerAsync(saisie["entreprise"], saisie["poste"], saisie["envoyeeLe"]);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        var probleme = await AppelsDesCandidatures.LireAsync(reponse);
        Assert.Equal("La candidature n'est pas enregistrée : corrigez les champs signalés.", probleme["title"]!.GetValue<string>());
        var refus = probleme["errors"]!.AsObject();
        Assert.Equal([champ], refus.Select(paire => paire.Key));
        Assert.Equal([message], refus[champ]!.AsArray().Select(texte => texte!.GetValue<string>()));
        Assert.Empty(await api.ListerAsync());
        Assert.Empty(await api.ListerLesEntreprisesAsync());
    }

    [Fact]
    public async Task Tous_les_champs_refuses_sont_signales_ensemble()
    {
        await using var api = await LancerAsync();

        using var reponse = await api.EnvoyerCorpsAsync("{}");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var refus = (await AppelsDesCandidatures.LireAsync(reponse))["errors"]!.AsObject();
        Assert.Equal(["entreprise", "poste", "envoyeeLe"], refus.Select(paire => paire.Key));
    }

    [Theory]
    [InlineData("""{"entreprise":"Lumière Verte SA","poste":"Comptable","envoyeeLe":"2026-11-02T10:00","statut":"Acceptee"}""", "application/json")]
    [InlineData("""{"entreprise":"Lumière Verte SA","poste":"Comptable","envoyeeLe":"2026-11-02T10:00","id":42}""", "application/json")]
    [InlineData("""{"entreprise":"Lumière Verte SA","poste":42,"envoyeeLe":"2026-11-02T10:00"}""", "application/json")]
    [InlineData("""{"entreprise":"Lumière Verte SA",""", "application/json")]
    [InlineData("pas du JSON", "application/json")]
    [InlineData("", "application/json")]
    public async Task Un_corps_illisible_ou_un_champ_que_l_interface_n_envoie_pas_est_refuse(string corps, string type)
    {
        await using var api = await LancerAsync();

        using var reponse = await api.EnvoyerCorpsAsync(corps, type);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("La requête est illisible.", (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        Assert.Empty(await api.ListerAsync());
    }

    [Fact]
    public async Task Un_corps_qui_n_est_pas_du_JSON_est_refuse()
    {
        await using var api = await LancerAsync();

        using var reponse = await api.EnvoyerCorpsAsync("""{"entreprise":"Lumière Verte SA","poste":"Comptable","envoyeeLe":"2026-11-02T10:00"}""", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reponse.StatusCode);
        Assert.Empty(await api.ListerAsync());
    }

    [Fact]
    public async Task Les_candidatures_se_retrouvent_au_lancement_suivant()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            void SurLeMemeDossier(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
            {
                new HorlogeDeTest().Utiliser(builder);
                builder.ConfigureTestServices(services => services.AddSingleton(new DossierDeLaBase(dossier)));
            }

            await using (var premier = await ApiLancee.LancerAsync(SurLeMemeDossier))
            {
                await premier.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-02T10:00");
            }

            await using var second = await ApiLancee.LancerAsync(SurLeMemeDossier);
            var candidature = Assert.Single(await second.ListerAsync())!;
            Assert.Equal("Comptable", candidature["poste"]!.GetValue<string>());
            Assert.Equal("2026-11-02T09:00:00Z", candidature["envoyeeLe"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    [Fact]
    public async Task Un_texte_qui_ressemble_a_du_HTML_est_garde_et_rendu_tel_quel()
    {
        await using var api = await LancerAsync();
        const string Entreprise = "<img src=x onerror=alert(1)> & Fils";
        const string Poste = "<b>Développeuse</b> \"senior\"";

        await api.EnregistrerAvecSuccesAsync(Entreprise, Poste, "2026-11-02T10:00");

        var candidature = Assert.Single(await api.ListerAsync())!;
        Assert.Equal(Entreprise, candidature["entreprise"]!["nom"]!.GetValue<string>());
        Assert.Equal(Poste, candidature["poste"]!.GetValue<string>());
    }

    private static Task<ApiLancee> LancerAsync() => ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
}
