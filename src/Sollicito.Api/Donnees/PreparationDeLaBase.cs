using Microsoft.EntityFrameworkCore;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Donnees;

/// <summary>
/// Crée ou met à jour la base au lancement, avant que Kestrel n'écoute (ADR-0001, D3) : au premier lancement, les
/// migrations versionnées créent la base dans son dossier ; aux suivants, elles appliquent celles qui manquent.
/// <list type="bullet">
/// <item>Un seul lancement à la fois sur un dossier (<see cref="VerrouDeLInstance"/>) : un second lancement est refusé
/// aussitôt, par <see cref="BaseDejaOuverte"/>, au lieu de migrer la même base.</item>
/// <item>Sous ce verrou, une ligne restée dans le verrou des migrations d'EF Core est abandonnée : elle est retirée,
/// sans quoi les migrations l'attendraient sans fin (<see cref="VerrouDesMigrations"/>).</item>
/// </list>
/// L'échec arrête le lancement, sans que son message ne cite le chemin de la base ; le verrou du dossier se rend à
/// l'arrêt de l'API.
/// </summary>
internal sealed partial class PreparationDeLaBase(
    IServiceScopeFactory portees,
    DossierDeLaBase dossier,
    ILogger<PreparationDeLaBase> journal) : IHostedLifecycleService, IDisposable
{
    private VerrouDeLInstance? _verrou;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(dossier.Chemin);
            _verrou = VerrouDeLInstance.Prendre(dossier);

            await using var portee = portees.CreateAsyncScope();
            var baseDeDonnees = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Database;
            if (await VerrouDesMigrations.RetirerUneLigneAbandonneeAsync(baseDeDonnees, cancellationToken) > 0)
            {
                VerrouAbandonneRetire(journal);
            }

            await baseDeDonnees.MigrateAsync(cancellationToken);
        }
        catch (BaseDejaOuverte)
        {
            DejaOuverte(journal);
            throw;
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            // Le type seul : le message d'une erreur d'accès au fichier cite son chemin, donc le nom du compte Windows.
            BaseNonPreparee(journal, erreur.GetType().Name);
            throw new InvalidOperationException("La base de données n'a pas pu être créée ni mise à jour.", erreur);
        }

        BasePreparee(journal);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Rend le verrou du dossier, y compris après un lancement qui a échoué.</summary>
    public void Dispose()
    {
        _verrou?.Dispose();
        _verrou = null;
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Base de données prête.")]
    private static partial void BasePreparee(ILogger journal);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "La base de données n'a pas pu être créée ni mise à jour ({TypeDErreur}).")]
    private static partial void BaseNonPreparee(ILogger journal, string typeDErreur);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Verrou des migrations abandonné par un lancement précédent : retiré.")]
    private static partial void VerrouAbandonneRetire(ILogger journal);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "Un autre lancement de Sollicito utilise déjà ce dossier de données.")]
    private static partial void DejaOuverte(ILogger journal);
}
