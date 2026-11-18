using Microsoft.Extensions.Logging.Console;

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
    /// Catégorie des lignes « Request starting » et « Request finished » d'ASP.NET Core, qui écrivent l'adresse
    /// complète de chaque requête, chaîne de requête comprise.
    /// </summary>
    internal const string JournalDesRequetes = "Microsoft.AspNetCore.Hosting.Diagnostics";

    public static void Appliquer(ILoggingBuilder journaux)
    {
        // La console seule. Le journal d'événements de Windows, ajouté d'office, garde ses entrées sur le disque,
        // et tout compte interactif du poste peut les y lire ; EventSource et Debug les offrent aux outils de diagnostic.
        journaux.ClearProviders();
        journaux.AddConsole();

        // Plafond posé pour tous les fournisseurs, puis pour la console : un réglage propre à la console
        // (Logging:Console:LogLevel) ne suffit donc pas à le lever.
        journaux.AddFilter(JournalDesRequetes, LogLevel.Warning);
        journaux.AddFilter<ConsoleLoggerProvider>(JournalDesRequetes, LogLevel.Warning);
    }
}
