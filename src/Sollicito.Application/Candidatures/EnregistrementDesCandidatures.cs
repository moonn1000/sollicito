using System.Globalization;
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
    // Les formes que produit un champ datetime-local, à la minute ou à la seconde, sans décalage.
    private static readonly string[] FormatsDeLEnvoi = ["yyyy'-'MM'-'dd'T'HH':'mm", "yyyy'-'MM'-'dd'T'HH':'mm':'ss"];

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

    private bool EssayerDeLireLEnvoi(string? saisie, DateTime maintenant, out DateTime envoyeeLe, out string erreur)
    {
        envoyeeLe = default;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = EnvoiManquant;
            return false;
        }

        if (!DateTime.TryParseExact(saisie, FormatsDeLEnvoi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var heureSaisie))
        {
            erreur = EnvoiIllisible;
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
