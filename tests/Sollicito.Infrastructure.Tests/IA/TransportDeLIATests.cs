using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Sollicito.Infrastructure.IA.EnLigne;

namespace Sollicito.Infrastructure.Tests.IA;

/// <summary>
/// Le vrai transport des fournisseurs en ligne (#48, ADR-0017) : le <see cref="SocketsHttpHandler"/> que construit le code
/// de production, contre des écoutes sur 127.0.0.1 qui écrivent leurs réponses à la main. Aucune requête ne quitte le poste
/// (ADR-0002). Il ne suit aucune redirection, n'envoie ni cookie ni demande de compression, ne réutilise aucune connexion,
/// et l'envoi borné coupe un corps sans fin et un serveur qui se tait.
/// </summary>
public sealed class TransportDeLIATests
{
    private const string Cle = "sk-ant-api03-SOLLICITO-ESSAI-48-cle-fictive-de-claude-0001";

    [Fact]
    public void Le_gestionnaire_de_production_est_regle_comme_l_ADR_0017_le_dit()
    {
        using var gestionnaire = TransportDeLIA.CreerLeGestionnaire();

        Assert.False(gestionnaire.AllowAutoRedirect);
        Assert.False(gestionnaire.UseCookies);
        Assert.Null(gestionnaire.Credentials);
        Assert.False(gestionnaire.PreAuthenticate);
        Assert.Null(gestionnaire.DefaultProxyCredentials);
        Assert.Equal(DecompressionMethods.None, gestionnaire.AutomaticDecompression);
        Assert.Equal(TimeSpan.Zero, gestionnaire.PooledConnectionLifetime);
        Assert.Null(gestionnaire.SslOptions.RemoteCertificateValidationCallback);
        Assert.Null(gestionnaire.ActivityHeadersPropagator);
    }

