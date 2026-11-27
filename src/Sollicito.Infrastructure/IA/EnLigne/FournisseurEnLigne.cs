using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sollicito.Application.IA;

namespace Sollicito.Infrastructure.IA.EnLigne;

/// <summary>
/// Ce que les adaptateurs des fournisseurs en ligne ont en commun (#48 ; ADR-0001, D7 et D9 ; ADR-0016 ; ADR-0017) :
/// <list type="bullet">
/// <item>une demande à la fois (<see cref="PorteDesDemandes"/>) ;</item>
/// <item>la clé lue au moment de l'appel, pour ce seul fournisseur, et envoyée seulement dans l'en-tête qu'il attend ;
/// l'adaptateur ne la garde pas ;</item>
/// <item>une seule requête, vers l'adresse fixée dans le code, par un envoi borné en durée et en taille
/// (<see cref="EnvoiBorne"/>) ;</item>
/// <item>une erreur classée par son seul statut HTTP, sans lire son corps ;</item>
/// <item>une réponse lue en JSON strict, traitée comme non fiable, et rendue en texte borné ou en issue fermée ;</item>
/// <item>un journal qui dit le fournisseur, l'issue, le statut et la durée, jamais la clé, la demande ni la réponse.</item>
/// </list>
/// </summary>
public abstract partial class FournisseurEnLigne : IFournisseurDIA
{
    /// <summary>Le plus gros corps de réponse lu : 20 000 caractères, même tous échappés, y tiennent.</summary>
    public const int OctetsMaximumDeLaReponse = 256 * 1024;

    /// <summary>Le moins de jetons demandés (OpenAI n'en admet pas moins de 16).</summary>
    public const int JetonsMinimum = 16;

    /// <summary>Le plus de jetons demandés : la moitié des 20 000 caractères qu'une réponse admet au plus.</summary>
    public const int JetonsMaximum = 10_000;

    /// <summary>Ce qui part quand une demande n'a aucune donnée : un fournisseur refuse un message vide.</summary>
    public const string SansDonnees = "Aucune donnée.";

    /// <summary>
    /// La lecture stricte d'une réponse : profondeur bornée, ni commentaire, ni virgule finale, ni propriété répétée. Les champs
    /// inconnus sont ignorés : les fournisseurs en ajoutent sans prévenir.
    /// </summary>
    internal static readonly JsonDocumentOptions LectureStricte = new()
    {
        MaxDepth = 32,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private static readonly ReponseDeLIA Illisible = new ReponseDeLIA.Echec(SorteDEchec.ReponseIllisible);

    private static readonly ReponseDeLIA TropLongue = new ReponseDeLIA.Echec(SorteDEchec.ReponseTropLongue);

    private readonly TransportDeLIA _transport;

    private readonly ISourceDeLaCle _cles;

    private readonly PorteDesDemandes _porte;

    private readonly ILogger _journal;

    /// <summary>Un adaptateur, sur son transport, sa source de la clé et la porte commune des demandes.</summary>
    protected FournisseurEnLigne(TransportDeLIA transport, ISourceDeLaCle cles, PorteDesDemandes porte, ILogger journal)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(cles);
        ArgumentNullException.ThrowIfNull(porte);
        ArgumentNullException.ThrowIfNull(journal);
        _transport = transport;
        _cles = cles;
        _porte = porte;
        _journal = journal;
    }

    /// <inheritdoc />
    public abstract FournisseurDIA Fournisseur { get; }

    /// <summary>Le refus du fournisseur, ou de son contenu.</summary>
    protected static ReponseDeLIA Refus { get; } = new ReponseDeLIA.Echec(SorteDEchec.RefusDuFournisseur);

    /// <inheritdoc />
    public async Task<ReponseDeLIA> DemanderAsync(DemandeALIA demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        annulation.ThrowIfCancellationRequested();
        if (!_porte.Entrer())
        {
            DemandeDejaEnCours(Fournisseur);
            return new ReponseDeLIA.Echec(SorteDEchec.UneDemandeEnCours);
        }

        try
        {
            var lecture = _cles.Lire(Fournisseur);
            if (lecture.Etat != EtatDeLaCle.Enregistree || lecture.Cle is not { } cle)
            {
                var raison = lecture.Etat == EtatDeLaCle.Illisible ? RaisonDIndisponibilite.CleIllisible : RaisonDIndisponibilite.CleAbsente;
                SansClePrete(Fournisseur, raison);
                return new ReponseDeLIA.Indisponible(raison);
            }

            var debut = Stopwatch.GetTimestamp();
            ReponseBrute brute;
            using (var requete = Construire(demande, cle))
            {
                brute = await EnvoiBorne.EnvoyerAsync(_transport, requete, OctetsMaximumDeLaReponse, demande.Duree, annulation);
            }

            var reponse = Interpreter(brute, demande);
            Noter(reponse, brute, (long)Stopwatch.GetElapsedTime(debut).TotalMilliseconds);
            return reponse;
        }
        finally
        {
            _porte.Sortir();
        }
    }

