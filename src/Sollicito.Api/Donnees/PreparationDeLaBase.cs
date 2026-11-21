using Microsoft.EntityFrameworkCore;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Api.Donnees;

/// <summary>
/// Crée ou met à jour la base au lancement, avant que Kestrel n'écoute (ADR-0001, D3 ; ADR-0008) : au premier lancement,
/// les migrations versionnées créent la base dans son dossier, en mode WAL ; aux suivants, celles qui attendent
/// s'appliquent à une copie, qui ne remplace la base qu'une fois migrée (<see cref="MiseAJourDeLaBase"/>).
/// <list type="bullet">
/// <item>Un seul lancement à la fois sur un dossier (<see cref="VerrouDeLInstance"/>) : un second lancement est refusé
/// aussitôt, par <see cref="BaseDejaOuverte"/>, au lieu de migrer la même base.</item>
/// <item>Sous ce verrou, une ligne restée dans le verrou des migrations d'EF Core est abandonnée : elle est retirée,
/// sans quoi les migrations l'attendraient sans fin (<see cref="VerrouDesMigrations"/>).</item>
/// <item>Sans migration en attente, rien n'est copié.</item>
/// </list>
/// L'échec arrête le lancement, sans que son message ne cite un chemin, ni que l'erreur relancée ne garde celle
/// d'origine, que l'hôte écrirait en entier (#80, point 1). Le verrou du dossier se rend à l'arrêt de l'API.
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
            // Sous le verrou, avant toute mise à jour : la copie de travail d'une mise à jour interrompue par un arrêt
            // brutal, et les fichiers provisoires des réglages. La base elle-même n'y répond jamais.
            if (await RestesDUnArret.RetirerAsync(dossier) is var restes and > 0)
            {
                RestesRetires(journal, restes);
            }

            await using var portee = portees.CreateAsyncScope();
            var baseDeDonnees = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Database;
            if (!File.Exists(dossier.Fichier))
            {
                // Une base neuve : rien à copier, aucun verrou abandonné. Les migrations la créent elles-mêmes, en mode
                // WAL ; ouverte avant elles, la base existerait déjà, vide, et resterait dans le mode par défaut.
                await baseDeDonnees.MigrateAsync(cancellationToken);
            }
            else
            {
                if (await VerrouDesMigrations.RetirerUneLigneAbandonneeAsync(baseDeDonnees, cancellationToken) > 0)
                {
                    VerrouAbandonneRetire(journal);
                }

                var enAttente = (await baseDeDonnees.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (enAttente.Count > 0)
                {
                    await MiseAJourDeLaBase.AppliquerAsync(dossier, enAttente[^1], cancellationToken);
                    BaseMiseAJour(journal, enAttente.Count);
                }
                else if (!string.Equals(await CopieDeLaBase.ModeDuJournalAsync(dossier.Fichier, cancellationToken), "wal", StringComparison.OrdinalIgnoreCase))
                {
                    // Une base créée avant #26 est restée dans le mode par défaut : elle passe en WAL, le mode qu'EF Core
                    // donne à une base neuve.
                    await CopieDeLaBase.MettreEnModeWalAsync(dossier.Fichier, cancellationToken);
                }
            }
        }
        catch (BaseDejaOuverte)
        {
            DejaOuverte(journal);
            throw;
        }
        catch (MiseAJourDeLaBaseEchouee echec)
        {
            MiseAJourEchouee(journal, echec.Raison, echec.TypeDeLaCause);
            throw;
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            // Le type seul : le message d'une erreur d'accès au fichier cite son chemin, donc le nom du compte Windows. Et
            // sans l'erreur d'origine : l'hôte écrit toute la chaîne d'un lancement qui échoue (#80, point 1).
            BaseNonPreparee(journal, erreur.GetType().Name);
            throw new InvalidOperationException("La base de données n'a pas pu être créée ni mise à jour.");
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

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Base mise à jour sur une copie : {Migrations} migration(s) appliquée(s), copie d'avant gardée.")]
    private static partial void BaseMiseAJour(ILogger journal, int migrations);

    [LoggerMessage(EventId = 15, Level = LogLevel.Error, Message = "Mise à jour de la base échouée ({Raison}, {TypeDErreur}) : la base est restée telle qu'avant.")]
    private static partial void MiseAJourEchouee(ILogger journal, RaisonDeLEchec raison, string typeDErreur);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "Restes d'un arrêt brutal retirés du dossier de données : {Restes} reste(s).")]
    private static partial void RestesRetires(ILogger journal, int restes);
}
