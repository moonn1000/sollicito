using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.IA;
using Sollicito.Application.Offres;

namespace Sollicito.Api.Offres;

/// <summary>
/// La route de l'extraction d'une offre collée (#46, ADR-0018), derrière les contrôles de l'API comme toutes les autres.
/// Le texte voyage dans le corps, jamais dans l'adresse. Elle ne lit ni n'écrit la base, et ne retient pas la porte : une
/// restauration n'attend que 10 secondes les requêtes entrées, et une extraction en prend jusqu'à 60. Elle ne rend jamais
/// le texte de l'IA, ni celui de l'offre, ni le message du fournisseur : des champs proposés, ou une issue fermée et son
/// texte. Elle ne lit jamais l'adresse d'une annonce.
/// </summary>
internal static class RoutesDeLExtraction
{
    /// <summary>L'adresse de l'extraction.</summary>
    public const string Adresse = "/api/offres/extraction";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapPost(Adresse, ExtraireAsync)
            .WithName("ExtraireUneOffre")
            .WithSummary("Proposer les champs d'une offre collée : son texte part à l'IA réglée, comme une donnée à analyser")
            .WithDescription(
                "Sur un geste de l'utilisateur seulement. Le texte suit les règles du collage : vide, trop long ou porteur d'un "
                + "caractère refusé, il ne part pas (400). La réponse de l'IA, lue par les règles des champs du collage, propose "
                + "des champs, ou est écartée en entier (HorsSchema) ; rien ne s'enregistre. Refusé (409), sans requête, sans IA "
                + "prête ou si une demande à l'IA est déjà en cours. Corps de 1 Mio au plus.")
            .WithMetadata(new RequestSizeLimitAttribute(RoutesDesOffres.BorneDuCorps))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("Offres")
            .SansLaBase();
    }

    private static async Task<Results<Ok<ResultatDeLExtraction>, ValidationProblem, ProblemHttpResult>> ExtraireAsync(
        SaisieDeLExtraction saisie,
        ExtractionDesOffres extraction,
        HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        ArgumentNullException.ThrowIfNull(http);
        var (issue, fournisseur) = await extraction.ExtraireAsync(saisie.Texte, http.RequestAborted);
        return issue switch
        {
            IssueDeLExtraction.Proposee proposee => TypedResults.Ok(new ResultatDeLExtraction(IssueDUneExtraction.Proposee, proposee.Champs, null, null)),
            IssueDeLExtraction.HorsSchema => TypedResults.Ok(
                new ResultatDeLExtraction(IssueDUneExtraction.Echec, null, ResultatDeLExtraction.EchecHorsSchema, MessagesDeLExtraction.HorsSchema(fournisseur))),
            IssueDeLExtraction.Echec { Sorte: SorteDEchec.UneDemandeEnCours } => Conflit(MessagesDeLIA.UneDemandeEnCours),
            IssueDeLExtraction.Echec echec => TypedResults.Ok(
                new ResultatDeLExtraction(IssueDUneExtraction.Echec, null, echec.Sorte.ToString(), MessagesDeLExtraction.Echec(echec.Sorte, fournisseur))),
            IssueDeLExtraction.Indisponible indisponible => Conflit(MessagesDeLExtraction.Refus(indisponible.Raison)),
            IssueDeLExtraction.TexteRefuse refuse => TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [SaisieDeLExtraction.Champs.Texte] = [refuse.Motif] },
                title: MessagesDeLExtraction.TitreDuTexteRefuse),
            _ => throw new UnreachableException(),
        };
    }

    private static ProblemHttpResult Conflit(string titre) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: titre);
}

/// <summary>Le texte d'une offre tel que l'écran « Coller une offre » l'envoie, sans rien de lu ni de converti.</summary>
/// <param name="Texte">Le texte de l'offre, tel que collé.</param>
public sealed record SaisieDeLExtraction(string? Texte)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Texte = SaisieDUneOffre.Champs.Texte;
    }

    /// <summary>Un texte qui ne montre pas celui de l'offre.</summary>
    public override string ToString() => "Texte d'une offre à extraire";
}

/// <summary>
/// Ce que rend une extraction partie : les champs proposés, ou l'échec et le texte que l'écran montre ; jamais la réponse
/// de l'IA ni celle du fournisseur.
/// </summary>
/// <param name="Issue">Proposée, ou en échec.</param>
/// <param name="Champs">Les champs proposés, chacun une chaîne ou rien, pour une extraction proposée.</param>
/// <param name="Echec">
/// Comment elle a échoué, une valeur d'une liste fermée : <c>HorsSchema</c>, ou une sorte d'échec du port
/// (<c>CleRefusee</c>, <c>QuotaAtteint</c>, <c>AccesRefuse</c>, <c>ModeleIndisponible</c>, <c>FournisseurEnPanne</c>,
/// <c>Injoignable</c>, <c>DelaiDepasse</c>, <c>ReponseIllisible</c>, <c>ReponseTropLongue</c>, <c>RefusDuFournisseur</c>).
/// </param>
/// <param name="Message">Ce que l'écran dit d'un échec.</param>
public sealed record ResultatDeLExtraction(IssueDUneExtraction Issue, ChampsProposes? Champs, string? Echec, string? Message)
{
    /// <summary>L'échec d'une réponse qui ne suit pas le schéma.</summary>
    public const string EchecHorsSchema = "HorsSchema";

    /// <summary>Un texte qui ne montre aucune valeur proposée.</summary>
    public override string ToString() => $"Extraction : {Issue}";
}

/// <summary>L'issue d'une extraction partie.</summary>
public enum IssueDUneExtraction
{
    /// <summary>Des champs sont proposés, peut-être aucun.</summary>
    Proposee,

    /// <summary>La demande a échoué, ou sa réponse a été écartée.</summary>
    Echec,
}
