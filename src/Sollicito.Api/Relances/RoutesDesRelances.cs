using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sollicito.Api.Candidatures;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.Candidatures;
using Sollicito.Application.Relances;
using Sollicito.Domain.Relances;

namespace Sollicito.Api.Relances;

/// <summary>
/// Les routes des relances (#14), derrière les contrôles de l'API comme toutes les autres : planifier, changer, marquer
/// faite ou abandonner la relance d'une candidature, lire celles que signale le tableau de bord, et le délai par défaut.
/// Aucune ne porte de donnée dans son adresse, seulement des identifiants. Aucune n'ouvre de brouillon : seule la fenêtre
/// hôte l'ouvre, à la demande de la page (ADR-0011).
/// </summary>
internal static class RoutesDesRelances
{
    /// <summary>Le titre d'une relance refusée par ses champs ; le détail suit, champ par champ.</summary>
    public const string TitreDuRefus = "La relance n'est pas planifiée : corrigez les champs signalés.";

    /// <summary>Le titre d'une relance que la candidature n'admet pas ; son motif suit, en détail.</summary>
    public const string TitreDeLaRelanceRefusee = "La relance n'est pas planifiée.";

    /// <summary>Le titre d'un changement refusé par ses champs ; le détail suit, champ par champ.</summary>
    public const string TitreDuChangementRefuse = "La relance n'est pas changée : corrigez les champs signalés.";

    /// <summary>Le titre d'un changement que la relance n'admet plus ; son motif suit, en détail.</summary>
    public const string TitreDeLaRelanceNonChangee = "La relance n'est pas changée.";

    /// <summary>Le titre d'une clôture refusée ; son motif suit, en détail.</summary>
    public const string TitreDeLaClotureRefusee = "La relance n'est pas close.";

    /// <summary>Le titre d'un délai par défaut refusé ; le détail suit.</summary>
    public const string TitreDuDelaiRefuse = "Le délai par défaut n'a pas changé : corrigez le champ signalé.";

    /// <summary>Le titre d'un délai par défaut qui ne s'écrit pas sur un fichier des réglages illisible.</summary>
    public const string TitreDesReglagesIllisibles =
        "Le délai par défaut n'a pas changé : les réglages n'ont pas pu être relus. Choisissez de nouveau le dossier des "
        + "sauvegardes, qui les réécrit, puis réglez le délai.";

    /// <summary>Le titre d'un délai par défaut que Sollicito n'a pas pu écrire.</summary>
    public const string TitreDuDelaiNonEcrit = "Le délai par défaut n'a pas changé : Sollicito n'a pas pu écrire ses réglages.";

    /// <summary>Le message d'un délai par défaut absent.</summary>
    public const string DelaiManquant = "Indiquez le délai par défaut, en jours.";

    /// <summary>Le titre d'une demande de premier jet envoyée avec un corps : elle n'en prend aucun (#47).</summary>
    public const string TitreDuPremierJetAvecUnCorps = "La demande d'un premier jet ne prend aucun corps.";

    /// <summary>Le titre d'un texte relu refusé par sa règle ; le motif suit, sous le champ (#47).</summary>
    public const string TitreDuBrouillonReluRefuse = "Le brouillon n'est pas prêt : corrigez le champ signalé.";

    /// <summary>Le titre d'un texte relu que la relance n'admet plus ; son motif suit, en détail (#47).</summary>
    public const string TitreDuBrouillonReluNonDepose = "Le brouillon n'est pas prêt.";

    /// <summary>Le corps le plus gros que lit le dépôt d'un texte relu : 2 000 caractères, même tous échappés, y tiennent.</summary>
    public const int BorneDuBrouillonRelu = 16 * 1024;

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/relances", ListerAsync)
            .WithName("ListerLesRelancesASignaler")
            .WithSummary("Les relances à faire que signale le tableau de bord : échues, et de la semaine")
            .WithDescription(
                "En date locale : échues, celles dont l'échéance tombe aujourd'hui ou avant ; de la semaine, celles qui "
                + "tombent dans les sept jours qui suivent. Chacune par échéance.")
            .WithTags("Relances");

        routes.MapPost("/api/candidatures/{id:int}/relances", PlanifierAsync)
            .WithName("PlanifierUneRelance")
            .WithSummary("Planifier une relance d'une candidature en cours, qui n'en a pas d'autre à faire")
            .WithDescription(
                "L'échéance est la date locale de l'envoi plus le délai, de 1 à 365 jours ; sans délai, le délai par "
                + "défaut. Le destinataire, facultatif, est un contact de l'entreprise de la candidature. Refusé (409) "
                + "sur une candidature close, ou qui a déjà une relance à faire.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Relances");

