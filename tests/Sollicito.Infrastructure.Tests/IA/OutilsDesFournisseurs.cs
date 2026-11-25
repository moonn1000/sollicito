using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sollicito.Application.IA;
using Sollicito.Infrastructure.IA;
using Sollicito.Infrastructure.IA.EnLigne;

namespace Sollicito.Infrastructure.Tests.IA;

/// <summary>
/// Les outils des tests des fournisseurs en ligne (#48) : un faux gestionnaire HTTP, qui ne touche jamais le réseau
/// (ADR-0002), une fausse source de la clé, un journal qui garde ses entrées, et des clés fictives reconnaissables.
/// </summary>
internal static class OutilsDesFournisseurs
{
    /// <summary>Une clé de Claude fictive, reconnaissable.</summary>
    public const string CleDeClaude = "sk-ant-api03-SOLLICITO-ESSAI-48-cle-fictive-de-claude-0001";

    /// <summary>Une clé d'OpenAI fictive, reconnaissable.</summary>
    public const string CleDOpenAI = "sk-proj-SOLLICITO-ESSAI-48-cle-fictive-d-openai-0001";

    /// <summary>La partie reconnaissable des clés fictives.</summary>
    public const string PartieReconnaissable = "SOLLICITO-ESSAI-48";

    /// <summary>Le message que portent les erreurs des faux fournisseurs : il ne doit paraître nulle part.</summary>
    public const string MessageDuFournisseur = "SOLLICITO-MESSAGE-DU-FOURNISSEUR";

    /// <summary>Des consignes et des données reconnaissables.</summary>
    public const string Consignes = "Réponds en une phrase : SOLLICITO-CONSIGNE-48.";

    public const string Donnees = "Une offre fictive : SOLLICITO-DONNEE-48, développeur à Namur.";

    /// <summary>Une clé admise par sa forme, pour <paramref name="fournisseur"/>.</summary>
    public static CleDIA Cle(FournisseurDIA fournisseur, string valeur)
    {
        Assert.Null(FormeDUneCle.Verifier(fournisseur, valeur, out var cle));
        return cle!;
    }

    /// <summary>La clé fictive de <paramref name="fournisseur"/>.</summary>
    public static string CleFictive(FournisseurDIA fournisseur) => fournisseur == FournisseurDIA.Claude ? CleDeClaude : CleDOpenAI;

    /// <summary>Une demande ordinaire, reconnaissable.</summary>
    public static DemandeALIA Demande(int caracteres = 200, TimeSpan? duree = null, string donnees = Donnees) =>
        new(Consignes, donnees, caracteres, duree ?? TimeSpan.FromSeconds(30));

    /// <summary>Une réponse JSON, du type donné.</summary>
    public static HttpResponseMessage Json(HttpStatusCode statut, string texte, string type = "application/json")
    {
        var contenu = new ByteArrayContent(Encoding.UTF8.GetBytes(texte));
        contenu.Headers.TryAddWithoutValidation("Content-Type", type);
        return new HttpResponseMessage(statut) { Content = contenu };
    }

    /// <summary>Une erreur du fournisseur, dont le corps porte un message reconnaissable et une partie de la clé.</summary>
    public static HttpResponseMessage Erreur(HttpStatusCode statut) =>
        Json(statut, $$$"""{"type":"error","error":{"type":"faux","code":"faux","message":"{{{MessageDuFournisseur}}} sk-...{{{PartieReconnaissable}}}"}}""");

    /// <summary>La réponse réussie de Claude, avec ce texte et cette fin.</summary>
    public static string ReponseDeClaude(string texte = "OK", string fin = "end_turn") =>
        $$$"""{"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":{{{JsonSerializer.Serialize(texte)}}}}],"stop_reason":"{{{fin}}}","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":1}}""";

    /// <summary>La réponse réussie d'OpenAI, avec ce texte.</summary>
    public static string ReponseDOpenAI(string texte = "OK") =>
        $$$"""{"id":"resp_1","object":"response","status":"completed","model":"gpt-6-luna","output":[{"type":"reasoning","id":"rs_1","summary":[]},{"type":"message","id":"msg_1","status":"completed","role":"assistant","content":[{"type":"output_text","text":{{{JsonSerializer.Serialize(texte)}}},"annotations":[]}]}],"usage":{"input_tokens":12,"output_tokens":1,"total_tokens":13}}""";

    /// <summary>La réponse réussie de <paramref name="fournisseur"/>.</summary>
    public static string ReponseReussie(FournisseurDIA fournisseur, string texte = "OK") =>
        fournisseur == FournisseurDIA.Claude ? ReponseDeClaude(texte) : ReponseDOpenAI(texte);

    /// <summary>
    /// Un adaptateur de <paramref name="fournisseur"/> sur un faux gestionnaire, avec la clé fictive lisible, la porte et le
    /// journal donnés ou neufs.
    /// </summary>
    public static FournisseurEnLigne Adaptateur(
        FournisseurDIA fournisseur,
        FauxGestionnaire faux,
        SourceDeTest? cles = null,
        PorteDesDemandes? porte = null,
        JournalDeTest? journal = null)
    {
        var transport = new TransportDeLIA(() => faux);
        cles ??= SourceDeTest.Lisible(fournisseur);
        porte ??= new PorteDesDemandes();
        journal ??= new JournalDeTest();
        return fournisseur == FournisseurDIA.Claude
            ? new ClaudeEnLigne(transport, cles, porte, journal.Pour<ClaudeEnLigne>())
            : new OpenAIEnLigne(transport, cles, porte, journal.Pour<OpenAIEnLigne>());
    }
}

