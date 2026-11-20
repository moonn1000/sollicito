using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using Sollicito.Api.Candidatures;
using Sollicito.Api.Donnees;
using Sollicito.Api.Erreurs;
using Sollicito.Api.Interface;
using Sollicito.Api.Journaux;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Securite;
using Sollicito.Application.Candidatures;
using Sollicito.Infrastructure;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api;

/// <summary>
/// La Web API locale (ADR-0001, D2 et D9 ; ADR-0003) : elle n'écoute que sur 127.0.0.1, exige le jeton du
/// lancement sur chaque route et refuse elle-même les en-têtes <c>Host</c> et les origines inattendus. Aucune
/// politique CORS ne l'ouvre à une autre origine : CORS assouplit la règle de même origine, il ne protège pas
/// le serveur. La fenêtre hôte (src/Sollicito.Fenetre) la construit par <see cref="ConstruirePourLaFenetre"/>,
/// puis lit dans ses services l'adresse d'écoute et le jeton à remettre à l'interface.
/// </summary>
public static class ApiLocale
{
    /// <summary>
    /// Construit l'API seule, prête à démarrer, sans les fichiers de l'interface. Sa base vit dans le dossier de
    /// données de l'utilisateur (<see cref="DossierDeDonnees"/>), développement compris.
    /// </summary>
    public static WebApplication Construire(string[] args) =>
        Construire(new WebApplicationOptions { Args = args }, DossierDeDonnees.DeLUtilisateur(), ajuster: null);

    /// <summary>
    /// Construit l'API de la fenêtre hôte, qui sert aussi les fichiers de l'interface. L'environnement de production
    /// est imposé, quelles que soient les variables d'environnement, car la page d'exception du développement
    /// afficherait les en-têtes d'une requête, jeton compris. La ligne de commande de l'exe ne lui est pas transmise,
    /// et aucun fichier de configuration n'est lu, ni à côté de l'exe ni dans le dossier courant. Les variables
    /// d'environnement gardent leur effet ordinaire sur le reste (journaux, limites de Kestrel) : seul un programme
    /// du même compte Windows peut les poser.
    /// </summary>
    /// <param name="fichiersDeLInterface">Les fichiers de l'interface React construite (index.html, assets).</param>
    /// <param name="dossierDeDonnees">Le dossier de données de l'utilisateur, déjà créé : la racine du contenu et le dossier de la base.</param>
    public static WebApplication ConstruirePourLaFenetre(IFileProvider fichiersDeLInterface, string dossierDeDonnees)
    {
        ArgumentNullException.ThrowIfNull(fichiersDeLInterface);
        ArgumentException.ThrowIfNullOrEmpty(dossierDeDonnees);

        return Construire(
            new WebApplicationOptions
            {
                // Aucun fichier de configuration à surveiller : sans ce réglage, la racine du contenu, donc tout le
                // dossier de données et le profil du navigateur, resterait observée pendant toute la session.
                Args = ["--hostBuilder:reloadConfigOnChange=false"],
                EnvironmentName = Environments.Production,
                ContentRootPath = dossierDeDonnees,
            },
            dossierDeDonnees,
            builder =>
            {
                // appsettings.json et ses variantes : un fichier déposé à côté de l'exe, par exemple par un
                // téléchargement dans le même dossier, ne doit rien régler.
                foreach (var source in builder.Configuration.Sources.OfType<FileConfigurationSource>().ToArray())
                {
                    builder.Configuration.Sources.Remove(source);
                }

                builder.Services.AddSingleton(new FichiersDeLInterface(fichiersDeLInterface));
            });
    }

    private static WebApplication Construire(WebApplicationOptions options, string dossierDeDonnees, Action<WebApplicationBuilder>? ajuster)
    {
        var builder = WebApplication.CreateBuilder(options);
        ajuster?.Invoke(builder);

        // 127.0.0.1 et un port libre choisi au lancement, jamais une interface réseau. L'écoute ne se règle que
        // dans le code : la section Kestrel de la configuration est ignorée, et rien ne la relit à chaud.
        // ControleDeLEcoute refuse le reste.
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        ReglesDesJournaux.Appliquer(builder.Logging);
        // Ni « Content root path » ni « Hosting environment » : le premier écrit un chemin du profil Windows, donc le
        // nom du compte. « Now listening on », qui vient du serveur web, reste écrit.
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        builder.Services.AddSingleton<JetonDeLancement>();
        builder.Services.AddSingleton<AdresseDEcoute>();
        builder.Services.AddHostedService<ControleDeLEcoute>();
        builder.Services.AddOpenApi();

        // La base, créée ou mise à jour au lancement, après les contrôles de l'écoute et avant que Kestrel n'écoute.
        builder.Services.AjouterLaBase(new DossierDeLaBase(dossierDeDonnees));
        builder.Services.AddHostedService<PreparationDeLaBase>();
        builder.Services.AddSingleton(TimeProvider.System);
        // Les copies du jour (#26), une fois la base prête : leur dossier se lit dans celui de la base, au premier usage.
        // Enregistrées après la préparation de la base, elles s'arrêtent avant elle : la copie de la fermeture se fait
        // sous le verrou du dossier.
        builder.Services.AddSingleton<SauvegardesDeLaBase>();
        builder.Services.AddHostedService<SauvegardesAutomatiques>();
        builder.Services.AddScoped<EnregistrementDesCandidatures>();
        builder.Services.AddScoped<SuiviDesCandidatures>();
        builder.Services.AddScoped<ModificationDesCandidatures>();

        // Les statuts s'écrivent en toutes lettres. Un champ inconnu du corps rend la requête illisible plutôt que
        // d'être ignoré : le statut, par exemple, ne vient jamais de l'interface.
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        // Un corps illisible lève une exception dans tous les environnements, et ErreursInattendues y répond.
        builder.Services.Configure<RouteHandlerOptions>(routes => routes.ThrowOnBadRequest = true);

        var app = builder.Build();

        // Aucune exception ne remonte au-delà : en développement, la page d'exception d'ASP.NET Core, placée avant
        // tout le reste, afficherait les en-têtes de la requête, jeton compris.
        app.UseMiddleware<ErreursInattendues>();
        // Chaque réponse, refus compris, porte la politique de sécurité du contenu et les autres en-têtes de sécurité.
        app.UseMiddleware<EnTetesDeSecurite>();
        // Ordre des refus : Host (400) pour toute requête, puis jeton (401) et Origin (403) pour l'API.
        app.UseMiddleware<ControleDeLHote>();
        // Les fichiers de l'interface, servis sans jeton : seul leur Host est contrôlé (ADR-0003, « Règles d'accès »).
        FichiersDeLInterface.Servir(app);
        app.UseMiddleware<ControleDuJeton>();
        app.UseMiddleware<ControleDeLOrigine>();
        // Le routage ne vient qu'après les contrôles : une requête refusée n'est jamais routée (OrdreDesControlesTests).
        app.UseRouting();

        app.MapOpenApi("/api/openapi/{documentName}.json");
        app.MapGet("/api/etat", () => TypedResults.Ok(EtatDeLApplication.Pret))
            .WithName("EtatDeLApplication")
            .WithSummary("État de l'application");
        RoutesDesCandidatures.Declarer(app);
        RoutesDesSauvegardes.Declarer(app);

        return app;
    }
}
