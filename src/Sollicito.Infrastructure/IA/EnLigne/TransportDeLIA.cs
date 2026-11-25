using System.Net;

namespace Sollicito.Infrastructure.IA.EnLigne;

/// <summary>
/// Le transport des fournisseurs d'IA en ligne (#48, ADR-0017) : ce qui envoie une requête et rend sa réponse, en-têtes
/// reçus, sans jamais lire le corps à sa place. En production, un <see cref="SocketsHttpHandler"/> réglé ici, construit au
/// premier usage, jamais au lancement ; dans les tests et la version de test de la fenêtre, un faux, qui ne touche jamais le
/// réseau. C'est un <see cref="HttpMessageInvoker"/>, la base de <c>HttpClient</c>, sans son délai ni sa mise en tampon, et
/// hors de la fabrique de .NET : aucun journal de client HTTP n'existe. Aucune configuration, aucune variable
/// d'environnement ne règle l'adresse ni ce réglage ; seul le proxy du système s'applique.
/// </summary>
public sealed class TransportDeLIA : IDisposable
{
    private readonly Lazy<HttpMessageInvoker> _invocateur;

    /// <summary>Le transport de production : <see cref="CreerLeGestionnaire"/>, au premier usage.</summary>
    public TransportDeLIA()
        : this(CreerLeGestionnaire)
    {
    }

    /// <summary>Un transport sur le gestionnaire que rend <paramref name="gestionnaire"/>, au premier usage.</summary>
    public TransportDeLIA(Func<HttpMessageHandler> gestionnaire)
    {
        ArgumentNullException.ThrowIfNull(gestionnaire);
        _invocateur = new Lazy<HttpMessageInvoker>(() => new HttpMessageInvoker(gestionnaire(), disposeHandler: true));
    }

    /// <summary>
    /// Le gestionnaire de production, tel que l'ADR-0017 le règle :
    /// <list type="bullet">
    /// <item>aucune redirection suivie : .NET n'en retire que <c>Authorization</c>, et un en-tête <c>x-api-key</c> suivrait
    /// une redirection vers un autre hôte ;</item>
    /// <item>aucun cookie, aucun identifiant Windows, proxy compris ;</item>
    /// <item>aucune décompression : la borne de taille compte les octets reçus, et aucun <c>Accept-Encoding</c> ne part ;</item>
    /// <item>aucune connexion réutilisée : une connexion neuve par demande, donc aucune reprise de .NET sur une connexion
    /// morte ;</item>
    /// <item>aucun en-tête de traçage (<c>traceparent</c>, <c>Request-Id</c>) : .NET en ajoute d'office quand une activité
    /// est en cours, ce qui est le cas pendant une requête de l'API ;</item>
    /// <item>la validation TLS du système, sans rappel ; le proxy du système, avec ses réglages et ses variables
    /// d'environnement (<c>HTTPS_PROXY</c>…) : ils peuvent faire passer la connexion par un proxy, que le TLS traverse de
    /// bout en bout, mais jamais changer l'adresse.</item>
    /// </list>
    /// </summary>
    internal static SocketsHttpHandler CreerLeGestionnaire() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        DefaultProxyCredentials = null,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.Zero,
        PooledConnectionIdleTimeout = TimeSpan.Zero,
        MaxConnectionsPerServer = 1,
        MaxResponseHeadersLength = 64,
        ActivityHeadersPropagator = null,
    };

    /// <summary>Envoie <paramref name="requete"/>, et rend sa réponse dès ses en-têtes reçus.</summary>
    public Task<HttpResponseMessage> EnvoyerAsync(HttpRequestMessage requete, CancellationToken annulation) =>
        _invocateur.Value.SendAsync(requete, annulation);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_invocateur.IsValueCreated)
        {
            _invocateur.Value.Dispose();
        }
    }
}
