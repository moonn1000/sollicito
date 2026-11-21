using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Sollicito.Application.Contacts;

namespace Sollicito.Api.Contacts;

/// <summary>
/// Les routes des contacts d'une entreprise (#14, absorbe #30), derrière les contrôles de l'API comme toutes les autres.
/// Aucune ne porte de donnée dans son adresse, seulement des identifiants : le nom et l'adresse voyagent dans le corps.
/// </summary>
internal static class RoutesDesContacts
{
    /// <summary>Le titre d'un contact refusé à l'ajout ; le détail suit, champ par champ.</summary>
    public const string TitreDuRefus = "Le contact n'est pas enregistré : corrigez les champs signalés.";

    /// <summary>Le titre d'une modification refusée ; le détail suit, champ par champ.</summary>
    public const string TitreDeLaModificationRefusee = "Le contact n'est pas modifié : corrigez les champs signalés.";

    /// <summary>Le titre d'une entreprise qui n'existe pas.</summary>
    public const string TitreDeLEntrepriseIntrouvable = "Cette entreprise n'existe pas.";

    /// <summary>Le titre d'un contact qui n'existe pas.</summary>
    public const string TitreDuContactIntrouvable = "Ce contact n'existe pas.";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/entreprises/{id:int}/contacts", ListerAsync)
            .WithName("ListerLesContactsDUneEntreprise")
            .WithSummary("Les contacts d'une entreprise, dans l'ordre de leur ajout")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Contacts");

        routes.MapPost("/api/entreprises/{id:int}/contacts", AjouterAsync)
            .WithName("AjouterUnContact")
            .WithSummary("Ajouter un contact à une entreprise : un nom et une adresse électronique")
            .WithDescription("Une seule adresse, de la forme nom@domaine, sans espace ni accent, sans virgule, point-virgule, « ? » ni « % ».")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Contacts");

        routes.MapPut("/api/contacts/{id:int}", ModifierAsync)
            .WithName("ModifierUnContact")
            .WithSummary("Corriger le nom et l'adresse d'un contact ; ses relances l'affichent désormais ainsi")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Contacts");

        routes.MapDelete("/api/contacts/{id:int}", SupprimerAsync)
            .WithName("SupprimerUnContact")
            .WithSummary("Supprimer un contact, définitivement ; ses relances restent, sans destinataire")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Contacts");
    }

    private static async Task<Results<Ok<IReadOnlyList<ContactConnu>>, ProblemHttpResult>> ListerAsync(
        int id,
        ContactsDesEntreprises contacts,
        CancellationToken annulation) =>
        await contacts.ListerAsync(id, annulation) is { } liste
            ? TypedResults.Ok(liste)
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: TitreDeLEntrepriseIntrouvable);

    private static async Task<Results<Created<ContactConnu>, ValidationProblem, ProblemHttpResult>> AjouterAsync(
        int id,
        SaisieDUnContact saisie,
        ContactsDesEntreprises contacts,
        CancellationToken annulation) =>
        await contacts.AjouterAsync(id, saisie, annulation) switch
        {
            ResultatDUnContact.Fait fait => TypedResults.Created(default(string), fait.Contact),
            ResultatDUnContact.Refuse refuse => TypedResults.ValidationProblem(refuse.Refus, title: TitreDuRefus),
            ResultatDUnContact.Introuvable => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: TitreDeLEntrepriseIntrouvable),
            _ => throw new UnreachableException(),
        };

    private static async Task<Results<Ok<ContactConnu>, ValidationProblem, ProblemHttpResult>> ModifierAsync(
        int id,
        SaisieDUnContact saisie,
        ContactsDesEntreprises contacts,
        CancellationToken annulation) =>
        await contacts.ModifierAsync(id, saisie, annulation) switch
        {
            ResultatDUnContact.Fait fait => TypedResults.Ok(fait.Contact),
            ResultatDUnContact.Refuse refuse => TypedResults.ValidationProblem(refuse.Refus, title: TitreDeLaModificationRefusee),
            ResultatDUnContact.Introuvable => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: TitreDuContactIntrouvable),
            _ => throw new UnreachableException(),
        };

    private static async Task<Results<NoContent, ProblemHttpResult>> SupprimerAsync(
        int id,
        ContactsDesEntreprises contacts,
        CancellationToken annulation) =>
        await contacts.SupprimerAsync(id, annulation)
            ? TypedResults.NoContent()
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: TitreDuContactIntrouvable);
}
