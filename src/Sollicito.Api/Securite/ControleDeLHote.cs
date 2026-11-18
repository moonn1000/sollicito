namespace Sollicito.Api.Securite;

/// <summary>
/// Refuse (400) toute requête dont l'en-tête <c>Host</c> n'est pas exactement l'adresse d'écoute, quelle que
/// soit la route, fichiers de l'interface compris (ADR-0001, D9 ; ADR-0003). Une page qui joint l'API sous un
/// autre nom, par rebinding DNS ou par <c>localhost</c>, est ainsi arrêtée avant toute autre étape.
/// </summary>
internal sealed partial class ControleDeLHote(RequestDelegate suite, AdresseDEcoute adresse, ILogger<ControleDeLHote> journal)
{
    public Task InvokeAsync(HttpContext http)
    {
        var hotes = http.Request.Headers.Host;
        if (hotes.Count == 1 && adresse.EstLHote(hotes[0]))
        {
            return suite(http);
        }

        // Ni la valeur reçue ni l'adresse de la requête : un journal n'écrit aucune donnée de la requête.
        RequeteRefusee(journal);
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Requête refusée : en-tête Host inattendu.")]
    private static partial void RequeteRefusee(ILogger journal);
}