    [Fact]
    public async Task Seuls_partent_les_en_tetes_de_la_requete_meme_pendant_une_activite()
    {
        await using var fournisseur = new EcouteLocale(_ => Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"));
        using var transport = new TransportDeLIA();
        // Pendant une requête de l'API, une activité est en cours : .NET y ajouterait d'office un en-tête de traçage.
        using var activite = new Activity("Sollicito.EssaiDuTransport").SetIdFormat(ActivityIdFormat.W3C).Start();

        await EnvoyerAsync(transport, fournisseur.Port);

        var enTetes = fournisseur.Requetes.Single().Split("\r\n\r\n")[0].Split("\r\n").Skip(1)
            .Select(ligne => ligne[..ligne.IndexOf(':', StringComparison.Ordinal)].ToLowerInvariant())
            .Order(StringComparer.Ordinal);
        Assert.Equal(["content-length", "content-type", "host", "x-api-key"], enTetes);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Une_redirection_vers_un_autre_hote_n_emporte_pas_la_cle(int statut)
    {
        await using var autre = new EcouteLocale(_ => Reponse(200, "OK"));
        await using var fournisseur = new EcouteLocale(_ => Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statut} Redirection\r\nLocation: http://127.0.0.1:{autre.Port}/vol\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        using var transport = new TransportDeLIA();

        var brute = await EnvoyerAsync(transport, fournisseur.Port);

        Assert.Equal(statut, brute.Statut);
        Assert.Null(brute.Corps);
        Assert.Single(fournisseur.Requetes);
        Assert.Contains("x-api-key: " + Cle, fournisseur.Requetes.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(autre.Requetes);
        Assert.Equal(0, autre.Connexions);
    }

    [Fact]
    public async Task Aucun_cookie_ne_repart_ni_aucune_demande_de_compression_et_chaque_demande_ouvre_sa_connexion()
    {
        await using var fournisseur = new EcouteLocale(_ => Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nSet-Cookie: suivi=SOLLICITO-COOKIE; Path=/\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}"));
        using var transport = new TransportDeLIA();

        await EnvoyerAsync(transport, fournisseur.Port);
        await EnvoyerAsync(transport, fournisseur.Port);

        Assert.Equal(2, fournisseur.Requetes.Count);
        Assert.All(fournisseur.Requetes, requete =>
        {
            Assert.DoesNotContain("Cookie:", requete, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Accept-Encoding", requete, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Authorization", requete, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(2, fournisseur.Connexions);
    }

    [Fact]
    public async Task Un_corps_en_gzip_n_est_pas_decode()
    {
        var json = Encoding.UTF8.GetBytes("""{"type":"message"}""");
        using var compresse = new MemoryStream();
        await using (var gzip = new GZipStream(compresse, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(json, TestContext.Current.CancellationToken);
        }

        var octets = compresse.ToArray();
        await using var fournisseur = new EcouteLocale(_ =>
        [
            .. Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Encoding: gzip\r\nContent-Length: {octets.Length}\r\n\r\n"),
            .. octets,
        ]);
        using var transport = new TransportDeLIA();

        var brute = await EnvoyerAsync(transport, fournisseur.Port);

        Assert.True(brute.Encode);
        Assert.Equal(octets, brute.Corps);
    }

    [Fact]
    public async Task Un_corps_sans_fin_est_coupe_a_sa_borne()
    {
        await using var fournisseur = new EcouteLocale(
            _ => Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n"),
            sansFin: true);
        using var transport = new TransportDeLIA();

        var brute = await EnvoyerAsync(transport, fournisseur.Port, octets: 64 * 1024);

        Assert.Equal(EchecDuTransport.TropGrande, brute.Echec);
    }

    [Fact]
    public async Task Un_serveur_qui_se_tait_depasse_le_delai()
    {
        await using var fournisseur = new EcouteLocale(_ => null);
        using var transport = new TransportDeLIA();

        var brute = await EnvoyerAsync(transport, fournisseur.Port, duree: TimeSpan.FromMilliseconds(500));

        Assert.Equal(EchecDuTransport.DelaiDepasse, brute.Echec);
        Assert.Single(fournisseur.Requetes);
    }

    [Fact]
    public async Task Un_corps_qui_s_arrete_en_chemin_depasse_le_delai()
    {
        await using var fournisseur = new EcouteLocale(_ => Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{\"type\":"));
        using var transport = new TransportDeLIA();

        var brute = await EnvoyerAsync(transport, fournisseur.Port, duree: TimeSpan.FromMilliseconds(500));

        Assert.Equal(EchecDuTransport.DelaiDepasse, brute.Echec);
    }

    [Fact]
    public async Task Une_connexion_refusee_rend_injoignable()
    {
        int port;
        using (var libre = new TcpListener(IPAddress.Loopback, 0))
        {
            libre.Start();
            port = ((IPEndPoint)libre.LocalEndpoint).Port;
        }

        using var transport = new TransportDeLIA();

        var brute = await EnvoyerAsync(transport, port);

        Assert.Equal(EchecDuTransport.Injoignable, brute.Echec);
        Assert.Equal(nameof(HttpRequestException), brute.TypeDErreur);
    }

    private static byte[] Reponse(int statut, string corps) =>
        Encoding.ASCII.GetBytes($"HTTP/1.1 {statut} X\r\nContent-Type: text/plain\r\nContent-Length: {corps.Length}\r\nConnection: close\r\n\r\n{corps}");

    // Une requête comme celle de Claude, la clé dans x-api-key, vers l'écoute locale.
    private static async Task<ReponseBrute> EnvoyerAsync(TransportDeLIA transport, int port, int octets = 4096, TimeSpan? duree = null)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{port}/v1/messages"));
        requete.Headers.Add("x-api-key", Cle);
        requete.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return await EnvoiBorne.EnvoyerAsync(transport, requete, octets, duree ?? TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Une écoute sur 127.0.0.1 qui lit chaque requête (en-têtes, puis corps selon sa longueur), la note, et écrit la
    /// réponse que le test lui donne : rien (elle se tait), des octets, ou des octets suivis d'un corps sans fin.
    /// </summary>
    private sealed class EcouteLocale : IAsyncDisposable
    {
        private readonly TcpListener _ecoute = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _fin = new();
        private readonly ConcurrentQueue<string> _requetes = new();
        private readonly Func<string, byte[]?> _repondre;
        private readonly bool _sansFin;
        private readonly Task _boucle;
        private int _connexions;

        public EcouteLocale(Func<string, byte[]?> repondre, bool sansFin = false)
        {
            _repondre = repondre;
            _sansFin = sansFin;
            _ecoute.Start();
            _boucle = AccepterAsync();
        }

        public int Port => ((IPEndPoint)_ecoute.LocalEndpoint).Port;

        public IReadOnlyList<string> Requetes => [.. _requetes];

        public int Connexions => Volatile.Read(ref _connexions);

        public async ValueTask DisposeAsync()
        {
            await _fin.CancelAsync();
            _ecoute.Stop();
            try
            {
                await _boucle;
            }
            catch (Exception erreur) when (erreur is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // L'écoute s'arrête.
            }

            _fin.Dispose();
        }

        private async Task AccepterAsync()
        {
            while (!_fin.IsCancellationRequested)
            {
                var client = await _ecoute.AcceptTcpClientAsync(_fin.Token);
                Interlocked.Increment(ref _connexions);
                _ = ServirAsync(client);
            }
        }

        private async Task ServirAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var flux = client.GetStream();
                    var requete = await LireLaRequeteAsync(flux);
                    _requetes.Enqueue(requete);
                    var reponse = _repondre(requete);
                    if (reponse is null)
                    {
                        await Task.Delay(Timeout.Infinite, _fin.Token);
                        return;
                    }

                    await flux.WriteAsync(reponse, _fin.Token);
                    if (_sansFin)
                    {
                        var morceau = new byte[16 * 1024];
                        Array.Fill(morceau, (byte)'a');
                        while (!_fin.IsCancellationRequested)
                        {
                            await flux.WriteAsync(morceau, _fin.Token);
                        }
                    }
                    else
                    {
                        // Garde la connexion ouverte : le client décide s'il la réutilise.
                        await Task.Delay(Timeout.Infinite, _fin.Token);
                    }
                }
                catch (Exception erreur) when (erreur is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    // Le client est parti, ou l'écoute s'arrête.
                }
            }
        }

        private async Task<string> LireLaRequeteAsync(NetworkStream flux)
        {
            var octets = new List<byte>();
            var un = new byte[1];
            while (!EndsWith(octets, "\r\n\r\n"u8))
            {
                if (await flux.ReadAsync(un, _fin.Token) == 0)
                {
                    break;
                }

                octets.Add(un[0]);
            }

            var enTetes = Encoding.ASCII.GetString([.. octets]);
            var longueur = enTetes.Split("\r\n")
                .Where(ligne => ligne.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(ligne => int.Parse(ligne["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture))
                .FirstOrDefault();
            var corps = new byte[longueur];
            var lus = 0;
            while (lus < longueur)
            {
                var n = await flux.ReadAsync(corps.AsMemory(lus), _fin.Token);
                if (n == 0)
                {
                    break;
                }

                lus += n;
            }

            return enTetes + Encoding.UTF8.GetString(corps, 0, lus);
        }

        private static bool EndsWith(List<byte> octets, ReadOnlySpan<byte> fin) =>
            octets.Count >= fin.Length && System.Runtime.InteropServices.CollectionsMarshal.AsSpan(octets)[^fin.Length..].SequenceEqual(fin);
    }
}
