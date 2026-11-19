using Microsoft.EntityFrameworkCore;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Donnees;

/// <summary>
/// Crée ou met à jour la base au lancement, avant que Kestrel n'écoute (ADR-0001, D3) : au premier lancement, les
/// migrations versionnées créent la base dans son dossier ; aux suivants, elles appliquent celles qui manquent.
/// L'échec arrête le lancement, sans que son message ne cite le chemin de la base.
/// </summary>
internal sealed partial class PreparationDeLaBase(
    IServiceScopeFactory portees,
    DossierDeLaBase dossier,
    ILogger<PreparationDeLaBase> journal) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(dossier.Chemin);
            await using var portee = portees.CreateAsyncScope();
            await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Database.MigrateAsync(cancellationToken);
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

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Base de données prête.")]
    private static partial void BasePreparee(ILogger journal);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "La base de données n'a pas pu être créée ni mise à jour ({TypeDErreur}).")]
    private static partial void BaseNonPreparee(ILogger journal, string typeDErreur);
}
