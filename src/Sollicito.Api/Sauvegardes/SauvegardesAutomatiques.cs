namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// Le moment des copies du jour (#26, ADR-0008), dans le processus de l'API, sans autre processus :
/// <list type="bullet">
/// <item>au lancement, une fois la base prête, en arrière-plan : la copie du jour, si elle manque ;</item>
/// <item>chaque heure, par une minuterie : la copie du nouveau jour d'une séance restée ouverte, et un nouvel essai après
/// un échec, par exemple quand un dossier débranché revient ;</item>
/// <item>à la fermeture : la copie du jour, refaite pour garder la fin de la séance. Elle ne retient pas la fermeture
/// plus de dix secondes ; manquée ou abandonnée, elle laisse la copie d'avant.</item>
/// </list>
/// Aucune exception n'en sort : l'hôte l'écrirait en entier, chemin compris, et s'arrêterait.
/// </summary>
internal sealed partial class SauvegardesAutomatiques(
    SauvegardesDeLaBase sauvegardes,
    TimeProvider horloge,
    ILogger<SauvegardesAutomatiques> journal) : BackgroundService
{
    // Vrai une fois le service démarré : un lancement qui a échoué avant lui, par exemple sur le verrou d'un autre
    // lancement, ne copie rien en s'arrêtant.
    private volatile bool _demarre;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _demarre = true;
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hors du fil du lancement : un dossier débranché ne retarde pas la fenêtre.
        await Task.Yield();
        try
        {
            await sauvegardes.FaireLaCopieDuJourSiBesoinAsync(stoppingToken);
            using var minuterie = new PeriodicTimer(SauvegardesDeLaBase.PeriodeDesVerifications, horloge);
            while (await minuterie.WaitForNextTickAsync(stoppingToken))
            {
                await sauvegardes.FaireLaCopieDuJourSiBesoinAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La fermeture : la copie de fermeture suit.
        }
        catch (Exception erreur)
        {
            Interrompues(journal, erreur.GetType().Name);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!_demarre)
        {
            return;
        }

        try
        {
            await sauvegardes.FaireLaCopieDeFermetureAsync(cancellationToken);
        }
        catch (Exception erreur)
        {
            Interrompues(journal, erreur.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 41, Level = LogLevel.Error, Message = "Copies automatiques interrompues ({TypeDErreur}).")]
    private static partial void Interrompues(ILogger journal, string typeDErreur);
}
