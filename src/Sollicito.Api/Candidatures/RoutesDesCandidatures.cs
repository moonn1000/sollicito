using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Sollicito.Application.Candidatures;

namespace Sollicito.Api.Candidatures;

/// <summary>
/// Les routes des candidatures (#11, #12, #25), derrière les contrôles de l'API comme toutes les autres. Aucune ne porte
/// de donnée dans son adresse, seulement des identifiants : les textes saisis voyagent dans le corps, et l'écran de
/// saisie filtre lui-même les entreprises qu'il propose.
/// </summary>
internal static class RoutesDesCandidatures
{
    /// <summary>Le titre d'un refus de la saisie ; le détail suit, champ par champ.</summary>
    public const string TitreDuRefus = "La candidature n'est pas enregistrée : corrigez les champs signalés.";

    /// <summary>Le titre d'une candidature qui n'existe pas.</summary>
    public const string TitreDeLaCandidatureIntrouvable = "Cette candidature n'existe pas.";

    /// <summary>Le titre d'un changement de statut refusé ; son motif suit, en détail.</summary>
    public const string TitreDuStatutRefuse = "Le statut n'a pas changé.";

    /// <summary>Le titre d'une correction refusée ; son motif suit, en détail.</summary>
    public const string TitreDeLaCorrectionRefusee = "Le changement de statut n'est pas corrigé.";

    /// <summary>Le titre d'une étape refusée ; le détail suit, champ par champ.</summary>
    public const string TitreDeLEtapeRefusee = "L'étape n'est pas ajoutée à l'historique : corrigez les champs signalés.";

    /// <summary>Le titre d'une modification refusée ; le détail suit, champ par champ.</summary>
    public const string TitreDeLaModificationRefusee = "La candidature n'est pas modifiée : corrigez les champs signalés.";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/candidatures", ListerAsync)
            .WithName("ListerLesCandidatures")
            .WithSummary("Les candidatures, les plus récentes d'abord")
            .WithTags("Candidatures");

        routes.MapPost("/api/candidatures", EnregistrerAsync)
            .WithName("EnregistrerUneCandidature")
            .WithSummary("Enregistrer une candidature, au statut Envoyée")
            .WithDescription(
                "L'entreprise déjà enregistrée sous la même clé est reprise, sinon elle est créée. La date et l'heure "
                + "d'envoi s'écrivent sans décalage (2026-03-27T23:30), dans le fuseau du poste ; l'API les rend en UTC.")
            .WithTags("Candidatures");

        routes.MapGet("/api/candidatures/{id:int}", LireLaFicheAsync)
            .WithName("LireLaFicheDUneCandidature")
            .WithSummary("La fiche d'une candidature : son statut, les passages permis et son historique")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Candidatures");

        routes.MapPut("/api/candidatures/{id:int}", ModifierAsync)
            .WithName("ModifierUneCandidature")
            .WithSummary("Corriger l'entreprise, le poste et la date d'envoi d'une candidature, sans toucher à son statut")
            .WithDescription(
                "Les champs suivent les règles de l'enregistrement, et l'entreprise est reconnue de nouveau par la clé de "
                + "son nom. La date d'envoi ne suit pas le premier événement de l'historique ; renvoyée inchangée, à la "
                + "minute près, elle garde l'instant enregistré. Ni le statut ni l'historique ne changent.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Candidatures");

        routes.MapDelete("/api/candidatures/{id:int}", SupprimerAsync)
            .WithName("SupprimerUneCandidature")
            .WithSummary("Supprimer une candidature et tout son historique, définitivement ; son entreprise reste")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Candidatures");

        routes.MapPost("/api/candidatures/{id:int}/statut", ChangerLeStatutAsync)
            .WithName("ChangerLeStatutDUneCandidature")
            .WithSummary("Faire passer une candidature à un nouveau statut, si le diagramme d'états le permet")
            .WithDescription(
                "Le changement entre dans l'historique, daté par l'horloge. Un passage que le diagramme d'états ne "
                + "permet pas, vers le statut déjà en place compris, est refusé (409), avec son motif.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Candidatures");

        routes.MapPost("/api/candidatures/{id:int}/changements/{changement:int}/correction", CorrigerAsync)
            .WithName("CorrigerLeDernierChangementDeStatut")
            .WithSummary("Corriger le dernier changement de statut d'une candidature")
            .WithDescription(
                "Un nouveau changement, daté par l'horloge, ramène la candidature au statut d'avant ; le changement "
                + "corrigé reste dans l'historique. Refusé (409) si ce changement n'est pas le dernier à corriger.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Candidatures");

