using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.IA;

namespace Sollicito.Api.IA;

/// <summary>
/// Les routes du réglage de l'IA (#43, ADR-0016), derrière les contrôles de l'API comme toutes les autres. Aucune ne porte
/// de donnée dans son adresse, et aucune réponse ne porte la clé : la page l'écrit une fois, et ne la relit jamais. Elles
/// ne touchent pas la base : une restauration ne change ni le choix ni la clé, et elles restent ouvertes pendant qu'elle
/// se fait, comme le délai des relances.
/// <list type="bullet">
/// <item><c>GET /api/reglages/ia</c> : l'état, sans la clé.</item>
/// <item><c>PUT /api/reglages/ia</c> : le fournisseur choisi ; un autre efface la clé.</item>
/// <item><c>PUT /api/reglages/ia/cle</c> : la clé du fournisseur réglé, à la place de toute autre.</item>
/// <item><c>DELETE /api/reglages/ia/cle</c> : la clé retirée.</item>
/// <item><c>POST /api/reglages/ia/essai</c> : l'essai de la clé (#48), une demande fixe au fournisseur réglé, sans aucune
/// donnée de l'utilisateur ; la réponse du fournisseur n'est jamais rendue.</item>
/// </list>
/// Ses titres sont publics : les tests et l'écran les comparent mot pour mot.
/// </summary>
public static class RoutesDeLIA
{
    /// <summary>L'adresse de l'essai de la clé.</summary>
    public const string AdresseDeLEssai = "/api/reglages/ia/essai";

    /// <summary>Le titre d'un essai envoyé avec un corps : il n'en prend aucun.</summary>
    public const string TitreDeLEssaiAvecUnCorps = "L'essai de la clé ne prend aucun corps.";

    /// <summary>Le corps le plus gros qu'une écriture de l'IA lit : une clé de 512 caractères y tient largement.</summary>
    public const int BorneDuCorps = 4 * 1024;

    /// <summary>Le titre d'un choix refusé par son champ.</summary>
    public const string TitreDuChoixRefuse = "Le choix de l'IA n'a pas changé : corrigez le champ signalé.";

    /// <summary>Le message d'un fournisseur inconnu.</summary>
    public const string FournisseurInconnu = "Choisissez Aucun, Claude, OpenAI ou Ollama.";

    /// <summary>Le titre d'un choix qui ne s'écrit pas sur un fichier des réglages illisible.</summary>
    public const string TitreDuChoixSurDesReglagesIllisibles =
        "Le choix de l'IA n'a pas changé : les réglages n'ont pas pu être relus. Choisissez de nouveau le dossier des "
        + "sauvegardes, qui les réécrit, puis l'IA.";

    /// <summary>Le titre d'un changement de fournisseur dont la clé ne s'efface pas.</summary>
    public const string TitreDeLaCleNonEffacee = "Le fournisseur n'a pas changé : la clé enregistrée n'a pas pu être effacée. Réessayez.";

    /// <summary>Le titre d'un choix que Sollicito n'a pas pu écrire.</summary>
    public const string TitreDuChoixNonEcrit = "Le fournisseur n'a pas changé : Sollicito n'a pas pu écrire ses réglages.";

    /// <summary>Le titre d'une clé refusée par sa forme ; le motif suit, sous le champ.</summary>
    public const string TitreDeLaCleRefusee = "La clé n'est pas enregistrée : corrigez le champ signalé.";

    /// <summary>Le titre d'une clé saisie quand le fournisseur réglé n'en prend pas.</summary>
    public const string TitreDeLaCleSansFournisseur = "La clé n'est pas enregistrée : choisissez d'abord Claude ou OpenAI.";

    /// <summary>Le titre d'une clé saisie sur un fichier des réglages illisible.</summary>
    public const string TitreDeLaCleSurDesReglagesIllisibles =
        "La clé n'est pas enregistrée : les réglages n'ont pas pu être relus. Choisissez de nouveau le dossier des "
        + "sauvegardes, qui les réécrit, puis l'IA.";

    /// <summary>Le titre d'une clé que Sollicito n'a pas pu chiffrer ni écrire.</summary>
    public const string TitreDeLaCleNonChiffree =
        "La clé n'est pas enregistrée : Sollicito n'a pas pu la chiffrer sur ce poste. Réessayez ; si l'erreur revient, "
        + "fermez Sollicito, puis relancez-le.";

    /// <summary>Le titre d'une clé qui ne se retire pas.</summary>
    public const string TitreDeLaCleNonRetiree = "La clé n'a pas été retirée : Sollicito n'a pas pu effacer son fichier. Réessayez.";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/reglages/ia", Lire)
            .WithName("LireLEtatDeLIA")
            .WithSummary("L'état de l'IA : le fournisseur choisi, s'il l'a été, ce qui ne se lisait pas, et si une clé est enregistrée")
            .WithDescription("La clé elle-même n'est jamais rendue : seulement sans objet, aucune, enregistrée ou illisible.")
            .WithTags("IA")
            .SansLaBase();

