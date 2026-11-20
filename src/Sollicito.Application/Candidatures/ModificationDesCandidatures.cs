using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Entreprises;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// Modifier ou supprimer une candidature (#25).
/// <list type="bullet">
/// <item>La modification corrige son entreprise, son poste et sa date d'envoi, avec les règles de l'enregistrement
/// (#11) : l'entreprise est reconnue de nouveau par la clé de son nom, ou créée, et la date d'envoi ne suit pas le
/// premier événement de l'historique. Ni le statut ni l'historique ne changent.</item>
/// <item>La suppression emporte la candidature et tout son historique, définitivement ; son entreprise reste.</item>
/// </list>
/// Chaque action lit la candidature et l'écrit dans une même écriture exclusive, comme celles de la fiche (#12) : une
/// étape notée pendant une modification de la date d'envoi ne passe jamais avant l'envoi.
/// </summary>
public sealed class ModificationDesCandidatures(IDepotDesCandidatures depot, TimeProvider horloge)
{
    /// <summary>
    /// Modifie la candidature, ou rend les refus de ses champs, sans rien changer : ni la candidature, ni les
    /// entreprises. Une date d'envoi renvoyée inchangée, à la minute près, garde l'instant enregistré
    /// (<see cref="DateDEnvoi.EstLEnvoiEnregistre"/>).
    /// </summary>
    public async Task<ResultatDuSuivi> ModifierAsync(int id, SaisieDUneCandidature saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);

        // Le nom, le poste et la forme de la date se lisent sans la candidature ; la date, elle, se compare à son envoi
        // enregistré et à son historique.
        var nomAdmis = Entreprise.EssayerDeLireLeNom(saisie.Entreprise, out var nom, out var refusDuNom);
        var posteAdmis = Candidature.EssayerDeLireLePoste(saisie.Poste, out var poste, out var refusDuPoste);
        var formeAdmise = EnregistrementDesCandidatures.EssayerDeLireLaFormeDeLEnvoi(saisie.EnvoyeeLe, out var heureSaisie, out var refusDeLEnvoi);

        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var suivie = await ecriture.TrouverLaCandidatureAsync(id, annulation);
        if (suivie is null)
        {
            return new ResultatDuSuivi.Introuvable();
        }

        var candidature = suivie.Candidature;
        var maintenant = horloge.GetUtcNow().UtcDateTime;
        DateTime? envoiCorrige = null;
        if (formeAdmise && !DateDEnvoi.EstLEnvoiEnregistre(heureSaisie, horloge.LocalTimeZone, candidature.EnvoyeeLe))
        {
            if (!DateDEnvoi.EssayerDeLire(heureSaisie, horloge.LocalTimeZone, maintenant, out var instant, out var refusDeLaDate))
            {
                refusDeLEnvoi = refusDeLaDate;
            }
            else if (!candidature.PeutEtreEnvoyeeLe(instant, maintenant, out var refusDeLHistorique))
            {
                refusDeLEnvoi = refusDeLHistorique;
            }
            else
            {
                envoiCorrige = instant;
            }
        }

        // Les refus suivent l'ordre des champs du formulaire : entreprise, poste, date d'envoi.
        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!nomAdmis)
        {
            refus[SaisieDUneCandidature.Champs.Entreprise] = [refusDuNom!];
        }

        if (!posteAdmis)
        {
            refus[SaisieDUneCandidature.Champs.Poste] = [refusDuPoste!];
        }

        if (refusDeLEnvoi is not null)
        {
            refus[SaisieDUneCandidature.Champs.EnvoyeeLe] = [refusDeLEnvoi];
        }

        if (nom is null || poste is null || refus.Count > 0)
        {
            return new ResultatDuSuivi.Refuse(refus);
        }

        // L'écriture est exclusive : deux modifications simultanées vers un même nouveau nom ne créent qu'une entreprise.
        var entreprise = await ecriture.TrouverLEntrepriseAsync(CleDEntreprise.De(nom), annulation);
        if (entreprise is null)
        {
            entreprise = Entreprise.Nouvelle(nom);
            await ecriture.AjouterAsync(entreprise, annulation);
        }

        candidature.RattacherA(entreprise.Id);
        candidature.ChangerDePoste(poste);
        if (envoiCorrige is { } envoi)
        {
            candidature.CorrigerLEnvoi(envoi, maintenant);
        }

        await ecriture.ValiderAsync(annulation);
        return new ResultatDuSuivi.Fait(FicheDUneCandidature.De(new CandidatureSuivie(candidature, new EntrepriseConnue(entreprise.Id, entreprise.Nom))));
    }

    /// <summary>
    /// Supprime la candidature et tout son historique, corrections comprises, définitivement ; son entreprise reste.
    /// Rend faux si elle n'existe pas.
    /// </summary>
    public async Task<bool> SupprimerAsync(int id, CancellationToken annulation)
    {
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        if (!await ecriture.SupprimerLaCandidatureAsync(id, annulation))
        {
            return false;
        }

        await ecriture.ValiderAsync(annulation);
        return true;
    }
}
