using System.Net;
using Sollicito.Api.Journaux;
using Sollicito.Api.Securite;

namespace Sollicito.Api;

/// <summary>
/// La Web API locale (ADR-0001, D2 et D9 ; ADR-0003) : elle n'écoute que sur 127.0.0.1, exige le jeton du
/// lancement sur chaque route et refuse elle-même les en-têtes <c>Host</c> et les origines inattendus. Aucune
/// politique CORS ne l'ouvre à une autre origine : CORS assouplit la règle de même origine, il ne protège pas
/// le serveur. La fenêtre hôte (#13) lira dans ses services l'adresse d'écoute et le jeton à remettre à l'interface.
/// </summary>
public static class ApiLocale
{
    /// <summary>Construit l'API, prête à démarrer.</summary>
    public static WebApplication Construire(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 127.0.0.1 et un port libre choisi au lancement, jamais une interface réseau. L'écoute ne se règle que
        // dans le code : la section Kestrel de la configuration est ignorée, et rien ne la relit à chaud.
        // ControleDeLEcoute refuse le reste.
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false);
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        ReglesDesJournaux.Appliquer(builder.Logging);

        builder.Services.AddSingleton<JetonDeLancement>();
        builder.Services.AddSingleton<AdresseDEcoute>();
        builder.Services.AddHostedService<ControleDeLEcoute>();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // Ordre des refus : Host (400) pour toute requête, puis jeton (401) et Origin (403) pour l'API.
        app.UseMiddleware<ControleDeLHote>();
        // Les fichiers de l'interface (#13) se placeront ici : servis sans jeton, seul leur Host contrôlé (ADR-0003).
        app.UseMiddleware<ControleDuJeton>();
        app.UseMiddleware<ControleDeLOrigine>();
        // Le routage ne vient qu'après les contrôles : une requête refusée n'est jamais routée (OrdreDesControlesTests).
        app.UseRouting();

        app.MapOpenApi("/api/openapi/{documentName}.json");
        app.MapGet("/api/etat", () => TypedResults.Ok(EtatDeLApplication.Pret))
            .WithName("EtatDeLApplication")
            .WithSummary("État de l'application");

        return app;
    }
}