        routes.MapPut("/api/candidatures/{id:int}/relances/{relance:int}", ChangerAsync)
            .WithName("ChangerUneRelance")
            .WithSummary("Changer le délai et le destinataire de la relance à faire d'une candidature")
            .WithDescription(
                "Mêmes champs qu'à la planification, traités de même : sans délai, le délai par défaut ; sans destinataire, "
                + "personne. Refusé (409) si la relance n'est plus à faire.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Relances");

        routes.MapPost("/api/candidatures/{id:int}/relances/{relance:int}/faite", MarquerFaiteAsync)
            .WithName("MarquerUneRelanceFaite")
            .WithSummary("Marquer faite la relance à faire d'une candidature, pour de bon")
            .WithDescription("Sa clôture entre dans l'historique, datée par l'horloge. Refusé (409) si la relance n'est plus à faire.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Relances");

        routes.MapPost("/api/candidatures/{id:int}/relances/{relance:int}/abandon", AbandonnerAsync)
            .WithName("AbandonnerUneRelance")
            .WithSummary("Abandonner la relance à faire d'une candidature, pour de bon")
            .WithDescription("Sa clôture entre dans l'historique, datée par l'horloge. Refusé (409) si la relance n'est plus à faire.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Relances");

        // Le premier jet de l'IA (#47, ADR-0019) : marqué « sans la base » pour la porte des requêtes, il n'y entre que le
        // temps de lire la relance, jamais pendant l'appel au fournisseur.
        routes.MapPost("/api/candidatures/{id:int}/relances/{relance:int}/premier-jet", RedigerUnPremierJetAsync)
            .WithName("RedigerUnPremierJetDeRelance")
            .WithSummary("Faire rédiger par l'IA réglée le paragraphe central de la relance à faire d'une candidature")
            .WithDescription(
                "Sur un geste de l'utilisateur. Ne partent chez le fournisseur que le poste, l'entreprise et la date d'envoi, "
                + "avec les consignes de Sollicito ; jamais le contact. La réponse, nettoyée et bornée, revient entre le salut "
                + "et la formule de politesse du brouillon, à relire ; un échec revient avec son issue et son texte, jamais la "
                + "réponse du fournisseur. Refusé (400) avec un corps, (404) pour une candidature inconnue, (409) si la relance "
                + "n'est plus à faire, sans IA prête, ou si une demande à l'IA est déjà en cours. La porte de la base n'est "
                + "tenue que le temps de lire la relance.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Relances")
            .SansLaBase();

        routes.MapPut("/api/candidatures/{id:int}/relances/{relance:int}/brouillon-relu", DeposerLeBrouillonReluAsync)
            .WithName("DeposerLeBrouillonRelu")
            .WithSummary("Déposer le texte relu du brouillon de la relance à faire, que la fenêtre hôte ouvrira dans la messagerie")
            .WithDescription(
                "Le texte, requis et de 2 000 caractères au plus, suit la règle d'un texte sur plusieurs lignes ; le lien qu'il "
                + "bâtira ne dépasse pas 6 000 caractères. Il est gardé en mémoire, jamais en base, jusqu'à ce que la fenêtre "
                + "l'ouvre ; l'objet et le destinataire restent ceux de la relance. Refusé (400) par sa règle, (404) pour une "
                + "candidature inconnue, (409) si la relance n'est plus à faire. Corps de 16 Kio au plus.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuBrouillonRelu))
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("Relances");

        routes.MapGet("/api/reglages/relances", LireLeReglage)
            .WithName("LireLeDelaiDesRelances")
            .WithSummary("Le délai par défaut des relances, et ce qui ne se lisait pas dans les réglages")
            .WithTags("Relances")
            .SansLaBase();

        routes.MapPut("/api/reglages/relances", ChangerLeReglageAsync)
            .WithName("ChangerLeDelaiDesRelances")
            .WithSummary("Changer le délai par défaut des relances, de 1 à 365 jours")
            .WithDescription(
                "Il ne sert qu'aux relances planifiées ensuite. Refusé (409) si le fichier des réglages ne se lit pas : "
                + "seul le choix d'un dossier des sauvegardes le réécrit.")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Relances")
            .SansLaBase();
    }

