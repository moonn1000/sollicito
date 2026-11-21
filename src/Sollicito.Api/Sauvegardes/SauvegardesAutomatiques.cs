namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// Le moment des copies du jour (#26, ADR-0008), dans le processus de l'API, sans autre processus :
/// <list type="bullet">
/// <item>au lancement, une fois celui-ci réussi, en arrière-plan : la copie du jour, si elle manque ;</item>
/// <item>chaque heure, par une minuterie : la copie du nouveau jour d'une séance restée ouverte, et un nouvel essai après
/// un échec, par exemple quand un dossier débranché revient ;</item>
/// <item>à la fermeture : la copie du jour, refaite pour garder la fin de la séance. Elle ne retient pas la fermeture
/// plus de dix secondes ; manquée ou abandonnée, elle laisse la copie d'avant.</item>
/// </list>
/// Un lancement qui échoue ne copie rien, ni au lancement ni en s'arrêtant, qu'il échoue avant ce service (sur le verrou
/// d'un autre lancement, par exemple) ou après lui (sur le contrôle de l'écoute) : quand il rend la main, aucune copie
/// n'est en cours. Aucune exception n'en sort : l'hôte l'écrirait en entier, chemin compris, et s'arrêterait.
/// </summary>
internal sealed partial class SauvegardesAutomatiques(
    SauvegardesDeLaBase sauvegardes,
    TimeProvider horloge,
    IHostApplicationLifetime cycleDeVie,
    ILogger<SauvegardesAutomatiques> journal) : BackgroundService
{
    // La copie de la fermeture, lancée une seule fois. L'hôte peut appeler StopAsync deux fois : sous l'hôte des tests, deux
    // appels pour un seul hôte, dont un seul est attendu par la fermeture (relevé du 2026-11-20). Chaque appel attend la
    // même copie, sans quoi elle pourrait finir après l'arrêt.
    private Lazy<Task>? _fermeture;

    // L'hôte ne signale ApplicationStarted qu'une fois tous ses services démarrés sans erreur, contrôles du lancement
    // compris : jamais pour un lancement qui échoue.
    private bool LancementReussi => cycleDeVie.ApplicationStarted.IsCancellationRequested;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await AttendreLeLancementAsync(stoppingToken);
            await VerifierAsync(stoppingToken);
            using var minuterie = new PeriodicTimer(SauvegardesDeLaBase.PeriodeDesVerifications, horloge);
            while (await minuterie.WaitForNextTickAsync(stoppingToken))
            {
                await VerifierAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La fermeture : la copie de fermeture suit.
        }
    }

    // Attend la fin d'un lancement réussi ; un arrêt, ou la fin de l'hôte après un lancement manqué, lève
    // OperationCanceledException. La suite ne se fait jamais sur le fil du lancement, qui signale ApplicationStarted : un
    // dossier débranché ne retarde pas la fenêtre.
    private async Task AttendreLeLancementAsync(CancellationToken stoppingToken)
    {
        var lance = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var quandLance = cycleDeVie.ApplicationStarted.Register(static etat => ((TaskCompletionSource)etat!).TrySetResult(), lance);
        await lance.Task.WaitAsync(stoppingToken);
    }

    // Une vérification : une erreur inattendue est notée par son type, et la minuterie continue, pour l'essai suivant.
    private async Task VerifierAsync(CancellationToken stoppingToken)
    {
        try
        {
            await sauvegardes.FaireLaCopieDuJourSiBesoinAsync(stoppingToken);
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            Interrompues(journal, erreur.GetType().Name);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (!LancementReussi)
        {
            return;
        }

        var nouvelle = new Lazy<Task>(FermerAsync);
        await (Interlocked.CompareExchange(ref _fermeture, nouvelle, null) ?? nouvelle).Value;
    }

    private async Task FermerAsync()
    {
        try
        {
            await sauvegardes.FaireLaCopieDeFermetureAsync();
        }
        catch (Exception erreur)
        {
            Interrompues(journal, erreur.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 41, Level = LogLevel.Error, Message = "Vérification des copies interrompue ({TypeDErreur}) : la suivante se fera à l'heure dite.")]
    private static partial void Interrompues(ILogger journal, string typeDErreur);
}
