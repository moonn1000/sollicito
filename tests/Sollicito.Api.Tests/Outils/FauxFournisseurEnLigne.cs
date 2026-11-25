using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Le faux des fournisseurs d'IA en ligne des tests de l'API (#48, ADR-0002) : <see cref="ApiLancee"/> le pose toujours à
/// la place du transport, et aucune requête ne quitte le processus. Il copie chaque requête, puis répond comme le test l'a
/// prévu. Sans réponse prévue, il lève une exception et compte l'appel : l'API lancée refuse alors de se fermer sans le
/// dire, et le test échoue.
/// </summary>
public sealed class FauxFournisseurEnLigne : HttpMessageHandler
{
    /// <summary>Le message que portent les erreurs du faux : il ne doit paraître nulle part.</summary>
    public const string MessageDuFournisseur = "SOLLICITO-MESSAGE-DU-FOURNISSEUR";

    private readonly ConcurrentQueue<RequeteDuFaux> _recues = new();

    private volatile Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _repondre;

    private int _sansReponsePrevue;

    /// <summary>Les requêtes reçues, dans l'ordre.</summary>
    public IReadOnlyList<RequeteDuFaux> Recues => [.. _recues];

    /// <summary>Le nombre d'appels arrivés sans réponse prévue.</summary>
    public int AppelsSansReponsePrevue => Volatile.Read(ref _sansReponsePrevue);

    /// <summary>Répond comme <paramref name="repondre"/> à chaque requête qui suit.</summary>
    public void Repondre(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> repondre) => _repondre = repondre;

    /// <summary>Répond par ce statut et ce corps JSON à chaque requête qui suit.</summary>
    public void Repondre(HttpStatusCode statut, string corps) => Repondre((_, _) => Task.FromResult(Json(statut, corps)));

    /// <summary>Répond comme le fournisseur de la requête répondrait à une demande réussie : « OK ».</summary>
    public void RepondreReussi() => Repondre((requete, _) => Task.FromResult(Json(HttpStatusCode.OK, Reussie(requete))));

    /// <summary>Répond par une erreur, dont le corps porte un message reconnaissable.</summary>
    public void RepondreErreur(HttpStatusCode statut) =>
        Repondre(statut, $$$"""{"type":"error","error":{"type":"faux","code":"faux","message":"{{{MessageDuFournisseur}}}"}}""");

    /// <summary>La réponse réussie du fournisseur de <paramref name="requete"/>.</summary>
    public static string Reussie(HttpRequestMessage requete)
    {
        ArgumentNullException.ThrowIfNull(requete);
        return requete.RequestUri?.Host == "api.anthropic.com"
            ? """{"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"OK"}],"stop_reason":"end_turn","stop_sequence":null}"""
            : """{"id":"resp_1","object":"response","status":"completed","model":"gpt-6-luna","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"OK"}]}]}""";
    }

    /// <summary>Une réponse JSON.</summary>
    public static HttpResponseMessage Json(HttpStatusCode statut, string corps) =>
        new(statut) { Content = new StringContent(corps, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var enTetes = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .SelectMany(e => e.Value.Select(valeur => new KeyValuePair<string, string>(e.Key, valeur)))
            .ToList();
        var corps = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        _recues.Enqueue(new RequeteDuFaux(request.Method.Method, request.RequestUri!.OriginalString, enTetes, corps));
        if (_repondre is not { } repondre)
        {
            Interlocked.Increment(ref _sansReponsePrevue);
            throw new InvalidOperationException("Aucune réponse prévue : un test n'atteint jamais le réseau.");
        }

        return await repondre(request, cancellationToken);
    }
}

/// <summary>Une requête reçue par le faux.</summary>
/// <param name="Methode">La méthode HTTP.</param>
/// <param name="Adresse">L'adresse, telle qu'écrite.</param>
/// <param name="EnTetes">Chaque en-tête, de la requête et de son contenu, avec ses valeurs.</param>
/// <param name="Corps">Le corps, en octets.</param>
public sealed record RequeteDuFaux(string Methode, string Adresse, IReadOnlyList<KeyValuePair<string, string>> EnTetes, byte[] Corps)
{
    /// <summary>Les valeurs de l'en-tête nommé, à la casse près.</summary>
    public IEnumerable<string> Valeurs(string nom) =>
        EnTetes.Where(e => string.Equals(e.Key, nom, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value);

    /// <summary>Le corps, en texte.</summary>
    public string Texte => Encoding.UTF8.GetString(Corps);
}
