using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Export;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Export;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les journaux de l'export (#49, ADR-0020), au niveau Trace, toutes catégories : le fichier choisi porte en général le nom
/// du compte Windows, et son nom peut porter celui de l'utilisateur. Ni un chemin, ni un nom de fichier, ni une donnée de
/// la base, ni le message d'une exception n'entrent dans un journal, sur aucun chemin de l'export : réussi, remplacé,
/// refusé pour chaque motif, impossible pour chaque erreur du disque, interrompu. Chaque dossier porte un nom qui imite un
/// compte Windows ; toutes les données sont fictives.
/// </summary>
public sealed class JournauxDeLExportTests : IAsyncLifetime
{
    private const string Compte = "Compte.Fictif-Dupont";
    private const string Donnee = "Donnée-Fictive-Export";
    private const string NomDuFichier = "export-de-Jeanne-Fictive";

    // Chaque catégorie au niveau le plus bavard, y compris par un réglage propre au fournisseur de test.
    private static readonly KeyValuePair<string, string?>[] LesPlusBavards =
    [
        new("Logging:LogLevel:Default", "Trace"),
        new($"Logging:{typeof(CaptureDesJournaux).FullName}:LogLevel:Default", "Trace"),
    ];

    private readonly string _racine = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
    private readonly CaptureDesJournaux _capture = new();
    private readonly EtapesDeTest _etapes = new();

