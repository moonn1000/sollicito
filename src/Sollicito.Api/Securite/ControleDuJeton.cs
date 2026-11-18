using Microsoft.Net.Http.Headers;

namespace Sollicito.Api.Securite;

/// <summary>
/// Refuse (401) toute requête qui ne porte pas le jeton de lancement dans son en-tête dédié, en développement
/// comme ailleurs (ADR-0001, D9). Il s'applique à toutes les routes de l'API, documentation OpenAPI et adresses
/// inconnues comprises : seuls les fichiers de l'interface, servis avant lui, n'y sont pas soumis (ADR-0003).
/// </summary>
internal sealed partial class ControleDuJeton(RequestDelegate suite, JetonDeLancement jeton, ILogger<ControleDuJeton> journal)
{
    public Task InvokeAsync(HttpContext http)
    {
        if (jeton.EstPresentDans(http.Request.Headers))
        {
            return suite(http);
        }

        RequeteRefusee(journal);
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers[HeaderNames.WWWAuthenticate] = JetonDeLancement.EnTete;
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Requête refusée : jeton absent ou faux.")]
    private static partial void RequeteRefusee(ILogger journal);
}
