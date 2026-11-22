using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Entreprises;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// Enregistrer une candidature (#11) : chaque champ est lu et validé, puis l'entreprise est reprise si sa clé est déjà
/// enregistrée, ou créée sinon, et la candidature part au statut Envoyée. L'heure d'envoi saisie se lit dans le fuseau
/// du poste, que donne l'horloge.
/// </summary>
public sealed class EnregistrementDesCandidatures(IDepotDesCandidatures depot, TimeProvider horloge)
{
    /// <summary>Le message d'une date et d'une heure d'envoi absentes.</summary>
    public const string EnvoiManquant = "Indiquez la date et l'heure d'envoi.";

    /// <summary>Le message d'une date et d'une heure d'envoi illisibles.</summary>
    public const string EnvoiIllisible = "La date et l'heure d'envoi s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-03-27T23:30.";

    /// <summary>Enregistre la candidature, ou rend les refus de ses champs, sans rien enregistrer.</summary>
    public async Task<ResultatDeLEnregistrement> EnregistrerAsync(SaisieDUneCandidature saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);

        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Entreprise.EssayerDeLireLeNom(saisie.Entreprise, out var nom, out var erreur))
        {
            refus[SaisieDUneCandidature.Champs.Entreprise] = [erreur];
        }

        if (!Candidature.EssayerDeLireLePoste(saisie.Poste, out var poste, out erreur))
        {
            refus[SaisieDUneCandidature.Champs.Poste] = [erreur];
        }

        var maintenant = horloge.GetUtcNow().UtcDateTime;
        if (!EssayerDeLireLEnvoi(saisie.EnvoyeeLe, maintenant, out var envoyeeLe, out erreur))
        {
            refus[SaisieDUneCandidature.Champs.EnvoyeeLe] = [erreur];
        }

        if (nom is null || poste is null || refus.Count > 0)
        {
            return new ResultatDeLEnregistrement.Refusee(refus);
        }

        // L'écriture est exclusive : deux enregistrements simultanés d'une même entreprise n'en créent pas deux.
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var entreprise = await ecriture.TrouverLEntrepriseAsync(CleDEntreprise.De(nom), annulation);
        if (entreprise is null)
        {
            entreprise = Entreprise.Nouvelle(nom);
            await ecriture.AjouterAsync(entreprise, annulation);
        }

        var candidature = Candidature.Enregistrer(entreprise.Id, poste, envoyeeLe, maintenant);
        await ecriture.AjouterAsync(candidature, annulation);
        await ecriture.ValiderAsync(annulation);

        return new ResultatDeLEnregistrement.Enregistree(new CandidatureListee(
            candidature.Id,
            new EntrepriseConnue(entreprise.Id, entreprise.Nom),
            candidature.Poste,
            candidature.EnvoyeeLe,
            candidature.Statut));
    }

    /// <summary>
    /// Postule à une offre (#38, ADR-0014) : la candidature reprend l'entreprise et le poste de l'offre, lus en base dans
    /// l'écriture qui la crée, et part d'Envoyée à la date d'envoi saisie, aux règles de l'enregistrement (#11). Elle
    /// désigne l'offre, qui n'en reçoit jamais une seconde. Rien n'est enregistré si l'offre n'existe pas, si elle a déjà sa
    /// candidature, si elle n'a pas d'entreprise, si son poste ne se reprend pas, ou si la date est refusée ; aucune
    /// entreprise n'est jamais créée.
    /// </summary>
    public async Task<ResultatDeLaPostulation> PostulerAsync(int offreId, SaisieDUnePostulation saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);

        // La date se lit avant l'écriture, sans conclure : l'état de l'offre passe avant elle, puisqu'une date corrigée
        // n'aiderait pas une offre déjà postulée.
        var maintenant = horloge.GetUtcNow().UtcDateTime;
        var dateAdmise = EssayerDeLireLEnvoi(saisie.EnvoyeeLe, maintenant, out var envoyeeLe, out var refusDeLaDate);

        // L'écriture est exclusive : deux « Postuler » simultanés sur une même offre passent l'un après l'autre, et le
        // second trouve la candidature du premier. L'index unique de la base refuserait de toute façon la seconde.
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var offre = await ecriture.TrouverLOffreAPostulerAsync(offreId, annulation);
        if (offre is null)
        {
            return new ResultatDeLaPostulation.Introuvable();
        }

        if (offre.CandidatureId is { } candidatureId)
        {
            return new ResultatDeLaPostulation.DejaPostulee(candidatureId);
        }

        if (offre.Entreprise is null)
        {
            return new ResultatDeLaPostulation.EntrepriseManquante();
        }

        // Le poste d'une offre admise est toujours un poste de candidature admis ; seule une base restaurée, retouchée
        // hors de Sollicito, pourrait y déroger : la route le dit au lieu d'échouer.
        if (!Candidature.EssayerDeLireLePoste(offre.Poste, out var poste, out _))
        {
            return new ResultatDeLaPostulation.PosteARevoir();
        }

        if (!dateAdmise)
        {
            return new ResultatDeLaPostulation.Refusee(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [SaisieDUneCandidature.Champs.EnvoyeeLe] = [refusDeLaDate],
            });
        }

        var candidature = Candidature.Postuler(offre.Id, offre.Entreprise.Id, poste, envoyeeLe, maintenant);
        await ecriture.AjouterAsync(candidature, annulation);
        await ecriture.ValiderAsync(annulation);

        return new ResultatDeLaPostulation.Enregistree(new CandidatureListee(
            candidature.Id,
            offre.Entreprise,
            candidature.Poste,
            candidature.EnvoyeeLe,
            candidature.Statut));
    }

    /// <summary>
    /// Lit la forme d'une date et d'une heure d'envoi saisies, requises : <c>AAAA-MM-JJTHH:MM</c>, les secondes en
    /// option. Rend l'heure saisie, sans fuseau, ou le motif du refus. La modification d'une candidature (#25) la lit de
    /// même.
    /// </summary>
    internal static bool EssayerDeLireLaFormeDeLEnvoi(string? saisie, out DateTime heureSaisie, [NotNullWhen(false)] out string? erreur)
    {
        heureSaisie = default;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = EnvoiManquant;
            return false;
        }

        if (!HeureSaisie.EssayerDeLire(saisie, out heureSaisie))
        {
            erreur = EnvoiIllisible;
            return false;
        }

        erreur = null;
        return true;
    }

    private bool EssayerDeLireLEnvoi(string? saisie, DateTime maintenant, out DateTime envoyeeLe, out string erreur)
    {
        envoyeeLe = default;
        if (!EssayerDeLireLaFormeDeLEnvoi(saisie, out var heureSaisie, out var illisible))
        {
            erreur = illisible;
            return false;
        }

        if (!DateDEnvoi.EssayerDeLire(heureSaisie, horloge.LocalTimeZone, maintenant, out envoyeeLe, out var refus))
        {
            erreur = refus;
            return false;
        }

        erreur = "";
        return true;
    }
}

