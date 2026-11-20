using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les journaux des sauvegardes (#26) : un chemin de sauvegarde porte en général le nom du compte Windows (#80, point 1).
/// Ni le dossier de données, ni le dossier des sauvegardes, ni une copie, ni une donnée de la base n'entrent dans un
/// journal, sur aucun chemin : copies réussies et manquées, dossier choisi, refusé ou manquant, réglage illisible, copie
/// d'avant une mise à jour, migration ratée, et l'échec d'un lancement que l'hôte écrit en entier. Chaque dossier porte un
/// nom qui imite un compte Windows. Toutes les données sont fictives.
/// </summary>
public sealed class JournauxDesSauvegardesTests : IAsyncLifetime
{
    private const string Compte = "Compte.Fictif-Dupont";
    private const string Donnee = "Donnée-Fictive-Sauvegarde";

    // Chaque catégorie au niveau le plus bavard, y compris par un réglage propre au fournisseur de test.
    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    private readonly string _racine = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
    private readonly CaptureDesJournaux _capture = new();
    private readonly List<string> _installations = [];

    private string Profil => Path.Combine(_racine, "Users", Compte);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_racine);

    [Fact]
    public async Task Aucun_chemin_ni_aucune_donnee_n_entre_dans_un_journal_des_sauvegardes()
    {
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        var cle = Path.Combine(Profil, "Cle USB", "Sauvegardes");
        Directory.CreateDirectory(donnees);
        Directory.CreateDirectory(cle);
        // Une base d'avant #25 : son lancement fait la copie d'avant une mise à jour, puis la migration sur une copie.
        await BaseDe12.PeuplerAsync(new DossierDeLaBase(donnees));

        await using (var api = await LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser))
        {
            await api.AttendreLaPremiereVerificationAsync();
            _installations.Add(api.Sauvegardes().Installation);
            await api.EnregistrerAvecSuccesAsync(Donnee, Donnee, "2026-11-02T10:00");
            await api.SauvegarderMaintenantAsync();
            await api.LireLesSauvegardesAsync();

            // Un dossier choisi, puis un dossier absent, refusé.
            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(cle, TestContext.Current.CancellationToken));
            Assert.Equal(IssueDuChoix.Refuse, await api.Sauvegardes().ChoisirLeDossierAsync(Path.Combine(Profil, "Absent"), TestContext.Current.CancellationToken));

            // La clé retirée, puis rebranchée.
            await SupprimerAsync(cle);
            await api.SauvegarderMaintenantAsync();
            await api.Sauvegardes().FaireLaCopieDuJourSiBesoinAsync(TestContext.Current.CancellationToken);
            Directory.CreateDirectory(cle);
            await api.Sauvegardes().FaireLaCopieDuJourSiBesoinAsync(TestContext.Current.CancellationToken);

            // Une copie qui s'écrit, mais ne peut pas prendre son nom.
            var copie = Path.Combine(cle, api.CopieDuJour(new DateOnly(2026, 11, 18)));
            await EffacerAsync(copie);
            Directory.CreateDirectory(copie);
            await api.SauvegarderMaintenantAsync();
            Directory.Delete(copie);

            // Le retour au dossier par défaut, réussi puis impossible.
            using (await api.RevenirAuDossierParDefautAsync())
            {
            }

            Assert.Equal(IssueDuChoix.Choisi, await api.Sauvegardes().ChoisirLeDossierAsync(cle, TestContext.Current.CancellationToken));
            var parDefaut = new DossierDeLaBase(donnees).DossierDesSauvegardes;
            await SupprimerAsync(parDefaut);
            await File.WriteAllTextAsync(parDefaut, "fictif", TestContext.Current.CancellationToken);
            using (await api.RevenirAuDossierParDefautAsync())
            {
            }
        }

        // Un réglage illisible, sur un autre dossier de données.
        var illisible = Path.Combine(Profil, "AppData", "Local", "Sollicito-Illisible");
        Directory.CreateDirectory(illisible);
        await File.WriteAllTextAsync(Path.Combine(illisible, ReglagesDeLApplication.NomDuFichier), $$"""{ "dossierDesSauvegardes": "{{Compte}}" """, TestContext.Current.CancellationToken);
        await using (var api = await LancerSurAsync(illisible, new HorlogeDeTest(), ToutJournaliser))
        {
            Assert.True((await api.AttendreLaPremiereVerificationAsync()).ReglageIllisible);
            _installations.Add(api.Sauvegardes().Installation);
        }

        var entrees = _capture.Entrees.ToArray();
        // Témoins : chaque chemin a bien été parcouru.
        Assert.Contains(entrees, entree => entree.Contains("Base mise à jour sur une copie", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie du jour faite", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie du jour manquée (DossierIntrouvable)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Copie du jour manquée (UnauthorizedAccessException)", StringComparison.Ordinal) || entree.Contains("Copie du jour manquée (IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Dossier des sauvegardes choisi", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Dossier des sauvegardes refusé", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Réglages illisibles", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    [Fact]
    public async Task Une_mise_a_jour_ratee_et_son_echec_de_lancement_n_ecrivent_aucun_chemin()
    {
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        Directory.CreateDirectory(donnees);
        await BaseDe12.PeuplerAsync(new DossierDeLaBase(donnees));
        using (var connexion = Ouvrir(new DossierDeLaBase(donnees).Fichier))
        {
            connexion.Executer("""CREATE INDEX "IX_EvenementsDeCandidature_EtapeCorrigeeId" ON "EvenementsDeCandidature" ("Type")""");
        }

        await Assert.ThrowsAnyAsync<Exception>(() => LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser));

        // Puis une copie d'avant impossible : un fichier à la place du dossier des sauvegardes.
        var parDefaut = new DossierDeLaBase(donnees).DossierDesSauvegardes;
        await SupprimerAsync(parDefaut);
        await File.WriteAllTextAsync(parDefaut, "fictif", TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser));

        var entrees = _capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("Mise à jour de la base échouée (Migration, SqliteException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Mise à jour de la base échouée (CopieDAvant", StringComparison.Ordinal));
        // L'hôte écrit l'échec du lancement avec toute la chaîne de l'exception.
        Assert.Contains(entrees, entree => entree.Contains("Hosting failed to start", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    [Fact]
    public async Task Un_dossier_de_donnees_qui_ne_peut_pas_se_creer_n_ecrit_pas_son_chemin_dans_le_journal_de_l_hote()
    {
        // La sonde de #80, point 1 : un fichier qui porte le nom du dossier de données.
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        Directory.CreateDirectory(Path.GetDirectoryName(donnees)!);
        await File.WriteAllTextAsync(donnees, "fictif", TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser));

        var entrees = _capture.Entrees.ToArray();
        Assert.Contains(entrees, entree => entree.Contains("La base de données n'a pas pu être créée ni mise à jour (IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Hosting failed to start", StringComparison.Ordinal));
        SansCheminNiDonnee(entrees);
    }

    private void ToutJournaliser(IWebHostBuilder builder) =>
        builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LesPlusBavards))
            .ConfigureLogging(journaux => journaux.AddProvider(_capture));

    // Ni le compte, ni un dossier ou une copie, ni la donnée, ni le nom d'une copie (son identifiant d'installation).
    private void SansCheminNiDonnee(string[] entrees)
    {
        Assert.NotEmpty(entrees);
        string[] interdits = [Compte, _racine, Donnee, "Cle USB", .. _installations];
        foreach (var interdit in interdits)
        {
            Assert.DoesNotContain(entrees, entree => entree.Contains(interdit, StringComparison.OrdinalIgnoreCase));
        }
    }
}