    private static async Task<Ok<RelancesDuTableauDeBord>> ListerAsync(RelancesDesCandidatures relances, CancellationToken annulation) =>
        TypedResults.Ok(await relances.ListerAsync(annulation));

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> PlanifierAsync(
        int id,
        SaisieDUneRelance saisie,
        RelancesDesCandidatures relances,
        ReglageDesRelances reglage,
        CancellationToken annulation) =>
        RoutesDesCandidatures.Repondre(
            await relances.PlanifierAsync(id, saisie, reglage.Lire().DelaiParDefaut, annulation),
            TitreDuRefus,
            TitreDeLaRelanceRefusee);

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> ChangerAsync(
        int id,
        int relance,
        SaisieDUneRelance saisie,
        RelancesDesCandidatures relances,
        ReglageDesRelances reglage,
        CancellationToken annulation) =>
        RoutesDesCandidatures.Repondre(
            await relances.ChangerAsync(id, relance, saisie, reglage.Lire().DelaiParDefaut, annulation),
            TitreDuChangementRefuse,
            TitreDeLaRelanceNonChangee);

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> MarquerFaiteAsync(
        int id,
        int relance,
        RelancesDesCandidatures relances,
        CancellationToken annulation) =>
        RoutesDesCandidatures.Repondre(await relances.MarquerFaiteAsync(id, relance, annulation), TitreDeLaClotureRefusee);

    private static async Task<Results<Ok<FicheDUneCandidature>, ValidationProblem, ProblemHttpResult>> AbandonnerAsync(
        int id,
        int relance,
        RelancesDesCandidatures relances,
        CancellationToken annulation) =>
        RoutesDesCandidatures.Repondre(await relances.AbandonnerAsync(id, relance, annulation), TitreDeLaClotureRefusee);

    // La demande n'a pas de corps : un corps annoncé, ou envoyé par morceaux, est refusé sans être lu, comme pour l'essai de
    // la clé. Porte fermée, la réponse est celle de la porte des requêtes.
    private static async Task<Results<Ok<PremierJet>, ProblemHttpResult>> RedigerUnPremierJetAsync(
        int id,
        int relance,
        HttpRequest requete,
        PremiersJetsDesRelances premiersJets)
    {
        ArgumentNullException.ThrowIfNull(requete);
        if (requete.ContentLength > 0 || requete.Headers.TransferEncoding.Count > 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: TitreDuPremierJetAvecUnCorps);
        }

        switch (await premiersJets.RedigerAsync(id, relance, requete.HttpContext.RequestAborted))
        {
            case IssueDuPremierJet.Fait fait:
                return TypedResults.Ok(fait.Resultat);
            case IssueDuPremierJet.CandidatureIntrouvable:
                return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: RoutesDesCandidatures.TitreDeLaCandidatureIntrouvable);
            case IssueDuPremierJet.Refuse refuse:
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: PremiersJetsDesRelances.TitreDuRefus, detail: refuse.Motif);
            default:
                requete.HttpContext.Response.Headers.RetryAfter = "1";
                return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: PorteDeLaBase.TitreDuRefus);
        }
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> DeposerLeBrouillonReluAsync(
        int id,
        int relance,
        SaisieDuBrouillonRelu saisie,
        IDepotDesCandidatures depot,
        PremiersJetsDesRelances premiersJets,
        CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        return await premiersJets.DeposerAsync(depot, id, relance, saisie.Texte, annulation) switch
        {
            IssueDuDepot.Depose => TypedResults.NoContent(),
            IssueDuDepot.Refuse refuse => TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [SaisieDuBrouillonRelu.Champs.Texte] = [refuse.Motif] },
                title: TitreDuBrouillonReluRefuse),
            IssueDuDepot.NonPermis nonPermis => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: TitreDuBrouillonReluNonDepose,
                detail: nonPermis.Motif),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: RoutesDesCandidatures.TitreDeLaCandidatureIntrouvable),
        };
    }

    private static Ok<ReglageDuDelai> LireLeReglage(ReglageDesRelances reglage) => TypedResults.Ok(reglage.Lire());

    private static async Task<Results<Ok<ReglageDuDelai>, ValidationProblem, ProblemHttpResult>> ChangerLeReglageAsync(
        SaisieDuDelaiParDefaut saisie,
        ReglageDesRelances reglage)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (saisie.DelaiParDefaut is not { } jours || !DelaiDeRelance.EstAdmis(jours))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [SaisieDuDelaiParDefaut.Champs.DelaiParDefaut] = [saisie.DelaiParDefaut is null ? DelaiManquant : DelaiDeRelance.HorsBornes],
                },
                title: TitreDuDelaiRefuse);
        }

        return await reglage.ChangerAsync(jours) switch
        {
            IssueDuReglage.Change => TypedResults.Ok(reglage.Lire()),
            IssueDuReglage.FichierIllisible => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: TitreDesReglagesIllisibles),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: TitreDuDelaiNonEcrit),
        };
    }
}

/// <summary>Le délai par défaut tel que l'écran des réglages l'envoie.</summary>
/// <param name="DelaiParDefaut">Le délai en jours, de 1 à 365.</param>
public sealed record SaisieDuDelaiParDefaut(int? DelaiParDefaut)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string DelaiParDefaut = "delaiParDefaut";
    }
}