/// <summary>Ce que rend l'enregistrement d'une candidature.</summary>
public abstract record ResultatDeLEnregistrement
{
    private ResultatDeLEnregistrement()
    {
    }

    /// <summary>La candidature est enregistrée.</summary>
    public sealed record Enregistree(CandidatureListee Candidature) : ResultatDeLEnregistrement;

    /// <summary>La saisie est refusée : pour chaque champ refusé, ses messages. Rien n'est enregistré.</summary>
    public sealed record Refusee(IReadOnlyDictionary<string, string[]> Refus) : ResultatDeLEnregistrement;
}

/// <summary>
/// « Postuler » tel que l'interface l'envoie (#38) : la seule date et heure d'envoi, telles que tapées, sans décalage.
/// L'entreprise et le poste viennent de l'offre, lus en base : un corps qui en porte est illisible, comme un statut.
/// </summary>
/// <param name="EnvoyeeLe">La date et l'heure d'envoi, sans décalage : <c>2026-03-27T23:30</c>, les secondes en option.</param>
public sealed record SaisieDUnePostulation(string? EnvoyeeLe);

/// <summary>Ce que rend « Postuler » (#38).</summary>
public abstract record ResultatDeLaPostulation
{
    private ResultatDeLaPostulation()
    {
    }

    /// <summary>La candidature est enregistrée, et désigne l'offre.</summary>
    public sealed record Enregistree(CandidatureListee Candidature) : ResultatDeLaPostulation;

    /// <summary>La date d'envoi est refusée : ses messages. Rien n'est enregistré.</summary>
    public sealed record Refusee(IReadOnlyDictionary<string, string[]> Refus) : ResultatDeLaPostulation;

    /// <summary>L'offre n'existe pas.</summary>
    public sealed record Introuvable : ResultatDeLaPostulation;

    /// <summary>L'offre a déjà sa candidature, de cet identifiant, lu en base : rien n'est enregistré.</summary>
    public sealed record DejaPostulee(int CandidatureId) : ResultatDeLaPostulation;

    /// <summary>L'offre n'a pas d'entreprise : elle s'indique d'abord par sa correction (#36).</summary>
    public sealed record EntrepriseManquante : ResultatDeLaPostulation;

    /// <summary>Le poste de l'offre ne se reprend pas tel quel : l'offre se corrige d'abord.</summary>
    public sealed record PosteARevoir : ResultatDeLaPostulation;
}
