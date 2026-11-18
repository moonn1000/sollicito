using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'API n'écoute que sur 127.0.0.1, jamais sur une interface réseau (ADR-0001, D9), et la configuration ne
/// peut pas l'ouvrir ailleurs. Les essais d'écoute interdite visent d'autres adresses locales (127.0.0.2) :
/// une écoute sur 0.0.0.0 ferait apparaître l'invite du pare-feu de Windows sur le poste qui lance les tests.
/// </summary>
public sealed class EcouteDeLApiTests(ApiLancee api) : IClassFixture<ApiLancee>
{
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

    [Fact]
    public async Task Une_adresse_donnee_par_la_configuration_ne_change_pas_l_ecoute()
    {
        await using var autre = await ApiLancee.LancerAsync(builder => Configurer(builder, ("urls", "http://127.0.0.2:0")));

        var adresse = Assert.Single(AdressesOuvertes(autre));
        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(adresse.Host));
    }

    [Theory]
    [InlineData("Kestrel:Endpoints:Autre:Url=http://127.0.0.2:0")]
    [InlineData("urls=http://127.0.0.2:0", "preferHostingUrls=true")]
    public async Task L_api_refuse_de_demarrer_si_la_configuration_l_ouvre_ailleurs(params string[] reglages)
    {
        var paires = reglages.Select(reglage => reglage.Split('=', 2)).Select(paire => (paire[0], paire[1])).ToArray();

        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiLancee.LancerAsync(builder => Configurer(builder, paires)));

        Assert.Contains("127.0.0.2", erreur.Message, StringComparison.Ordinal);
        Assert.Contains("n'écoute que sur une seule adresse http, en 127.0.0.1", erreur.Message, StringComparison.Ordinal);
    }

    private static Uri[] AdressesOuvertes(ApiLancee lancee) =>
    [
        .. lancee.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Select(adresse => new Uri(adresse)),
    ];

    private static void Configurer(IWebHostBuilder builder, params (string Cle, string Valeur)[] reglages) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            reglages.Select(reglage => new KeyValuePair<string, string?>(reglage.Cle, reglage.Valeur))));

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