/// <summary>Une requête reçue par le faux, copiée avant que l'adaptateur ne la libère.</summary>
/// <param name="Methode">La méthode HTTP.</param>
/// <param name="Adresse">L'adresse, telle qu'écrite.</param>
/// <param name="EnTetes">Chaque en-tête, de la requête et de son contenu, avec ses valeurs.</param>
/// <param name="Corps">Le corps, en octets.</param>
internal sealed record RequeteRecue(string Methode, string Adresse, IReadOnlyList<KeyValuePair<string, string>> EnTetes, byte[] Corps)
{
    /// <summary>Les valeurs de l'en-tête nommé, à la casse près.</summary>
    public IEnumerable<string> Valeurs(string nom) =>
        EnTetes.Where(e => string.Equals(e.Key, nom, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value);

    /// <summary>Le corps, lu en JSON.</summary>
    public JsonElement CorpsJson()
    {
        using var document = JsonDocument.Parse(Corps);
        return document.RootElement.Clone();
    }
}

/// <summary>
/// Le faux gestionnaire HTTP des tests : il copie chaque requête, puis rend ce que le test a prévu. Sans réponse prévue, il
/// lève une exception : un test oublié n'atteint jamais le réseau.
/// </summary>
internal sealed class FauxGestionnaire : HttpMessageHandler
{
    private readonly ConcurrentQueue<RequeteRecue> _recues = new();

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _repondre;

    /// <summary>Un faux qui rend toujours la réponse que fabrique <paramref name="reponse"/>.</summary>
    public FauxGestionnaire(Func<HttpResponseMessage> reponse)
    {
        _repondre = (_, _) => Task.FromResult(reponse());
    }

    /// <summary>Un faux qui répond comme <paramref name="repondre"/>.</summary>
    public FauxGestionnaire(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> repondre)
    {
        _repondre = repondre;
    }

    /// <summary>Un faux sans réponse prévue.</summary>
    public FauxGestionnaire()
    {
    }

    /// <summary>Les requêtes reçues, dans l'ordre.</summary>
    public IReadOnlyList<RequeteRecue> Recues => [.. _recues];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var enTetes = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .SelectMany(e => e.Value.Select(valeur => new KeyValuePair<string, string>(e.Key, valeur)))
            .ToList();
        var corps = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        _recues.Enqueue(new RequeteRecue(request.Method.Method, request.RequestUri!.OriginalString, enTetes, corps));
        var repondre = _repondre ?? throw new InvalidOperationException("Aucune réponse prévue : un test n'atteint jamais le réseau.");
        return await repondre(request, cancellationToken);
    }
}

/// <summary>Une fausse source de la clé : elle rend la lecture prévue, et note chaque fournisseur demandé.</summary>
internal sealed class SourceDeTest(Func<FournisseurDIA, LectureDeLaCle> lire) : ISourceDeLaCle
{
    private readonly ConcurrentQueue<FournisseurDIA> _demandes = new();

    /// <summary>Les fournisseurs pour lesquels la clé a été lue.</summary>
    public IReadOnlyList<FournisseurDIA> Demandes => [.. _demandes];

    /// <summary>La clé fictive de <paramref name="fournisseur"/>, lisible, et aucune pour un autre.</summary>
    public static SourceDeTest Lisible(FournisseurDIA fournisseur) =>
        new(demande => demande == fournisseur
            ? LectureDeLaCle.Lisible(OutilsDesFournisseurs.Cle(fournisseur, OutilsDesFournisseurs.CleFictive(fournisseur)))
            : LectureDeLaCle.Absente);

    public LectureDeLaCle Lire(FournisseurDIA fournisseur)
    {
        _demandes.Enqueue(fournisseur);
        return lire(fournisseur);
    }
}

/// <summary>Un journal qui garde chaque entrée : son niveau, son numéro, son message et ses valeurs.</summary>
internal sealed class JournalDeTest
{
    private readonly ConcurrentQueue<string> _entrees = new();

    /// <summary>Les entrées, une chaîne par entrée.</summary>
    public IReadOnlyList<string> Entrees => [.. _entrees];

    /// <summary>Un journal pour la catégorie de <typeparamref name="T"/>.</summary>
    public ILogger<T> Pour<T>() => new Journal<T>(this);

    private sealed class Journal<T>(JournalDeTest capture) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entree = new StringBuilder().Append(logLevel).Append(' ').Append(eventId.Id).Append(" : ").Append(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> valeurs)
            {
                foreach (var (cle, valeur) in valeurs)
                {
                    entree.Append(" | ").Append(cle).Append('=').Append(valeur);
                }
            }

            if (exception is not null)
            {
                entree.Append(" | exception=").Append(exception);
            }

            capture._entrees.Enqueue(entree.ToString());
        }
    }
}
