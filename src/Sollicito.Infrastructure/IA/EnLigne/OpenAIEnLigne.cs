using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sollicito.Application.IA;

namespace Sollicito.Infrastructure.IA.EnLigne;

/// <summary>
/// L'adaptateur d'OpenAI (#48, ADR-0017) : l'API Responses, à son adresse officielle, fixée ici, en HTTPS. La clé part
/// seulement dans l'en-tête <c>Authorization: Bearer</c>. Les consignes vont dans <c>instructions</c>, les données seules dans
/// <c>input</c>. Le modèle est fixé ici, raisonnement coupé, et la réponse n'est pas gardée chez OpenAI (<c>store: false</c>).
/// </summary>
public sealed class OpenAIEnLigne(TransportDeLIA transport, ISourceDeLaCle cles, PorteDesDemandes porte, ILogger<OpenAIEnLigne> journal)
    : FournisseurEnLigne(transport, cles, porte, journal)
{
    /// <summary>L'adresse officielle de l'API Responses d'OpenAI.</summary>
    public static readonly Uri Adresse = new("https://api.openai.com/v1/responses");

    /// <summary>Le modèle de cette version de Sollicito, que son retrait rend indisponible.</summary>
    public const string Modele = "gpt-6-luna";

    /// <inheritdoc />
    public override FournisseurDIA Fournisseur => FournisseurDIA.OpenAI;

    /// <inheritdoc />
    protected override HttpRequestMessage Construire(DemandeALIA demande, CleDIA cle)
    {
        ArgumentNullException.ThrowIfNull(demande);
        ArgumentNullException.ThrowIfNull(cle);
        var requete = new HttpRequestMessage(HttpMethod.Post, Adresse);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cle.Valeur());
        requete.Content = CorpsJson(json =>
        {
            json.WriteStartObject();
            json.WriteString("model", Modele);
            json.WriteString("instructions", demande.Consignes);
            json.WriteString("input", DonneesAEnvoyer(demande));
            json.WriteNumber("max_output_tokens", JetonsDeLaReponse(demande));
            json.WriteStartObject("reasoning");
            json.WriteString("effort", "none");
            json.WriteEndObject();
            json.WriteBoolean("store", false);
            json.WriteEndObject();
        });
        return requete;
    }

    /// <inheritdoc />
    protected override ReponseDeLIA LireLeCorps(JsonElement racine, DemandeALIA demande)
    {
        if (!Chaine(racine, "status", out var statut))
        {
            return ReponseIllisible();
        }

        switch (statut)
        {
            case "completed":
                break;
            case "incomplete":
                var raison = racine.TryGetProperty("incomplete_details", out var details) && Chaine(details, "reason", out var lue) ? lue : "";
                return raison switch
                {
                    "max_output_tokens" => ReponseTropLongue(),
                    "content_filter" => Refus,
                    _ => ReponseIllisible(),
                };
            case "failed":
                return EnPanne;
            default:
                // queued, in_progress, cancelled, ou un statut inconnu : rien de ce qu'une demande sans attente rend.
                return ReponseIllisible();
        }

        if (!Tableau(racine, "output", out var elements))
        {
            return ReponseIllisible();
        }

        var texte = new StringBuilder();
        foreach (var element in elements.EnumerateArray())
        {
            if (!Chaine(element, "type", out var type))
            {
                return ReponseIllisible();
            }

            // Le raisonnement et tout autre élément sont ignorés : seul le message porte le texte.
            if (type != "message")
            {
                continue;
            }

            if (!Tableau(element, "content", out var parties))
            {
                return ReponseIllisible();
            }

            foreach (var partie in parties.EnumerateArray())
            {
                if (!Chaine(partie, "type", out var sorte))
                {
                    return ReponseIllisible();
                }

                if (sorte == "refusal")
                {
                    return Refus;
                }

                if (sorte != "output_text")
                {
                    continue;
                }

                if (!Chaine(partie, "text", out var morceau))
                {
                    return ReponseIllisible();
                }

                if (!Ajouter(texte, morceau, demande))
                {
                    return ReponseTropLongue();
                }
            }
        }

        return Texte(texte, demande);
    }
}
