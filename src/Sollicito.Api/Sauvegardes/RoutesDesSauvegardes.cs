using Microsoft.AspNetCore.Http.HttpResults;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// Les routes des sauvegardes (#26), derrière les contrôles de l'API comme toutes les autres. Aucune ne reçoit un chemin :
/// seul le dialogue de la fenêtre hôte choisit un dossier (ADR-0008). L'état se lit en mémoire, sans toucher au dossier.
/// </summary>
internal static class RoutesDesSauvegardes
{
    /// <summary>Le titre d'un retour au dossier par défaut qui n'a pas pu se faire.</summary>
    public const string TitreDuDossierInchange = "Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie.";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/sauvegardes", Lire)
            .WithName("LireLesSauvegardes")
            .WithSummary("L'état des sauvegardes : le dossier, la dernière copie, les copies gardées, et ce qui empêche une copie")
            .WithTags("Sauvegardes")
            .SansLaBase();

        routes.MapPost("/api/sauvegardes", SauvegarderMaintenantAsync)
            .WithName("SauvegarderMaintenant")
            .WithSummary("Refaire aussitôt la copie du jour dans le dossier des sauvegardes")
            .WithDescription("L'état rendu dit si la copie a réussi ; une copie manquée n'efface aucune copie.")
            .WithTags("Sauvegardes")
            // Hors de la porte de la base : la copie attend déjà la fin d'une restauration, qui tient le sémaphore des
            // copies ; entrée dans la porte, elle ferait attendre la restauration, qui l'attendrait (#28).
            .SansLaBase();

        routes.MapPost("/api/sauvegardes/dossier-par-defaut", RevenirAuDossierParDefautAsync)
            .WithName("RevenirAuDossierParDefaut")
            .WithSummary("Revenir au dossier des sauvegardes par défaut, sur le disque de la base")
            .WithDescription(
                "Une copie s'y fait d'abord : réussie, il redevient le dossier des sauvegardes ; manquée, refusé (409), et "
                + "l'ancien dossier reste. Les copies de l'ancien dossier y restent.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Sauvegardes")
            .SansLaBase();
    }

    private static Ok<EtatDesSauvegardes> Lire(SauvegardesDeLaBase sauvegardes) => TypedResults.Ok(sauvegardes.Etat);

    private static async Task<Ok<EtatDesSauvegardes>> SauvegarderMaintenantAsync(SauvegardesDeLaBase sauvegardes, CancellationToken annulation) =>
        TypedResults.Ok(await sauvegardes.RefaireLaCopieDuJourAsync(annulation));

    private static async Task<Results<Ok<EtatDesSauvegardes>, ProblemHttpResult>> RevenirAuDossierParDefautAsync(
        SauvegardesDeLaBase sauvegardes,
        CancellationToken annulation) =>
        await sauvegardes.RevenirAuDossierParDefautAsync(annulation) == IssueDuChoix.Choisi
            ? TypedResults.Ok(sauvegardes.Etat)
            : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: TitreDuDossierInchange);
}
