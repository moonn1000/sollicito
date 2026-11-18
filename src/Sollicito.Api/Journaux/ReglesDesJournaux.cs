using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sollicito.Api.Journaux;

/// <summary>
/// La règle des journaux (ADR-0001, D9, « Secrets et journaux ») : un journal n'écrit ni le jeton, ni une clé,
/// ni le contenu des offres, des candidatures ou du profil, ni aucune autre donnée personnelle. Il dit ce qui
/// s'est passé (une étape, un refus, une erreur), jamais ce que la requête contenait : ni sa chaîne de
/// requête, ni ses en-têtes, ni ses cookies, ni son corps. Le modèle de menaces (docs/modele-de-menaces.md)
/// la détaille, et les tests de l'API la vérifient.
/// </summary>
internal static class ReglesDesJournaux
{
    /// <summary>
    /// Catégories d'ASP.NET Core qui recopient une requête, et ne reçoivent donc jamais de journal :
    /// <list type="bullet">
    /// <item><c>Microsoft.AspNetCore.Hosting.Diagnostics</c> écrit l'adresse complète de chaque requête, chaîne de
    /// requête comprise (« Request starting », « Request finished »), et attache son chemin à chaque entrée écrite
    /// pendant la requête (portée <c>RequestPath</c>) ;</item>
    /// <item><c>Microsoft.AspNetCore.Server.Kestrel.BadRequests</c> recopie la ligne brute d'une requête mal formée ;</item>
    /// <item><c>Microsoft.AspNetCore.Routing.Matching.DfaMatcher</c> écrit le chemin de chaque requête routée.</item>
    /// </list>
    /// </summary>
    internal static readonly IReadOnlySet<string> CategoriesMuettes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Server.Kestrel.BadRequests",
        "Microsoft.AspNetCore.Routing.Matching.DfaMatcher",
    };

    public static void Appliquer(ILoggingBuilder journaux)
    {
        // La console seule. Le journal d'événements de Windows, ajouté d'office, garde ses entrées sur le disque,
        // et tout compte interactif du poste peut les y lire ; EventSource et Debug les offrent aux outils de diagnostic.
        journaux.ClearProviders();
        journaux.AddConsole();

        // Les catégories muettes le restent quels que soient les réglages des journaux, joker ou réglage propre à
        // un fournisseur compris : la fabrique des journaux leur donne un journal qui n'écrit rien.
        journaux.Services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(services => new FabriqueDeJournaux(new LoggerFactory(
            services.GetServices<ILoggerProvider>(),
            services.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>(),
            services.GetService<IOptions<LoggerFactoryOptions>>(),
            services.GetService<IExternalScopeProvider>()))));
    }

    /// <summary>La fabrique des journaux de .NET, sauf pour les catégories muettes.</summary>
    private sealed class FabriqueDeJournaux(LoggerFactory fabrique) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) =>
            CategoriesMuettes.Contains(categoryName) ? NullLogger.Instance : fabrique.CreateLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) => fabrique.AddProvider(provider);

        public void Dispose() => fabrique.Dispose();
    }
}