        routes.MapPut("/api/reglages/ia", ChoisirAsync)
            .WithName("ChoisirLeFournisseurDIA")
            .WithSummary("Choisir le fournisseur d'IA : Aucun, Claude, OpenAI ou Ollama")
            .WithDescription(
                "Un autre fournisseur que celui du réglage efface d'abord la clé enregistrée (une clé, une adresse) ; le même ne "
                + "change rien. Refusé (409) si le fichier des réglages ne se lit pas, ou si la clé ne s'efface pas. Corps de "
                + "4 Kio au plus.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("IA")
            .SansLaBase();

        routes.MapPut("/api/reglages/ia/cle", EnregistrerLaCleAsync)
            .WithName("EnregistrerLaCleDIA")
            .WithSummary("Enregistrer la clé d'API du fournisseur réglé, Claude ou OpenAI, à la place de toute autre")
            .WithDescription(
                "La clé est chiffrée par Data Protection, trousseau protégé par DPAPI en portée utilisateur, hors de la base. "
                + "Elle n'est jamais rendue. Refusée (400) si elle n'a pas sa forme, (409) si le fournisseur réglé n'en prend "
                + "pas ou si elle ne se chiffre pas. Corps de 4 Kio au plus.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("IA")
            .SansLaBase();

        routes.MapDelete("/api/reglages/ia/cle", RetirerLaCleAsync)
            .WithName("RetirerLaCleDIA")
            .WithSummary("Retirer la clé d'API enregistrée")
            .WithDescription("Sans clé enregistrée, la réponse est la même. Refusé (409) si son fichier ne s'efface pas.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("IA")
            .SansLaBase();

        routes.MapPost(AdresseDeLEssai, EssayerLaCleAsync)
            .WithName("EssayerLaCleDIA")
            .WithSummary("Tester la clé : une courte question fixe au fournisseur réglé, sans aucune donnée de l'utilisateur")
            .WithDescription(
                "Un seul appel, à l'adresse officielle du fournisseur, sur un geste de l'utilisateur ; ni la clé ni la réponse du "
                + "fournisseur ne sont rendues, seulement l'issue et son texte. Refusé (400) avec un corps, (409) sans clé prête "
                + "ou si une demande à l'IA est déjà en cours.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("IA")
            .SansLaBase();
    }

    private static Ok<EtatDeLIA> Lire(ReglageDeLIA reglage) => TypedResults.Ok(reglage.LireLEtat());

    private static async Task<Results<Ok<EtatDeLIA>, ValidationProblem, ProblemHttpResult>> ChoisirAsync(
        SaisieDuFournisseurDIA saisie,
        ReglageDeLIA reglage)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!FournisseursDIA.EssayerDeLire(saisie.Fournisseur, out var fournisseur))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [SaisieDuFournisseurDIA.Champs.Fournisseur] = [FournisseurInconnu],
                },
                title: TitreDuChoixRefuse);
        }

        return await reglage.ChoisirAsync(fournisseur) switch
        {
            IssueDuChoixDeLIA.Change or IssueDuChoixDeLIA.Inchange => TypedResults.Ok(reglage.LireLEtat()),
            IssueDuChoixDeLIA.FichierIllisible => Conflit(TitreDuChoixSurDesReglagesIllisibles),
            IssueDuChoixDeLIA.CleNonEffacee => Conflit(TitreDeLaCleNonEffacee),
            _ => Conflit(TitreDuChoixNonEcrit),
        };
    }

    private static async Task<Results<Ok<EtatDeLIA>, ValidationProblem, ProblemHttpResult>> EnregistrerLaCleAsync(
        SaisieDeLaCleDIA saisie,
        ReglageDeLIA reglage)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var issue = await reglage.EnregistrerLaCleAsync(saisie.Cle);
        return issue.Sorte switch
        {
            SorteDIssueDeLaCle.Enregistree => TypedResults.Ok(reglage.LireLEtat()),
            SorteDIssueDeLaCle.Refusee => TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [SaisieDeLaCleDIA.Champs.Cle] = [issue.Motif!.Value.Message()],
                },
                title: TitreDeLaCleRefusee),
            SorteDIssueDeLaCle.SansCle => Conflit(TitreDeLaCleSansFournisseur),
            SorteDIssueDeLaCle.FichierIllisible => Conflit(TitreDeLaCleSurDesReglagesIllisibles),
            _ => Conflit(TitreDeLaCleNonChiffree),
        };
    }

    private static async Task<Results<Ok<EtatDeLIA>, ProblemHttpResult>> RetirerLaCleAsync(ReglageDeLIA reglage) =>
        await reglage.RetirerLaCleAsync() ? TypedResults.Ok(reglage.LireLEtat()) : Conflit(TitreDeLaCleNonRetiree);

    // L'essai n'a pas de corps : un corps annoncé, ou envoyé par morceaux, est refusé sans être lu.
    private static async Task<Results<Ok<ResultatDeLEssai>, ProblemHttpResult>> EssayerLaCleAsync(HttpRequest requete, ReglageDeLIA reglage)
    {
        ArgumentNullException.ThrowIfNull(requete);
        if (requete.ContentLength > 0 || requete.Headers.TransferEncoding.Count > 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: TitreDeLEssaiAvecUnCorps);
        }

        var issue = await reglage.TesterLaCleAsync(requete.HttpContext.RequestAborted);
        return issue.Resultat is { } resultat ? TypedResults.Ok(resultat) : Conflit(issue.MotifDuRefus!);
    }

    private static ProblemHttpResult Conflit(string titre) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: titre);
}

/// <summary>Le fournisseur tel que la section « IA » l'envoie, par son nom exact.</summary>
/// <param name="Fournisseur"><c>Aucun</c>, <c>Claude</c>, <c>OpenAI</c> ou <c>Ollama</c>.</param>
public sealed record SaisieDuFournisseurDIA(string? Fournisseur)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Fournisseur = "fournisseur";
    }
}

/// <summary>La clé telle que la section « IA » l'envoie, une seule fois. Son texte ne la montre pas.</summary>
/// <param name="Cle">La clé d'API du fournisseur réglé.</param>
public sealed record SaisieDeLaCleDIA(string? Cle)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Cle = "cle";
    }

    /// <summary>Un texte qui ne montre pas la clé.</summary>
    public override string ToString() => "Saisie d'une clé d'IA";
}
