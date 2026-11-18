using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Sollicito.Api.Securite;

/// <summary>
/// Garde l'écoute sur 127.0.0.1 au lancement, en deux temps. Avant que Kestrel n'ouvre quoi que ce soit
/// (<see cref="StartingAsync"/>), il refuse une configuration qui ferait préférer ses propres adresses à celle du
/// code. Une fois Kestrel démarré (<see cref="StartedAsync"/>), il relève l'adresse réellement ouverte : toute
/// autre écoute arrête Kestrel et fait échouer le lancement.
/// </summary>
internal sealed class ControleDeLEcoute(IServer serveur, AdresseDEcoute adresse, IConfiguration configuration) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Même lecture que l'hôte web d'ASP.NET Core : « true », quelle que soit la casse, ou « 1 ».
        var preference = configuration[WebHostDefaults.PreferHostingUrlsKey];
        if (string.Equals(preference, "true", StringComparison.OrdinalIgnoreCase) || preference == "1")
        {
            throw new InvalidOperationException(
                $"La configuration demande à Kestrel de préférer ses propres adresses ({WebHostDefaults.PreferHostingUrlsKey}) : "
                + "l'API locale n'écoute que sur 127.0.0.1, sur un port choisi au lancement (ADR-0001, D9).");
        }

        return Task.CompletedTask;
    }

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var adresses = serveur.Features.Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? [];
        try
        {
            adresse.Relever(adresses);
        }
        catch (InvalidOperationException)
        {
            // Plus rien n'écoute avant que l'erreur ne remonte et n'interrompe le lancement.
            await serveur.StopAsync(cancellationToken);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
