using Sollicito.Application.Candidatures;
using Sollicito.Application.Contacts;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Relances;

/// <summary>
/// Les relances des candidatures (#14) : en planifier une, la changer, la marquer faite ou l'abandonner, et lister celles
/// que le tableau de bord signale. Chaque action lit la candidature et l'écrit dans une même écriture exclusive, comme
/// celles de la fiche (#12) : deux planifications simultanées n'en créent qu'une. L'horloge date les clôtures, et donne
/// le fuseau où se lit la date de l'envoi, et aujourd'hui.
/// </summary>
public sealed class RelancesDesCandidatures(IDepotDesCandidatures depot, TimeProvider horloge)
{
    /// <summary>Le message d'un destinataire qui n'est pas un contact de l'entreprise de la candidature, ou qui n'existe pas.</summary>
    public const string DestinataireEtranger = "Ce contact n'est pas un contact de l'entreprise de la candidature.";

    /// <summary>
    /// Planifie une relance de la candidature : le délai saisi, ou <paramref name="delaiParDefaut"/> s'il manque, et le
    /// destinataire saisi, ou personne. Rend les refus des champs, ou le motif d'une relance que la candidature n'admet
    /// pas, sans rien changer.
    /// </summary>
    public Task<ResultatDuSuivi> PlanifierAsync(int id, SaisieDUneRelance saisie, int delaiParDefaut, CancellationToken annulation) =>
        EcrireAsync(id, relance: null, saisie, delaiParDefaut, annulation);

    /// <summary>
    /// Change le délai et le destinataire de la relance à faire que l'écran montrait, comme à la planification. Une
    /// relance close, ou qui n'est pas de cette candidature, est refusée, sans rien changer.
    /// </summary>
    public Task<ResultatDuSuivi> ChangerAsync(int id, int relance, SaisieDUneRelance saisie, int delaiParDefaut, CancellationToken annulation) =>
        EcrireAsync(id, relance, saisie, delaiParDefaut, annulation);

    /// <summary>Marque faite la relance à faire que l'écran montrait : sa clôture entre dans l'historique.</summary>
    public Task<ResultatDuSuivi> MarquerFaiteAsync(int id, int relance, CancellationToken annulation) =>
        CloreAsync(id, relance, EtatDeRelance.Faite, annulation);

    /// <summary>Abandonne la relance à faire que l'écran montrait : sa clôture entre dans l'historique.</summary>
    public Task<ResultatDuSuivi> AbandonnerAsync(int id, int relance, CancellationToken annulation) =>
        CloreAsync(id, relance, EtatDeRelance.Abandonnee, annulation);

    /// <summary>
    /// Les relances que signale le tableau de bord, en date locale : les échues, dont l'échéance tombe aujourd'hui ou
    /// avant, et celles de la semaine, dans les sept jours qui suivent. Chacune par échéance, puis par ordre de
    /// planification.
    /// </summary>
    public async Task<RelancesDuTableauDeBord> ListerAsync(CancellationToken annulation)
    {
        var aujourdhui = DelaiDeRelance.JourLocal(horloge.GetUtcNow().UtcDateTime, horloge.LocalTimeZone);
        var relances = await depot.ListerLesRelancesAFaireAsync(Relance.FinDeLaSemaine(aujourdhui), annulation);
        return new RelancesDuTableauDeBord(
            aujourdhui,
            [.. relances.Where(relance => Relance.EstUneEcheanceEchue(relance.Echeance, aujourdhui))],
            [.. relances.Where(relance => Relance.EstUneEcheanceDeLaSemaine(relance.Echeance, aujourdhui))]);
    }

    // Une relance planifiée (relance absente), ou la relance à faire changée : mêmes champs, mêmes règles.
    private async Task<ResultatDuSuivi> EcrireAsync(int id, int? relance, SaisieDUneRelance saisie, int delaiParDefaut, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var delai = saisie.Delai ?? delaiParDefaut;
        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!DelaiDeRelance.EstAdmis(delai))
        {
            refus[SaisieDUneRelance.Champs.Delai] = [DelaiDeRelance.HorsBornes];
        }

        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var suivie = await ecriture.TrouverLaCandidatureAsync(id, annulation);
        if (suivie is null)
        {
            return new ResultatDuSuivi.Introuvable();
        }