    private string Profil => Path.Combine(_racine, "Users", Compte);

    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_racine);

    [Fact]
    public async Task Aucun_chemin_aucun_nom_de_fichier_ni_aucune_donnee_n_entre_dans_un_journal_de_l_export()
    {
        var donnees = Path.Combine(Profil, "AppData", "Local", "Sollicito");
        var documents = Path.Combine(Profil, "OneDrive", "Documents");
        Directory.CreateDirectory(donnees);
        Directory.CreateDirectory(documents);
        string installation;
        await using (var api = await LancerSurAsync(donnees, new HorlogeDeTest(), ToutJournaliser))
        {
            await api.AttendreLaPremiereVerificationAsync();
            installation = api.Sauvegardes().Installation;
            var candidature = await api.EnregistrerAvecSuccesAsync(Donnee, Donnee, "2026-11-02T10:00");
            await api.ChangerLeStatutAvecSuccesAsync(AppelsDuSuivi.IdDe(candidature), "Entretien");
            var export = api.Services.GetRequiredService<ExportDesDonnees>();
            var fichier = Path.Combine(documents, NomDuFichier + ".json");

            // Réussi, puis remplacé avec le oui, puis refusé sans lui.
            Assert.Equal(IssueDeLExport.Exporte, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: false, Annulation));
            Assert.Equal(IssueDeLExport.Exporte, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));
            Assert.Equal(IssueDeLExport.Existe, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: false, Annulation));

            // Refusé pour chaque motif : l'extension, le dossier de données, un chemin relatif, la porte fermée.
            Assert.Equal(IssueDeLExport.Extension, await export.ExporterAsync(FormatDExport.Json, Path.Combine(documents, NomDuFichier + ".txt"), remplacer: false, Annulation));
            Assert.Equal(IssueDeLExport.DossierDeDonnees, await export.ExporterAsync(FormatDExport.Json, Path.Combine(donnees, NomDuFichier + ".json"), remplacer: true, Annulation));
            Assert.Equal(IssueDeLExport.Impossible, await export.ExporterAsync(FormatDExport.Json, NomDuFichier + ".json", remplacer: false, Annulation));
            var porte = api.Services.GetRequiredService<PorteDeLaBase>();
            Assert.True(await porte.FermerAsync(TimeSpan.FromSeconds(10), TimeProvider.System, Annulation));
            try
            {
                Assert.Equal(IssueDeLExport.Occupe, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));
            }
            finally
            {
                porte.Ouvrir();
            }

            // Impossible : un dossier disparu, un fichier en lecture seule, la place qui manque, dont les erreurs citent
            // le chemin dans leur message.
            Assert.Equal(IssueDeLExport.DossierIntrouvable, await export.ExporterAsync(FormatDExport.Json, Path.Combine(Profil, "Cle USB", NomDuFichier + ".json"), remplacer: false, Annulation));
            File.SetAttributes(fichier, FileAttributes.ReadOnly);
            try
            {
                Assert.Equal(IssueDeLExport.AccesRefuse, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));
            }
            finally
            {
                File.SetAttributes(fichier, FileAttributes.Normal);
            }

            _etapes.Erreur = (EtapeDeLExport.CandidaturesEcrites, new IOException($"Disque plein (essai) : {fichier} {Donnee}", unchecked((int)0x80070070)));
            Assert.Equal(IssueDeLExport.PlaceManquante, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));
            _etapes.Erreur = (EtapeDeLExport.ContactsEcrits, new InvalidOperationException($"Erreur imprévue (essai) : {fichier} {Donnee}"));
            Assert.Equal(IssueDeLExport.Impossible, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));

            // Un provisoire tenu par un autre programme au moment de l'effacer : il reste, et le journal le dit, sans chemin.
            FileStream? tenu = null;
            _etapes.Action = etape =>
            {
                if (etape == EtapeDeLExport.FichierEcrit)
                {
                    var provisoire = Directory.GetFiles(documents, ExportDesDonnees.PrefixeDuProvisoire + "*" + ExportDesDonnees.SuffixeDuProvisoire).Single();
                    tenu = new FileStream(provisoire, FileMode.Open, FileAccess.Read, FileShare.None);
                }
            };
            _etapes.Erreur = (EtapeDeLExport.FichierEcrit, new IOException($"Erreur imprévue (essai) : {fichier} {Donnee}"));
            try
            {
                Assert.Equal(IssueDeLExport.Impossible, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, Annulation));
            }
            finally
            {
                var resteDuProvisoire = tenu?.Name;
                tenu?.Dispose();
                if (resteDuProvisoire is not null)
                {
                    File.Delete(resteDuProvisoire);
                }
            }

            _etapes.Action = null;
            _etapes.Erreur = null;

            // Interrompu, la fenêtre qui se ferme ; puis l'annulation dans le dialogue, et une fermeture sans attendre.
            using var fermeture = new CancellationTokenSource();
            _etapes.Action = etape =>
            {
                if (etape == EtapeDeLExport.OffresEcrites)
                {
                    fermeture.Cancel();
                }
            };
            Assert.Equal(IssueDeLExport.Annule, await export.ExporterAsync(FormatDExport.Json, fichier, remplacer: true, fermeture.Token));
            _etapes.Action = null;
            export.NoterLAnnulation(FormatDExport.Json);
            export.NoterLaFermetureAvantLaFin(FormatDExport.Json);
        }

        var entrees = _capture.Entrees.ToArray();
        // Témoins : chaque chemin a bien été parcouru, et l'entrée dit l'issue, les comptes et la durée.
        Assert.Contains(entrees, entree => entree.Contains("Export Json fait : 1 entreprise(s), 0 contact(s), 0 offre(s), 1 candidature(s), 0 relance(s) et 1 événement(s), en ", StringComparison.Ordinal) && entree.Contains("fichier existant remplacé : False", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("fichier existant remplacé : True", StringComparison.Ordinal));
        foreach (var motif in new[] { "Extension", "DossierDeDonnees", "CheminRelatif", "RestaurationEnCours" })
        {
            Assert.Contains(entrees, entree => entree.Contains($"Export Json refusé ({motif})", StringComparison.Ordinal));
        }

        Assert.Contains(entrees, entree => entree.Contains("(Renommage, IOException, Existe)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("(Ouverture, DirectoryNotFoundException, DossierIntrouvable)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("(Renommage, UnauthorizedAccessException, AccesRefuse)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("(Ecriture, IOException, PlaceManquante)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("(Ecriture, InvalidOperationException, Impossible)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Export Json interrompu par la fermeture de la fenêtre", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Export Json annulé dans le dialogue", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("Le fichier provisoire d'un export Json n'a pas pu être effacé (IOException)", StringComparison.Ordinal));
        Assert.Contains(entrees, entree => entree.Contains("La fenêtre s'est fermée avant la fin d'un export Json", StringComparison.Ordinal));

        // Ni le compte, ni un dossier, ni le nom du fichier, ni la donnée, ni l'identifiant d'installation, ni le message
        // d'une erreur.
        Assert.NotEmpty(entrees);
        string[] interdits = [Compte, _racine, NomDuFichier, Donnee, "Cle USB", "OneDrive", "Disque plein (essai)", "Erreur imprévue (essai)", installation, ExportDesDonnees.PrefixeDuProvisoire];
        foreach (var interdit in interdits)
        {
            Assert.DoesNotContain(entrees, entree => entree.Contains(interdit, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ToutJournaliser(IWebHostBuilder builder) =>
        builder
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LesPlusBavards))
            .ConfigureLogging(journaux => journaux.AddProvider(_capture))
            .ConfigureTestServices(services => services.AddSingleton<IEtapesDeLExport>(_etapes));

    /// <summary>Les étapes de l'export, où le test agit ou lève une erreur.</summary>
    private sealed class EtapesDeTest : IEtapesDeLExport
    {
        public Action<EtapeDeLExport>? Action { get; set; }

        public (EtapeDeLExport Etape, Exception Erreur)? Erreur { get; set; }

        public void Atteinte(EtapeDeLExport etape)
        {
            Action?.Invoke(etape);
            if (Erreur is { } erreur && erreur.Etape == etape)
            {
                throw erreur.Erreur;
            }
        }
    }
}
