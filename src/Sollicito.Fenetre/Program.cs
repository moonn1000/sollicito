using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api;
using Sollicito.Api.Export;
using Sollicito.Api.Relances;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Securite;

namespace Sollicito.Fenetre;

/// <summary>
/// Le lancement de Sollicito : un seul exe démarre l'API locale, puis ouvre la fenêtre sur l'interface, servie à la
/// même origine que l'API (ADR-0003, « Montage »). Fermer la fenêtre arrête l'API.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        // L'Application de WinForms, écrite en entier : ici, « Application » désignerait l'espace de noms
        // Sollicito.Application, celui de la couche application.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, _) =>
        {
            Signalement.Afficher("Sollicito a rencontré une erreur inattendue et va se fermer.");
            Environment.Exit(1);
        };

        Lancement lancement;
        WebApplication api;
        try
        {
            lancement = Lancement.Lire();
            var fichiers = InterfaceEmbarquee.Fichiers();
            if (!InterfaceEmbarquee.EstPresente(fichiers))
            {
                Signalement.Afficher("Cet exe a été construit sans son interface : construisez-la (pnpm --dir interface run build), puis l'exe.");
                return 1;
            }

            Directory.CreateDirectory(lancement.DossierDeDonnees);
            api = ApiLocale.ConstruirePourLaFenetre(fichiers, lancement.DossierDeDonnees, lancement.EtapesDeLaRestauration, lancement.FaussesReponsesDeLIA);
            api.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Le message ne reprend pas l'exception, qui peut citer un chemin du poste ; seule une mise à jour de la base
            // ratée a le sien, écrit pour l'utilisateur et sans chemin (#26).
            Signalement.Afficher(MessagesDuLancement.Pour(erreur));
            return 1;
        }

        try
        {
            var origine = new Uri(api.Services.GetRequiredService<AdresseDEcoute>().Origine);
            var jeton = api.Services.GetRequiredService<JetonDeLancement>();
            var sauvegardes = api.Services.GetRequiredService<SauvegardesDeLaBase>();
            var brouillons = api.Services.GetRequiredService<BrouillonsDesRelances>();
            var restauration = api.Services.GetRequiredService<RestaurationDeLaBase>();
            var export = api.Services.GetRequiredService<ExportDesDonnees>();
            using var fenetre = new FenetreHote(origine, jeton, lancement, sauvegardes, brouillons, restauration, export);
            System.Windows.Forms.Application.Run(fenetre);
            return fenetre.CodeDeSortie;
        }
        finally
        {
            // Fermer la fenêtre arrête l'API : plus rien n'écoute quand l'exe se termine. Hors du fil de la fenêtre,
            // dont le contexte de synchronisation bloquerait l'attente.
            Task.Run(async () =>
            {
                await api.StopAsync();
                await api.DisposeAsync();
            }).GetAwaiter().GetResult();
        }
    }
}
