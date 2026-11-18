using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prototype9;

record Requete(string Methode, string Hote, string Chemin);

/// <summary>Kestrel dans le même processus que la fenêtre : la page et l'API à la même origine (ADR-0003, « Montage »).</summary>
static class Serveur
{
    public static WebApplication Construire(string cheminBase, ConcurrentQueue<Requete> requetes)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        app.Use(async (http, suite) =>
        {
            requetes.Enqueue(new Requete(http.Request.Method, http.Request.Host.Value ?? "", http.Request.Path.Value ?? ""));
            await suite(http);
        });
        app.MapGet("/", () => Results.Content(Page.Html, "text/html; charset=utf-8"));
        app.MapGet("/api/candidatures", () =>
        {
            var lecture = Base.Lire(cheminBase);
            return Results.Json(new { version = lecture.Version, candidatures = lecture.Candidatures });
        });
        return app;
    }
}

static class Page
{
    public static readonly string Html = Lire();

    static string Lire()
    {
        using var flux = typeof(Page).Assembly.GetManifestResourceStream("index.html")
            ?? throw new InvalidOperationException("page embarquée introuvable");
        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }
}
