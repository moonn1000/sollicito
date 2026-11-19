using Sollicito.Api.Securite;

namespace Sollicito.Api.Erreurs;

/// <summary>
/// Répond elle-même à toute exception d'une requête, en tête du chemin des requêtes de l'API. En développement,
/// ASP.NET Core place sa page d'exception avant tout le reste : elle afficherait les en-têtes de la requête, jeton
/// compris, et le message de l'exception. Aucune exception ne l'atteint donc, quel que soit l'environnement.
/// <list type="bullet">
/// <item>un corps illisible (<see cref="BadHttpRequestException"/>, JSON mal formé ou d'une autre forme que la
/// route) reçoit son statut, 400 le plus souvent, et un problème sans détail ;</item>
/// <item>toute autre exception reçoit un 500, et un problème sans détail.</item>
/// </list>
/// Le journal ne note que le type de l'exception : son message peut reprendre ce que la requête contenait.
/// </summary>
internal sealed partial class ErreursInattendues(RequestDelegate suite, ILogger<ErreursInattendues> journal)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await suite(http);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            // Le client est parti : il n'y a plus personne à qui répondre.
            RequeteAbandonnee(journal);
        }
        catch (Exception erreur)
        {
            await RepondreAsync(http, erreur);
        }
    }

    private async Task RepondreAsync(HttpContext http, Exception erreur)
    {
        if (http.Response.HasStarted)
        {
            // La réponse est partie en partie : on coupe la connexion plutôt que d'envoyer une réponse tronquée.
            ErreurApresLeDebutDeLaReponse(journal, erreur.GetType().Name);
            http.Abort();
            return;
        }

        var (statut, titre) = erreur is BadHttpRequestException illisible
            ? (illisible.StatusCode, "La requête est illisible.")
            : (StatusCodes.Status500InternalServerError, "L'API locale a rencontré une erreur inattendue.");
        if (statut >= StatusCodes.Status500InternalServerError)
        {
            ErreurInattendue(journal, erreur.GetType().Name);
        }
        else
        {
            RequeteIllisible(journal, statut);
        }

        // Rien de ce que la route avait préparé ne part : ni en-tête, ni début de corps. Les en-têtes de sécurité,
        // posés avant elle, sont remis.
        http.Response.Clear();
        EnTetesDeSecurite.Poser(http.Response.Headers);
        await TypedResults.Problem(statusCode: statut, title: titre).ExecuteAsync(http);
    }

    [LoggerMessage(EventId = 20, Level = LogLevel.Error, Message = "Erreur inattendue pendant une requête ({TypeDErreur}).")]
    private static partial void ErreurInattendue(ILogger journal, string typeDErreur);

    [LoggerMessage(EventId = 21, Level = LogLevel.Error, Message = "Erreur inattendue après le début de la réponse ({TypeDErreur}) : connexion coupée.")]
    private static partial void ErreurApresLeDebutDeLaReponse(ILogger journal, string typeDErreur);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "Requête refusée : corps illisible ({Statut}).")]
    private static partial void RequeteIllisible(ILogger journal, int statut);

    [LoggerMessage(EventId = 23, Level = LogLevel.Debug, Message = "Requête abandonnée par le client.")]
    private static partial void RequeteAbandonnee(ILogger journal);
}
