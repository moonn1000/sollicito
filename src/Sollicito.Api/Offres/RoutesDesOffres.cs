using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sollicito.Application.Offres;

namespace Sollicito.Api.Offres;

/// <summary>
/// Les routes des offres collées (#36, ADR-0013), derrière les contrôles de l'API comme toutes les autres, et derrière la
/// porte de la base (#28). Aucune ne porte de donnée dans son adresse, seulement des identifiants : le texte et les
/// champs voyagent dans le corps. Aucune ne lit l'adresse de l'annonce : seul le navigateur par défaut l'ouvre, sur le
/// clic de l'utilisateur.
/// </summary>
internal static class RoutesDesOffres
{
    /// <summary>La borne du corps d'une offre collée : 1 Mio, assez pour 50 000 caractères même tous échappés en <c>\uXXXX</c>.</summary>
    public const long BorneDuCorps = 1024 * 1024;

    /// <summary>Le titre d'une offre refusée à l'enregistrement ; le détail suit, champ par champ.</summary>
    public const string TitreDuRefus = "L'offre n'est pas enregistrée : corrigez les champs signalés.";

    /// <summary>Le titre d'une correction refusée ; le détail suit, champ par champ.</summary>
    public const string TitreDeLaCorrectionRefusee = "L'offre n'est pas modifiée : corrigez les champs signalés.";

    /// <summary>Le titre d'une offre déjà enregistrée (règle 2).</summary>
    public const string TitreDejaEnregistree = "Cette offre est déjà enregistrée : rien n'a été ajouté.";

    /// <summary>Le titre d'un doublon possible (règle 3).</summary>
    public const string TitreDuDoublonPossible =
        "Doublon possible : une offre du même poste, dans la même entreprise, est déjà enregistrée. Gardez les deux, ou renoncez à la nouvelle.";

    /// <summary>Le titre d'une offre qui n'existe pas.</summary>
    public const string TitreDeLOffreIntrouvable = "Cette offre n'existe pas.";

    /// <summary>Le motif d'une offre déjà enregistrée, dans la réponse.</summary>
    public const string MotifDejaEnregistree = "dejaEnregistree";

    /// <summary>Le motif d'un doublon possible, dans la réponse.</summary>
    public const string MotifDoublonPossible = "doublonPossible";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/offres", CollerAsync)
            .WithName("CollerUneOffre")
            .WithSummary("Coller une offre : son texte, gardé tel quel, et ses champs, remplis à la main")
            .WithDescription(
                "Le même texte recollé est reconnu à son empreinte : rien ne s'enregistre ni ne s'écrase (409, motif dejaEnregistree). "
                + "Une offre de même entreprise et de même poste est un doublon possible : refusée (409, motif doublonPossible) sans l'accord garderLesDeux. "
                + "Corps de 1 Mio au plus.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("Offres");

        routes.MapGet("/api/offres/{id:int}", LireAsync)
            .WithName("LireUneOffre")
            .WithSummary("La fiche d'une offre : ses champs, son texte et ses doublons possibles")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Offres");

        routes.MapPut("/api/offres/{id:int}", CorrigerAsync)
            .WithName("CorrigerUneOffre")
            .WithSummary("Corriger les champs d'une offre, jamais son texte")
            .WithDescription(
                "Une entreprise ou un poste changés refont la comparaison des doublons possibles : refusée (409, motif doublonPossible) sans l'accord garderLesDeux. "
                + "Aucune entreprise n'est renommée.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Offres");
    }

    private static async Task<Results<Created<FicheDUneOffre>, ValidationProblem, ProblemHttpResult>> CollerAsync(
        SaisieDUneOffre saisie,
        ConnecteurDuTexteColle connecteur,
        IImportDesOffres import,
        CancellationToken annulation) =>
        await import.ImporterAsync(connecteur.Recevoir(saisie).Single(), saisie.GarderLesDeux, annulation) switch
        {
            ResultatDeLImport.Enregistree enregistree => TypedResults.Created(default(string), enregistree.Fiche),
            ResultatDeLImport.Refusee refusee => TypedResults.ValidationProblem(refusee.Refus, title: TitreDuRefus),
            // L'offre trouvée, lue en base : son identifiant et sa date d'ajout ; jamais le texte saisi.
            ResultatDeLImport.DejaEnregistree deja => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: TitreDejaEnregistree,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["motif"] = MotifDejaEnregistree,
                    ["offre"] = deja.Offre,
                }),
            ResultatDeLImport.DoublonPossible doublon => DoublonPossible(doublon.Doublons),
            _ => throw new UnreachableException(),
        };

    private static async Task<Results<Ok<FicheDUneOffre>, ProblemHttpResult>> LireAsync(
        int id,
        OffresEnregistrees offres,
        CancellationToken annulation) =>
        await offres.LireAsync(id, annulation) is { } fiche ? TypedResults.Ok(fiche) : Introuvable();

    private static async Task<Results<Ok<FicheDUneOffre>, ValidationProblem, ProblemHttpResult>> CorrigerAsync(
        int id,
        CorrectionDUneOffre correction,
        OffresEnregistrees offres,
        CancellationToken annulation) =>
        await offres.CorrigerAsync(id, correction, annulation) switch
        {
            ResultatDeLaCorrection.Corrigee corrigee => TypedResults.Ok(corrigee.Fiche),
            ResultatDeLaCorrection.Refusee refusee => TypedResults.ValidationProblem(refusee.Refus, title: TitreDeLaCorrectionRefusee),
            ResultatDeLaCorrection.DoublonPossible doublon => DoublonPossible(doublon.Doublons),
            ResultatDeLaCorrection.Introuvable => Introuvable(),
            _ => throw new UnreachableException(),
        };

    // Les offres trouvées, lues en base : de quoi les reconnaître et les ouvrir ; jamais le texte saisi.
    private static ProblemHttpResult DoublonPossible(IReadOnlyList<OffreSignalee> doublons) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: TitreDuDoublonPossible,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["motif"] = MotifDoublonPossible,
                ["doublons"] = doublons,
            });

    private static ProblemHttpResult Introuvable() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: TitreDeLOffreIntrouvable);
}
