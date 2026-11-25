#if VERSION_DE_TEST
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Sollicito.Fenetre;

/// <summary>
/// Le faux des fournisseurs d'IA en ligne de la version de test (#48, ADR-0002, ADR-0017). Il remplace tout le transport :
/// aucune requête ne quitte le processus, et aucune variable ne le renvoie vers le réseau.
/// <list type="bullet">
/// <item>Il répond selon le scénario écrit dans un fichier du dossier de données, relu à chaque demande ; sans lui, comme un
/// réseau absent.</item>
/// <item>Il note chaque demande sur une ligne d'un autre fichier : la méthode, l'adresse, les noms des en-têtes, l'en-tête
/// qui porte la clé et l'empreinte SHA-256 de la clé, si la clé paraît dans l'adresse ou dans le corps, et la taille du
/// corps. Jamais la clé elle-même, ni le corps.</item>
/// <item>Ses erreurs portent un message reconnaissable, que Sollicito ne doit jamais lire ni recopier.</item>
/// </list>
/// </summary>
internal sealed class FauxFournisseurDeLIA(string fichierDuScenario, string fichierDesDemandes) : HttpMessageHandler
{
    /// <summary>Le message que portent les erreurs du faux : il ne doit paraître nulle part.</summary>
    public const string MessageDuFournisseur = "SOLLICITO-MESSAGE-DU-FOURNISSEUR";

    private static readonly SemaphoreSlim EcrituresDesDemandes = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scenario = File.Exists(fichierDuScenario) ? (await File.ReadAllTextAsync(fichierDuScenario, cancellationToken)).Trim() : "reseau-absent";
        var corps = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        await NoterAsync(request, corps, scenario, cancellationToken);

        var deClaude = request.RequestUri?.Host == "api.anthropic.com";
        switch (scenario)
        {
            case "reseau-absent":
                throw new HttpRequestException("Faux réseau absent.", null, statusCode: null);
            case "delai":
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            case "redirection":
                var redirection = new HttpResponseMessage(HttpStatusCode.Found);
                redirection.Headers.Location = new Uri("https://exemple.invalid/v1/redirige");
                return redirection;
            case "trop-grande":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FluxSansFin()) };
            case "mal-formee":
                return Json(HttpStatusCode.OK, "{\"type\":\"message\",\"content\":[");
            case "coupee":
                return Json(HttpStatusCode.OK, deClaude ? Claude("OK", "max_tokens") : """{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}""");
            case "refus":
                return Json(HttpStatusCode.OK, deClaude ? Claude("", "refusal") : """{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"refusal","refusal":"Non."}]}]}""");
            case "reussi":
                return Json(HttpStatusCode.OK, deClaude ? Claude("OK", "end_turn") : """{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"OK","annotations":[]}]}]}""");
            default:
                if (int.TryParse(scenario, NumberStyles.None, CultureInfo.InvariantCulture, out var statut) && statut is >= 400 and <= 599)
                {
                    return Json((HttpStatusCode)statut, $$$"""{"type":"error","error":{"type":"faux","code":"faux","message":"{{{MessageDuFournisseur}}}"}}""");
                }

                throw new HttpRequestException("Scénario inconnu.", null, statusCode: null);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode statut, string texte) =>
        new(statut) { Content = new StringContent(texte, Encoding.UTF8, "application/json") };

    private static string Claude(string texte, string fin) =>
        $$$"""{"id":"msg_essai","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"{{{texte}}}"}],"stop_reason":"{{{fin}}}","stop_sequence":null,"usage":{"input_tokens":20,"output_tokens":1}}""";

    // Une ligne par demande, sans la clé ni le corps : ce qu'il faut pour vérifier l'adresse, l'en-tête et l'empreinte.
    private async Task NoterAsync(HttpRequestMessage requete, byte[] corps, string scenario, CancellationToken annulation)
    {
        var (enTete, cle) = requete.Headers.TryGetValues("x-api-key", out var valeurs)
            ? ("x-api-key", valeurs.FirstOrDefault() ?? "")
            : requete.Headers.Authorization is { Scheme: "Bearer", Parameter: { } jeton }
                ? ("Authorization: Bearer", jeton)
                : ("aucun", "");
        var noms = requete.Headers.Select(e => e.Key)
            .Concat(requete.Content?.Headers.Select(e => e.Key) ?? [])
            .Select(nom => nom.ToLowerInvariant())
            .Order(StringComparer.Ordinal);
        var empreinte = cle.Length == 0 ? "aucune" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(cle)));
        var dansLAdresse = cle.Length > 0 && (requete.RequestUri?.OriginalString.Contains(cle, StringComparison.Ordinal) ?? false);
        var dansLeCorps = cle.Length > 0 && Encoding.UTF8.GetString(corps).Contains(cle, StringComparison.Ordinal);
        var ligne = string.Create(
            CultureInfo.InvariantCulture,
            $"{requete.Method} {requete.RequestUri} | en-têtes : {string.Join(", ", noms)} | clé : {enTete} sha256={empreinte} | clé dans l'adresse : {(dansLAdresse ? "oui" : "non")} | clé dans le corps : {(dansLeCorps ? "oui" : "non")} | corps : {corps.Length} octets | scénario : {scenario}");
        await EcrituresDesDemandes.WaitAsync(annulation);
        try
        {
            await File.AppendAllTextAsync(fichierDesDemandes, ligne + Environment.NewLine, annulation);
        }
        finally
        {
            EcrituresDesDemandes.Release();
        }
    }

    // Un corps qui ne finit jamais, sans longueur annoncée : Sollicito doit s'arrêter à sa borne.
    private sealed class FluxSansFin : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'a', offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
#endif
