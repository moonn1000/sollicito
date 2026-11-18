using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Securite;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// L'API lancée dans le processus des tests, telle que <c>Program</c> la construit, sur un vrai Kestrel :
/// 127.0.0.1 et un port choisi au lancement. Le serveur de test en mémoire (TestServer) ne convient pas : il
/// n'ouvre aucune adresse, et c'est sur l'adresse réellement ouverte que l'API contrôle Host et Origin.
/// </summary>
public sealed class ApiLancee : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _racine = new();
    private readonly WebApplicationFactory<Program> _usine;

    /// <summary>L'API telle qu'elle se lance, en environnement de développement (celui des tests).</summary>
    public ApiLancee()
        : this(_ => { })
    {
    }

    private ApiLancee(Action<IWebHostBuilder> ajuster)
    {
        _usine = _racine.WithWebHostBuilder(ajuster);
    }

    /// <summary>Origine de l'interface et adresse de l'API : « http://127.0.0.1:port ».</summary>
    public Uri Adresse { get; private set; } = null!;

    /// <summary>Port choisi au lancement.</summary>
    public int Port => Adresse.Port;

    /// <summary>Client HTTP qui envoie les en-têtes tels qu'écrits : ni cookies gérés, ni redirections suivies.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Services de l'API lancée.</summary>
    public IServiceProvider Services => _usine.Services;

    /// <summary>Jeton du lancement, lu dans les services comme le fera la fenêtre hôte.</summary>
    public string Jeton => Services.GetRequiredService<JetonDeLancement>().Valeur();

    /// <summary>Origine de l'interface, telle que le navigateur l'écrit dans l'en-tête <c>Origin</c>.</summary>
    public string Origine => Adresse.GetLeftPart(UriPartial.Authority);

    /// <summary>Remplace « {port} », « {origine} » et « {jeton} » par leur valeur pour ce lancement.</summary>
    public string Remplir(string modele) => modele
        .Replace("{port}", Port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{origine}", Origine, StringComparison.Ordinal)
        .Replace("{jeton}", Jeton, StringComparison.Ordinal);

    /// <summary>Lance l'API après avoir ajusté sa construction (environnement, configuration, journaux).</summary>
    public static async Task<ApiLancee> LancerAsync(Action<IWebHostBuilder> ajuster)
    {
        var api = new ApiLancee(ajuster);
        try
        {
            await api.InitializeAsync();
            return api;
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }

    public ValueTask InitializeAsync()
    {
        _usine.UseKestrel();
        _usine.StartServer();
        Adresse = new Uri(Services.GetRequiredService<AdresseDEcoute>().Origine);
        Client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = Adresse,
        };
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Envoie une requête. Avec <paramref name="jeton"/> à vrai, elle porte le jeton dans son en-tête dédié ;
    /// <paramref name="preparer"/> ajoute ensuite ce que le test veut (Host, Origin, cookie, corps…).
    /// </summary>
    public async Task<HttpResponseMessage> EnvoyerAsync(
        HttpMethod methode,
        string chemin,
        bool jeton,
        Action<HttpRequestMessage>? preparer = null)
    {
        using var requete = new HttpRequestMessage(methode, new Uri(chemin, UriKind.Relative));
        if (jeton)
        {
            requete.Headers.Add(JetonDeLancement.EnTete, Jeton);
        }

        preparer?.Invoke(requete);
        return await Client.SendAsync(requete, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Envoie une requête écrite à la main sur une connexion TCP, pour ce qu'un client HTTP ne sait pas
    /// envoyer (HTTP/1.0 sans Host, adresse absolue, en-tête répété), et rend la ligne d'état de la réponse.
    /// </summary>
    public async Task<string> EnvoyerBrutAsync(string requete)
    {
        var annulation = TestContext.Current.CancellationToken;
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync(IPAddress.Loopback, Port, annulation);
        var flux = tcp.GetStream();
        await flux.WriteAsync(Encoding.ASCII.GetBytes(requete), annulation);
        using var lecteur = new StreamReader(flux, Encoding.ASCII);
        return await lecteur.ReadLineAsync(annulation) ?? "";
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await _racine.DisposeAsync();
    }
}
