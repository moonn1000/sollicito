namespace Sollicito.Api.Securite;

/// <summary>
/// Refuse (403) une requête de l'API dont l'en-tête <c>Origin</c> n'est pas celui de l'interface (ADR-0003,
/// « Règles d'accès »). Une lecture (<c>GET</c>, <c>HEAD</c>) peut s'en passer, car le navigateur ne l'envoie pas
/// sur une lecture de même origine ; toute autre méthode doit le porter. L'origine <c>null</c> est refusée,
/// comme un en-tête répété.
/// </summary>
internal sealed partial class ControleDeLOrigine(RequestDelegate suite, AdresseDEcoute adresse, ILogger<ControleDeLOrigine> journal)
{
    public Task InvokeAsync(HttpContext http)
    {
        var origines = http.Request.Headers.Origin;
        var admise = origines.Count switch
        {
            0 => HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method),
            1 => adresse.EstLOrigine(origines[0]),
            _ => false,
        };

        if (admise)
        {
            return suite(http);
        }

        RequeteRefusee(journal);
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Requête refusée : en-tête Origin absent ou étranger.")]
    private static partial void RequeteRefusee(ILogger journal);
}