    /// <summary>
    /// La classe d'une réponse en erreur, par son seul statut HTTP : son corps n'est jamais lu (ADR-0017). Une redirection,
    /// jamais suivie, et tout statut inattendu valent une réponse illisible.
    /// </summary>
    internal static SorteDEchec Classer(int statut) => statut switch
    {
        401 => SorteDEchec.CleRefusee,
        402 or 429 => SorteDEchec.QuotaAtteint,
        403 => SorteDEchec.AccesRefuse,
        404 => SorteDEchec.ModeleIndisponible,
        400 or 413 or 422 => SorteDEchec.RefusDuFournisseur,
        408 or 500 or 502 or 503 or 504 or 529 => SorteDEchec.FournisseurEnPanne,
        _ => SorteDEchec.ReponseIllisible,
    };

    /// <summary>La requête de <paramref name="demande"/>, vers l'adresse fixée, la clé dans l'en-tête de ce fournisseur.</summary>
    protected abstract HttpRequestMessage Construire(DemandeALIA demande, CleDIA cle);

    /// <summary>Le texte d'une réponse 200 lue en JSON, ou son issue fermée.</summary>
    protected abstract ReponseDeLIA LireLeCorps(JsonElement racine, DemandeALIA demande);

    /// <summary>Le nombre de jetons à demander : la moitié des caractères admis, entre les bornes.</summary>
    protected static int JetonsDeLaReponse(DemandeALIA demande)
    {
        ArgumentNullException.ThrowIfNull(demande);
        return Math.Clamp((demande.CaracteresDeLaReponse + 1) / 2, JetonsMinimum, JetonsMaximum);
    }

    /// <summary>Les données de la demande, ou <see cref="SansDonnees"/> s'il n'y en a pas.</summary>
    protected static string DonneesAEnvoyer(DemandeALIA demande)
    {
        ArgumentNullException.ThrowIfNull(demande);
        return demande.Donnees.Length == 0 ? SansDonnees : demande.Donnees;
    }

    /// <summary>Un corps JSON, écrit d'un bloc.</summary>
    protected static ByteArrayContent CorpsJson(Action<Utf8JsonWriter> ecrire)
    {
        ArgumentNullException.ThrowIfNull(ecrire);
        var tampon = new ArrayBufferWriter<byte>();
        using (var ecrivain = new Utf8JsonWriter(tampon))
        {
            ecrire(ecrivain);
        }

        var contenu = new ByteArrayContent(tampon.WrittenSpan.ToArray());
        contenu.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return contenu;
    }

    /// <summary>La chaîne d'une propriété, si elle existe et en est une.</summary>
    protected static bool Chaine(JsonElement objet, string nom, out string valeur)
    {
        if (objet.ValueKind == JsonValueKind.Object
            && objet.TryGetProperty(nom, out var propriete)
            && propriete.ValueKind == JsonValueKind.String)
        {
            valeur = propriete.GetString()!;
            return true;
        }

        valeur = "";
        return false;
    }

