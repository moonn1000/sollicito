using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Sollicito.Application.Candidatures;

namespace Sollicito.Api.Candidatures;

/// <summary>
/// Les routes des candidatures (#11), derrière les contrôles de l'API comme toutes les autres. Aucune ne porte de
/// donnée dans son adresse : les textes saisis voyagent dans le corps, et l'écran de saisie filtre lui-même les
/// entreprises qu'il propose.
/// </summary>
internal static class RoutesDesCandidatures
{
    /// <summary>Le titre d'un refus de la saisie ; le détail suit, champ par champ.</summary>
    public const string TitreDuRefus = "La candidature n'est pas enregistrée : corrigez les champs signalés.";

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

    private static async Task<Ok<IReadOnlyList<EntrepriseConnue>>> ListerLesEntreprisesAsync(IDepotDesCandidatures depot, CancellationToken annulation) =>
        TypedResults.Ok(await depot.ListerLesEntreprisesAsync(annulation));
}