        var candidature = suivie.Candidature;
        ContactConnu? destinataire = null;
        if (saisie.Destinataire is { } idDuContact)
        {
            var contact = idDuContact > 0 ? await ecriture.TrouverLeContactAsync(idDuContact, annulation) : null;
            if (contact is null || contact.EntrepriseId != candidature.EntrepriseId)
            {
                refus[SaisieDUneRelance.Champs.Destinataire] = [DestinataireEtranger];
            }
            else
            {
                destinataire = ContactConnu.De(contact);
            }
        }

        if (refus.Count > 0)
        {
            return new ResultatDuSuivi.Refuse(refus);
        }

        string? motif;
        if (relance is { } idDeLaRelance)
        {
            if (!candidature.EssayerDeChangerLaRelance(idDeLaRelance, delai, destinataire?.Id, horloge.LocalTimeZone, out motif))
            {
                return new ResultatDuSuivi.NonPermis(motif);
            }
        }
        else if (!candidature.EssayerDePlanifierUneRelance(delai, destinataire?.Id, horloge.LocalTimeZone, out motif))
        {
            return new ResultatDuSuivi.NonPermis(motif);
        }

        await ecriture.ValiderAsync(annulation);
        var destinataires = destinataire is null
            ? suivie.Destinataires
            : [.. suivie.Destinataires.Where(connu => connu.Id != destinataire.Id), destinataire];
        return new ResultatDuSuivi.Fait(FicheDUneCandidature.De(suivie with { Destinataires = destinataires }));
    }

    private async Task<ResultatDuSuivi> CloreAsync(int id, int relance, EtatDeRelance issue, CancellationToken annulation)
    {
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var suivie = await ecriture.TrouverLaCandidatureAsync(id, annulation);
        if (suivie is null)
        {
            return new ResultatDuSuivi.Introuvable();
        }

        if (!suivie.Candidature.EssayerDeCloreLaRelance(relance, issue, horloge.GetUtcNow().UtcDateTime, out var refus))
        {
            return new ResultatDuSuivi.NonPermis(refus);
        }

        await ecriture.ValiderAsync(annulation);
        return new ResultatDuSuivi.Fait(FicheDUneCandidature.De(suivie));
    }
}

/// <summary>Une relance telle que l'écran l'envoie : un délai en jours et un destinataire, facultatifs tous deux.</summary>
/// <param name="Delai">Le délai en jours, compté depuis la date locale de l'envoi ; absent, le délai par défaut.</param>
/// <param name="Destinataire">L'identifiant d'un contact de l'entreprise de la candidature ; absent, personne.</param>
public sealed record SaisieDUneRelance(int? Delai, int? Destinataire)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Delai = "delai";
        public const string Destinataire = "destinataire";
    }
}

/// <summary>Les relances que signale le tableau de bord.</summary>
/// <param name="Aujourdhui">Le jour local où elles ont été lues.</param>
/// <param name="Echues">Les relances à faire dont l'échéance tombe aujourd'hui ou avant.</param>
/// <param name="DeLaSemaine">Les relances à faire dont l'échéance tombe dans les sept jours qui suivent.</param>
public sealed record RelancesDuTableauDeBord(DateOnly Aujourdhui, IReadOnlyList<RelanceSignalee> Echues, IReadOnlyList<RelanceSignalee> DeLaSemaine);

/// <summary>Une relance à faire, telle que le tableau de bord la signale.</summary>
/// <param name="Id">L'identifiant de la relance.</param>
/// <param name="Echeance">Son échéance.</param>
/// <param name="Candidature">Sa candidature.</param>
/// <param name="Destinataire">Son destinataire, ou rien.</param>
public sealed record RelanceSignalee(int Id, DateOnly Echeance, CandidatureDeLaRelance Candidature, ContactConnu? Destinataire);

/// <summary>La candidature d'une relance signalée : de quoi la nommer et ouvrir sa fiche.</summary>
/// <param name="Id">L'identifiant de la candidature.</param>
/// <param name="Poste">Le poste visé.</param>
/// <param name="Entreprise">Son entreprise.</param>
public sealed record CandidatureDeLaRelance(int Id, string Poste, EntrepriseConnue Entreprise);
