// Prototype jetable de la story #9 : la fenêtre hôte de l'ADR-0003 (WinForms + WebView2), Kestrel et SQLite
// dans un seul exe. Hors de la solution livrée ; voir README.md.
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Prototype9;

static class Program
{
    // Contrôles qu'un autotest réussi doit tous contenir, au vert.
    static readonly string[] ControlesAttendus =
    [
        "sqlite-dossier-temporaire",
        "sqlite-lue-par-la-page",
        "page-locale",
        "message-page-vers-hote",
        "message-hote-vers-page",
        "navigation-autre-site-annulee",
        "navigation-meme-serveur-autre-origine-annulee",
        "fenetre-ouverte",
        "capture",
        "exe-autonome-un-seul-fichier",
    ];

    [STAThread]
    static int Main(string[] args)
    {
        var lancement = Stopwatch.StartNew();
        var autotest = args.Contains("--autotest");
        var cheminRapport = Option(args, "--rapport") ?? Path.Combine(AppContext.BaseDirectory, "rapport-autotest.json");
        var cheminCapture = Option(args, "--capture") ?? Path.ChangeExtension(cheminRapport, ".png");
        var rapport = new Rapport(cheminRapport);

        // Garde-fous de l'autotest : jamais de boîte de dialogue, jamais d'attente sans fin.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => rapport.Abandonner("exception non gérée : " + e.Exception.Message);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => rapport.Abandonner("exception non gérée : " + e.ExceptionObject);
        using var chienDeGarde = autotest
            ? new System.Threading.Timer(_ => rapport.Abandonner("délai de 90 s dépassé"), null, TimeSpan.FromSeconds(90), Timeout.InfiniteTimeSpan)
            : null;

        var dossierTemporaire = Directory.CreateTempSubdirectory("sollicito-proto9-").FullName;
        WebApplication? serveur = null;
        try
        {
            rapport.Etape("dossier temporaire " + Rapport.Masquer(dossierTemporaire));

            // Base SQLite créée dans le dossier temporaire, fermée, puis rouverte en lecture seule.
            var cheminBase = Base.Creer(dossierTemporaire);
            var lecture = Base.Lire(cheminBase);
            rapport.Controler("sqlite-dossier-temporaire",
                lecture.Candidatures.Count == 2 && cheminBase.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
                new { fichier = Rapport.Masquer(cheminBase), versionSqlite = lecture.Version, lignesLues = lecture.Candidatures.Count });

            // Kestrel sur 127.0.0.1, port libre choisi au lancement ; il sert la page et l'API à la même origine.
            var requetes = new ConcurrentQueue<Requete>();
            serveur = Serveur.Construire(cheminBase, requetes);
            serveur.StartAsync().GetAwaiter().GetResult();
            var origine = new Uri(serveur.Services.GetRequiredService<IServer>()
                .Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single());
            rapport.Etape("Kestrel écoute sur " + origine);

            ApplicationConfiguration.Initialize();
            var contexte = new Contexte
            {
                Rapport = rapport,
                Origine = origine,
                DossierProfil = Path.Combine(dossierTemporaire, "WebView2"),
                Autotest = autotest,
                CheminCapture = cheminCapture,
                Lancement = lancement,
                LignesEnBase = lecture.Candidatures.Count,
            };
            using (var fenetre = new FenetreHote(contexte))
            {
                Application.Run(fenetre);
            }
            rapport.Etape("fenêtre fermée");

            // Les modules sont relevés une fois WebView2 et SQLite chargés.
            var (execution, autonome) = Execution.Decrire();
            rapport.Section("execution", execution);
            rapport.Controler("exe-autonome-un-seul-fichier", autonome, new
            {
                regle = "CoreLib dans le bundle (emplacement vide), aucune assembly lue sur le disque, aucun module de l'installation .NET du poste",
            });

            AttendreFinNavigateur(contexte.IdProcessusNavigateur);
            rapport.Etape("processus du navigateur terminé");
            Task.Run(() => serveur.StopAsync()).GetAwaiter().GetResult();
            rapport.Etape("Kestrel arrêté");
            var recues = requetes.ToArray();
            rapport.Section("requetesRecuesParKestrel", recues);
            // Constat, pas un critère : l'annulation garde la page intacte, mais la requête GET de la navigation
            // peut partir pendant que l'hôte répond (documentation de CoreWebView2NavigationStartingEventArgs.Cancel).
            // Une requête reçue sous un autre en-tête Host vient donc de la navigation annulée vers localhost.
            rapport.Section("constatNavigationAnnulee", new
            {
                requetesRecuesSousUnAutreHote = recues.Where(r => r.Hote != origine.Authority).ToArray(),
                lecture = "une requête listée ici est partie malgré l'annulation ; le serveur doit refuser lui-même un en-tête Host inattendu (ADR-0001, D9)",
            });
        }
        catch (Exception ex)
        {
            rapport.Erreur("hôte", ex);
        }
        finally
        {
            if (serveur is not null)
            {
                Task.Run(() => serveur.DisposeAsync().AsTask()).GetAwaiter().GetResult();
            }
            SqliteConnection.ClearAllPools();
            rapport.Section("nettoyage", Nettoyer(dossierTemporaire));
            rapport.Etape("dossier temporaire nettoyé");
        }

        return autotest ? rapport.Ecrire(ControlesAttendus) : 0;
    }

    static string? Option(string[] args, string nom)
    {
        var i = Array.IndexOf(args, nom);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Le dossier de profil reste verrouillé tant que le processus du navigateur tourne.
    static void AttendreFinNavigateur(uint? idProcessus)
    {
        if (idProcessus is not uint id)
        {
            return;
        }
        try
        {
            using var navigateur = Process.GetProcessById((int)id);
            navigateur.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (ArgumentException)
        {
            // Déjà terminé.
        }
    }

    static object Nettoyer(string dossier)
    {
        Exception? derniere = null;
        for (var essai = 1; essai <= 10; essai++)
        {
            try
            {
                Directory.Delete(dossier, recursive: true);
                return new { dossierTemporaireSupprime = true, essais = essai };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                derniere = ex;
                Thread.Sleep(500);
            }
        }
        return new { dossierTemporaireSupprime = false, erreur = Rapport.Masquer(derniere?.Message) };
    }
}

/// <summary>État partagé entre l'hôte et la fenêtre.</summary>
sealed class Contexte
{
    public required Rapport Rapport { get; init; }
    public required Uri Origine { get; init; }
    public required string DossierProfil { get; init; }
    public required bool Autotest { get; init; }
    public required string CheminCapture { get; init; }
    public required Stopwatch Lancement { get; init; }
    public required int LignesEnBase { get; init; }
    public uint? IdProcessusNavigateur { get; set; }
}
