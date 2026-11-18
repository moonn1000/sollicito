using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Sollicito.Api.Securite;

/// <summary>
/// Relève l'adresse d'écoute dès que Kestrel l'a ouverte : <see cref="StartedAsync"/> passe après le démarrage
/// du serveur. Si Kestrel écoute ailleurs qu'en 127.0.0.1, il est arrêté et le lancement échoue.
/// </summary>
internal sealed class ReleveDeLAdresseDEcoute(IServer serveur, AdresseDEcoute adresse) : IHostedLifecycleService
{
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

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
