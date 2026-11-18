using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'API n'écoute que sur 127.0.0.1, jamais sur une interface réseau (ADR-0001, D9). La configuration ne
/// l'ouvre pas ailleurs : ses adresses sont ignorées, une préférence pour elles fait échouer le lancement avant
/// toute écoute, et une écoute ouverte par un autre moyen le fait échouer après coup. Les essais d'écoute
/// interdite visent 127.0.0.2, une autre adresse locale : une écoute sur 0.0.0.0 ferait apparaître l'invite du
/// pare-feu de Windows sur le poste qui lance les tests.
/// </summary>
public sealed class EcouteDeLApiTests(ApiLancee api) : IClassFixture<ApiLancee>
{
    private static readonly IPAddress AutreAdresseLocale = IPAddress.Parse("127.0.0.2");

    [Fact]
    public void L_api_n_ouvre_qu_une_adresse_en_127_0_0_1_sur_un_port_choisi_au_lancement()
    {
        var adresse = Assert.Single(AdressesOuvertes(api));

        Assert.Equal(Uri.UriSchemeHttp, adresse.Scheme);
        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(adresse.Host));
        Assert.InRange(adresse.Port, 1, IPEndPoint.MaxPort);
    }

    [Fact]
    public async Task Aucune_autre_adresse_du_poste_ne_repond_sur_le_port_de_l_api()
    {
        // Les adresses des interfaces réseau actives, et l'adresse locale IPv6 [::1].
        var adresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(carte => carte.OperationalStatus == OperationalStatus.Up)
            .SelectMany(carte => carte.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(ip => !ip.Equals(IPAddress.Loopback))
            .Distinct()
            .ToList();
        Assert.SkipWhen(adresses.Count == 0, "Ni interface réseau active ni [::1] sur ce poste : aucune adresse à essayer.");

        var essais = await Task.WhenAll(adresses.Select(async ip => (Adresse: ip, Resultat: await EssayerDeSeConnecterAsync(ip, api.Port))));

        Assert.All(essais, essai => Assert.True(
            essai.Resultat != "connexion établie",
            $"{essai.Adresse} répond sur le port {api.Port}."));
    }

    [Theory]
    [InlineData("urls=http://127.0.0.2:0")]
    [InlineData("Kestrel:Endpoints:Autre:Url=http://127.0.0.2:0")]
    public async Task Une_adresse_donnee_par_la_configuration_est_ignoree(string reglage)
    {
        await using var autre = await ApiLancee.LancerAsync(builder => Configurer(builder, Paire(reglage)));

        var adresse = Assert.Single(AdressesOuvertes(autre));
        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(adresse.Host));
    }

    [Fact]
    public async Task Une_preference_pour_les_adresses_de_la_configuration_fait_echouer_le_lancement_avant_toute_ecoute()
    {
        var capture = new CaptureDesJournaux();

        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiLancee.LancerAsync(builder =>
        {
            Configurer(builder, Paire("urls=http://127.0.0.2:0"), Paire("preferHostingUrls=true"));
            builder.ConfigureLogging(journaux => journaux.AddProvider(capture));
        }));

        Assert.Contains("preferHostingUrls", erreur.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(capture.Entrees, entree => entree.Contains("Now listening on", StringComparison.Ordinal));
        // L'échec reste visible dans les journaux.
        Assert.Contains(capture.Entrees, entree =>
            entree.StartsWith(nameof(LogLevel.Error) + " Microsoft.Extensions.Hosting.", StringComparison.Ordinal)
            || entree.StartsWith(nameof(LogLevel.Critical) + " Microsoft.Extensions.Hosting.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_autre_ecoute_ouverte_au_lancement_le_fait_echouer()
    {
        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiLancee.LancerAsync(builder =>
            builder.ConfigureKestrel(kestrel => kestrel.Listen(AutreAdresseLocale, 0))));

        Assert.Contains("127.0.0.2", erreur.Message, StringComparison.Ordinal);
        Assert.Contains("n'écoute que sur une seule adresse http, en 127.0.0.1", erreur.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_ecoute_ajoutee_a_chaud_dans_la_configuration_ne_s_ouvre_pas()
    {
        var port = PortLibre(AutreAdresseLocale);
        await using var lancee = await ApiLancee.LancerAsync(builder => Configurer(builder, Paire("Sollicito:Essai=écoute à chaud")));

        var configuration = (IConfigurationRoot)lancee.Services.GetRequiredService<IConfiguration>();
        configuration["Kestrel:Endpoints:AChaud:Url"] = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.2:{port}");
        configuration.Reload();

        // Si Kestrel relisait sa configuration, il ouvrirait cette écoute dans la foulée : trois secondes pour le voir.
        var fin = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < fin)
        {
            Assert.NotEqual("connexion établie", await EssayerDeSeConnecterAsync(AutreAdresseLocale, port));
            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        }

        Assert.Single(AdressesOuvertes(lancee));
    }

    private static Uri[] AdressesOuvertes(ApiLancee lancee) =>
    [
        .. lancee.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Select(adresse => new Uri(adresse)),
    ];

    private static (string Cle, string Valeur) Paire(string reglage)
    {
        var morceaux = reglage.Split('=', 2);
        return (morceaux[0], morceaux[1]);
    }

    private static void Configurer(IWebHostBuilder builder, params (string Cle, string Valeur)[] reglages) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            reglages.Select(reglage => new KeyValuePair<string, string?>(reglage.Cle, reglage.Valeur))));

    private static int PortLibre(IPAddress ip)
    {
        var ecoute = new TcpListener(ip, 0);
        ecoute.Start();
        try
        {
            return ((IPEndPoint)ecoute.LocalEndpoint).Port;
        }
        finally
        {
            ecoute.Stop();
        }
    }

    private static async Task<string> EssayerDeSeConnecterAsync(IPAddress ip, int port)
    {
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        delai.CancelAfter(TimeSpan.FromSeconds(15));
        using var tcp = new TcpClient(ip.AddressFamily);
        try
        {
            await tcp.ConnectAsync(ip, port, delai.Token);
            return "connexion établie";
        }
        catch (SocketException erreur)
        {
            return erreur.SocketErrorCode.ToString();
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return "délai dépassé";
        }
    }
}