        routes.MapPost("/api/candidatures/{id:int}/etapes", NoterUneEtapeAsync)
            .WithName("NoterUneEtape")
            .WithSummary("Ajouter à l'historique un appel, un entretien ou un échange, sans changer le statut")
            .WithDescription(
                "La date et l'heure s'écrivent sans décalage (2026-11-20T10:00), dans le fuseau du poste : jamais avant "
                + "l'envoi, ni plus d'un an après l'instant présent. La note est facultative.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Candidatures");

        routes.MapGet("/api/entreprises", ListerLesEntreprisesAsync)
            .WithName("ListerLesEntreprises")
            .WithSummary("Les entreprises déjà enregistrées, que l'écran de saisie propose")
            .WithTags("Entreprises");
    }

    private static async Task<Ok<IReadOnlyList<CandidatureListee>>> ListerAsync(IDepotDesCandidatures depot, CancellationToken annulation) =>
        TypedResults.Ok(await depot.ListerAsync(annulation));

    private static async Task<Results<Created<CandidatureListee>, ValidationProblem>> EnregistrerAsync(
        SaisieDUneCandidature saisie,
        EnregistrementDesCandidatures enregistrement,
        CancellationToken annulation) =>
        await enregistrement.EnregistrerAsync(saisie, annulation) switch
        {
            ResultatDeLEnregistrement.Enregistree enregistree => TypedResults.Created(default(string), enregistree.Candidature),
            ResultatDeLEnregistrement.Refusee refusee => TypedResults.ValidationProblem(refusee.Refus, title: TitreDuRefus),
            _ => throw new UnreachableException(),
        };

    private static async Task<Results<Ok<FicheDUneCandidature>, ProblemHttpResult>> LireLaFicheAsync(
        int id,
        SuiviDesCandidatures suivi,
        CancellationToken annulation) =>
        await suivi.LireLaFicheAsync(id, annulation) is { } fiche ? TypedResults.Ok(fiche) : Introuvable();

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> ModifierAsync(
        int id,
        SaisieDUneCandidature saisie,
        ModificationDesCandidatures modification,
        CancellationToken annulation) =>
        Repondre(await modification.ModifierAsync(id, saisie, annulation), TitreDeLaModificationRefusee);

    private static async Task<Results<NoContent, ProblemHttpResult>> SupprimerAsync(
        int id,
        ModificationDesCandidatures modification,
        CancellationToken annulation) =>
        await modification.SupprimerAsync(id, annulation) ? TypedResults.NoContent() : Introuvable();

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> ChangerLeStatutAsync(
        int id,
        SaisieDUnStatut saisie,
        SuiviDesCandidatures suivi,
        CancellationToken annulation) =>
        Repondre(await suivi.ChangerLeStatutAsync(id, saisie, annulation), TitreDuStatutRefuse);

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> CorrigerAsync(
        int id,
        int changement,
        SuiviDesCandidatures suivi,
        CancellationToken annulation) =>
        Repondre(await suivi.CorrigerLeDernierChangementAsync(id, changement, annulation), TitreDeLaCorrectionRefusee);

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> NoterUneEtapeAsync(
        int id,
        SaisieDUneEtape saisie,
        SuiviDesCandidatures suivi,
        CancellationToken annulation) =>
        Repondre(await suivi.NoterUneEtapeAsync(id, saisie, annulation), TitreDeLEtapeRefusee);

    private static async Task<Ok<IReadOnlyList<EntrepriseConnue>>> ListerLesEntreprisesAsync(IDepotDesCandidatures depot, CancellationToken annulation) =>
        TypedResults.Ok(await depot.ListerLesEntreprisesAsync(annulation));

    // La fiche à jour (200), les refus d'une saisie (400), une candidature introuvable (404), une action que l'état de
    // la candidature ne permet pas (409, avec son motif en détail). Le titre est celui du refus, 400 ou 409.
    private static Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult> Repondre(ResultatDuSuivi resultat, string titreDuRefus) =>
        resultat switch
        {
            ResultatDuSuivi.Fait fait => TypedResults.Ok(fait.Fiche),
            ResultatDuSuivi.Refuse refuse => TypedResults.ValidationProblem(refuse.Refus, title: titreDuRefus),
            ResultatDuSuivi.Introuvable => Introuvable(),
            ResultatDuSuivi.NonPermis nonPermis => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: titreDuRefus,
                detail: nonPermis.Motif),
            _ => throw new UnreachableException(),
        };

    private static ProblemHttpResult Introuvable() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: TitreDeLaCandidatureIntrouvable);
}