    /// <summary>Le tableau d'une propriété, s'il existe et en est un.</summary>
    protected static bool Tableau(JsonElement objet, string nom, out JsonElement valeur)
    {
        if (objet.ValueKind == JsonValueKind.Object
            && objet.TryGetProperty(nom, out valeur)
            && valeur.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        valeur = default;
        return false;
    }

    /// <summary>
    /// Ajoute un morceau du texte de l'IA ; faux si le texte dépasse déjà la borne de la demande, qui le refuse alors.
    /// </summary>
    protected static bool Ajouter(StringBuilder texte, string morceau, DemandeALIA demande)
    {
        ArgumentNullException.ThrowIfNull(texte);
        ArgumentNullException.ThrowIfNull(demande);
        texte.Append(morceau);
        return texte.Length <= demande.CaracteresDeLaReponse;
    }

    /// <summary>Le texte de l'IA, s'il n'est pas vide et tient dans la borne de la demande.</summary>
    protected static ReponseDeLIA Texte(StringBuilder texte, DemandeALIA demande)
    {
        ArgumentNullException.ThrowIfNull(texte);
        ArgumentNullException.ThrowIfNull(demande);
        if (texte.Length == 0)
        {
            return Illisible;
        }

        return texte.Length > demande.CaracteresDeLaReponse ? TropLongue : new ReponseDeLIA.Texte(texte.ToString());
    }

    /// <summary>Une réponse qui ne se lit pas comme on l'attend.</summary>
    protected static ReponseDeLIA ReponseIllisible() => Illisible;

    /// <summary>Une réponse coupée par le fournisseur, ou plus longue que la borne.</summary>
    protected static ReponseDeLIA ReponseTropLongue() => TropLongue;

    // Ce qui est revenu, rendu par le port : un échec du transport, une erreur classée par son statut, ou le corps d'un 200 lu.
    private ReponseDeLIA Interpreter(ReponseBrute brute, DemandeALIA demande)
    {
        switch (brute.Echec)
        {
            case EchecDuTransport.Injoignable:
                return new ReponseDeLIA.Echec(SorteDEchec.Injoignable);
            case EchecDuTransport.DelaiDepasse:
                return new ReponseDeLIA.Echec(SorteDEchec.DelaiDepasse);
            case EchecDuTransport.TropGrande:
                return TropLongue;
        }

        var statut = brute.Statut ?? 0;
        if (brute.Corps is not { } corps)
        {
            return new ReponseDeLIA.Echec(Classer(statut));
        }

        if (statut != 200 || brute.Encode || !string.Equals(brute.TypeDuContenu, "application/json", StringComparison.Ordinal))
        {
            return Illisible;
        }

        try
        {
            using var document = JsonDocument.Parse(corps, LectureStricte);
            return LireLeCorps(document.RootElement, demande);
        }
        catch (Exception erreur) when (erreur is JsonException or InvalidOperationException)
        {
            // Du JSON mal formé, ou valide mais illisible en texte : un échappement de substitut sans son pareil, dans un texte
            // ou dans un nom de propriété, fait lever InvalidOperationException par la lecture. La réponse n'est pas fiable,
            // et le port ne laisse sortir aucune exception.
            return Illisible;
        }
    }

    // Une entrée par demande partie : le fournisseur, l'issue, le statut et la durée ; le type d'une erreur du transport.
    private void Noter(ReponseDeLIA reponse, ReponseBrute brute, long millisecondes)
    {
        var statut = brute.Statut?.ToString(CultureInfo.InvariantCulture) ?? "aucun";
        switch (reponse)
        {
            case ReponseDeLIA.Texte:
                DemandeReussie(Fournisseur, statut, millisecondes);
                break;
            case ReponseDeLIA.Echec { Sorte: SorteDEchec.Injoignable }:
                FournisseurInjoignable(Fournisseur, brute.TypeDErreur ?? "inconnu", millisecondes);
                break;
            case ReponseDeLIA.Echec echec:
                DemandeEchouee(Fournisseur, echec.Sorte, statut, millisecondes);
                break;
        }
    }

    [LoggerMessage(EventId = 91, Level = LogLevel.Information, Message = "Demande à l'IA : {Fournisseur} a répondu (statut {Statut}) en {Duree} ms.")]
    private partial void DemandeReussie(FournisseurDIA fournisseur, string statut, long duree);

    [LoggerMessage(EventId = 92, Level = LogLevel.Warning, Message = "Demande à l'IA : {Fournisseur}, échec {Sorte} (statut {Statut}) en {Duree} ms.")]
    private partial void DemandeEchouee(FournisseurDIA fournisseur, SorteDEchec sorte, string statut, long duree);

    [LoggerMessage(EventId = 93, Level = LogLevel.Warning, Message = "Demande à l'IA : {Fournisseur} n'a pas pu être joint ({TypeDErreur}) en {Duree} ms.")]
    private partial void FournisseurInjoignable(FournisseurDIA fournisseur, string typeDErreur, long duree);

    // Un échec, rendu par le port : Warning, comme les échecs d'une demande partie. L'entrée 95, sans clé prête, dit un état,
    // et reste en Information (#108, point 4).
    [LoggerMessage(EventId = 94, Level = LogLevel.Warning, Message = "Demande à l'IA refusée : une autre est en cours ({Fournisseur}).")]
    private partial void DemandeDejaEnCours(FournisseurDIA fournisseur);

    [LoggerMessage(EventId = 95, Level = LogLevel.Information, Message = "Demande à l'IA sans clé prête : {Fournisseur} ({Raison}).")]
    private partial void SansClePrete(FournisseurDIA fournisseur, RaisonDIndisponibilite raison);
}
