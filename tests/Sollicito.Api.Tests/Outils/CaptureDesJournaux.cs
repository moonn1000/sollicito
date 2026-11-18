using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Fournisseur de journaux des tests : il garde, pour chaque entrée, tout ce qu'un fournisseur pourrait écrire
/// (catégorie, message formaté, valeurs structurées, exception, portées).
/// </summary>
public sealed class CaptureDesJournaux : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _entrees = new();
    private IExternalScopeProvider _portees = new LoggerExternalScopeProvider();

    /// <summary>Les entrées reçues, une chaîne par entrée.</summary>
    public IReadOnlyCollection<string> Entrees => _entrees;

    public ILogger CreateLogger(string categoryName) => new Journal(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _portees = scopeProvider;

    public void Dispose()
    {
    }

    private static void AjouterLesValeurs(StringBuilder entree, object? etat)
    {
        if (etat is IEnumerable<KeyValuePair<string, object?>> valeurs)
        {
            foreach (var (cle, valeur) in valeurs)
            {
                entree.Append(" | ").Append(cle).Append('=').Append(valeur);
            }
        }
    }

    private sealed class Journal(CaptureDesJournaux capture, string categorie) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => capture._portees.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entree = new StringBuilder()
                .Append(logLevel).Append(' ').Append(categorie).Append(" : ").Append(formatter(state, exception));
            AjouterLesValeurs(entree, state);
            if (exception is not null)
            {
                entree.Append(" | exception=").Append(exception);
            }

            capture._portees.ForEachScope(
                (portee, texte) =>
                {
                    texte.Append(" | portée=").Append(portee);
                    AjouterLesValeurs(texte, portee);
                },
                entree);
            capture._entrees.Enqueue(entree.ToString());
        }
    }
}
