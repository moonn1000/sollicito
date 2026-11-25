using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sollicito.Application.IA;

namespace Sollicito.Infrastructure.IA.EnLigne;

/// <summary>
/// L'adaptateur de Claude, d'Anthropic (#48, ADR-0017) : l'API Messages, à son adresse officielle, fixée ici, en HTTPS. La
/// clé part seulement dans l'en-tête <c>x-api-key</c>. Les consignes vont dans <c>system</c>, les données seules dans le
/// message de l'utilisateur. Le modèle est fixé ici, raisonnement coupé : la borne de la réponse ne couvre que son texte.
/// </summary>
public sealed class ClaudeEnLigne(TransportDeLIA transport, ISourceDeLaCle cles, PorteDesDemandes porte, ILogger<ClaudeEnLigne> journal)
    : FournisseurEnLigne(transport, cles, porte, journal)
{
    /// <summary>L'adresse officielle de l'API Messages d'Anthropic.</summary>
    public static readonly Uri Adresse = new("https://api.anthropic.com/v1/messages");

    /// <summary>Le modèle de cette version de Sollicito : un instantané figé, que son retrait rend indisponible.</summary>
    public const string Modele = "claude-haiku-5-5";

    /// <summary>La version de l'API d'Anthropic que la requête suit.</summary>
    public const string VersionDeLApi = "2026-10-21";

    /// <summary>Le seul en-tête qui porte la clé.</summary>
    public const string EnTeteDeLaCle = "x-api-key";

    /// <inheritdoc />
    public override FournisseurDIA Fournisseur => FournisseurDIA.Claude;

    /// <inheritdoc />
    protected override HttpRequestMessage Construire(DemandeALIA demande, CleDIA cle)
    {
        ArgumentNullException.ThrowIfNull(demande);
        ArgumentNullException.ThrowIfNull(cle);
        var requete = new HttpRequestMessage(HttpMethod.Post, Adresse);
        requete.Headers.Add(EnTeteDeLaCle, cle.Valeur());
        requete.Headers.Add("anthropic-version", VersionDeLApi);
        requete.Content = CorpsJson(json =>
        {
            json.WriteStartObject();
            json.WriteString("model", Modele);
            json.WriteNumber("max_tokens", JetonsDeLaReponse(demande));
            json.WriteString("system", demande.Consignes);
            json.WriteStartArray("messages");
            json.WriteStartObject();
            json.WriteString("role", "user");
            json.WriteString("content", DonneesAEnvoyer(demande));
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteStartObject("thinking");
            json.WriteString("type", "disabled");
            json.WriteEndObject();
            json.WriteEndObject();
        });
        return requete;
    }

    /// <inheritdoc />
    protected override ReponseDeLIA LireLeCorps(JsonElement racine, DemandeALIA demande)
    {
        if (!Chaine(racine, "type", out var type) || type != "message"
            || !Tableau(racine, "content", out var blocs)
            || !Chaine(racine, "stop_reason", out var fin))
        {
            return ReponseIllisible();
        }

        switch (fin)
        {
            case "end_turn" or "stop_sequence":
                break;
            case "max_tokens" or "model_context_window_exceeded":
                return ReponseTropLongue();
            case "refusal":
                return Refus;
            default:
                // tool_use, pause_turn, ou une fin inconnue : rien de ce qu'une demande sans outil attend.
                return ReponseIllisible();
        }

        var texte = new StringBuilder();
        foreach (var bloc in blocs.EnumerateArray())
        {
            if (!Chaine(bloc, "type", out var sorte))
            {
                return ReponseIllisible();
            }

            if (sorte != "text")
            {
                continue;
            }

            if (!Chaine(bloc, "text", out var morceau))
            {
                return ReponseIllisible();
            }

            if (!Ajouter(texte, morceau, demande))
            {
                return ReponseTropLongue();
            }
        }

        return Texte(texte, demande);
    }
}
