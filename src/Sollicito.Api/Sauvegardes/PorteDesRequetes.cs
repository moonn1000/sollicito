namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// La porte de la base, côté routes (#28, ADR-0012) : après le routage, toute route de <c>/api</c> passe par
/// <see cref="PorteDeLaBase"/>, sauf celles marquées <see cref="SansLaBase"/>, qui ne touchent pas la base. Une route
/// ajoutée plus tard est donc gardée d'office. Porte fermée, la requête reçoit un 503, sans détail, et n'atteint pas la
/// route. Le journal ne note que le refus, sans l'adresse de la requête.
/// </summary>
internal sealed partial class PorteDesRequetes(RequestDelegate suite, PorteDeLaBase porte, ILogger<PorteDesRequetes> journal)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var route = http.GetEndpoint();
        // Sans la casse, comme le routage : « /API/… » atteint les mêmes routes que « /api/… ».
        if (route is null || route.Metadata.GetMetadata<SansLaBase>() is not null || !http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await suite(http);
            return;
        }

        if (!porte.EssayerDEntrer())
        {
            RequeteRefusee(journal);
            http.Response.Headers.RetryAfter = "1";
            await TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: PorteDeLaBase.TitreDuRefus).ExecuteAsync(http);
            return;
        }

        try
        {
            await suite(http);
        }
        finally
        {
            porte.Sortir();
        }
    }

    [LoggerMessage(EventId = 57, Level = LogLevel.Information, Message = "Requête refusée : une restauration de la base est en cours.")]
    private static partial void RequeteRefusee(ILogger journal);
}

/// <summary>Marque une route qui ne touche pas la base : la porte de la base la laisse passer pendant une restauration.</summary>
internal sealed class SansLaBase
{
    /// <summary>La marque, partagée par toutes ces routes.</summary>
    public static readonly SansLaBase Marque = new();
}

/// <summary>La marque <see cref="SansLaBase"/> sur une route.</summary>
internal static class MarqueSansLaBase
{
    public static TBuilder SansLaBase<TBuilder>(this TBuilder route)
        where TBuilder : IEndpointConventionBuilder => route.WithMetadata(Sauvegardes.SansLaBase.Marque);
}
