using System.Text.Json.Serialization;
using Sollicito.Application.Contacts;
using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// La fiche d'une candidature, telle que l'écran la montre (#12) : la candidature, ce qu'on peut en faire, son
/// historique et ses relances (#14). Les passages permis et la correction possible viennent du domaine : l'écran n'en
/// garde pas de copie.
/// </summary>
/// <param name="Id">L'identifiant de la candidature.</param>
/// <param name="Entreprise">Son entreprise.</param>
/// <param name="Poste">Le poste visé.</param>
/// <param name="EnvoyeeLe">L'instant de l'envoi, en UTC : l'historique commence là.</param>
/// <param name="Statut">Son statut.</param>
/// <param name="PassagesPermis">Les statuts où elle peut passer, dans l'ordre du modèle de domaine ; aucun si elle est close.</param>
/// <param name="Correction">La correction du dernier changement, si un changement est à corriger.</param>
/// <param name="Historique">Ses événements, du plus ancien au plus récent ; à égalité, dans l'ordre d'enregistrement.</param>
/// <param name="Relances">Ses relances, dans l'ordre de leur planification : au plus une à faire.</param>
public sealed record FicheDUneCandidature(
    int Id,
    EntrepriseConnue Entreprise,
    string Poste,
    DateTime EnvoyeeLe,
    StatutDeCandidature Statut,
    IReadOnlyList<StatutDeCandidature> PassagesPermis,
    CorrectionPossible? Correction,
    IReadOnlyList<EvenementDeLHistorique> Historique,
    IReadOnlyList<RelanceLue> Relances)
{
    /// <summary>La fiche d'une candidature et de son entreprise.</summary>
    public static FicheDUneCandidature De(CandidatureSuivie suivie)
    {
        ArgumentNullException.ThrowIfNull(suivie);
        var candidature = suivie.Candidature;
        var aCorriger = candidature.ChangementACorriger;
        var destinataires = suivie.Destinataires.ToDictionary(contact => contact.Id);
        ContactConnu? Destinataire(Relance relance) =>
            relance.DestinataireId is { } id && destinataires.TryGetValue(id, out var contact) ? contact : null;

        return new FicheDUneCandidature(
            candidature.Id,
            suivie.Entreprise,
            candidature.Poste,
            candidature.EnvoyeeLe,
            candidature.Statut,
            candidature.PassagesPermis,
            aCorriger is null ? null : new CorrectionPossible(aCorriger.Id, aCorriger.Nouveau, aCorriger.Ancien),
            [.. candidature.Historique.Select(evenement => EvenementDeLHistorique.De(candidature, evenement, Destinataire))],
            [.. candidature.Relances.Select(relance => new RelanceLue(relance.Id, relance.Echeance, relance.Etat, Destinataire(relance)))]);
    }
}

/// <summary>Ce que ferait la correction du dernier changement de statut.</summary>
/// <param name="Changement">L'identifiant du changement qu'elle corrigerait.</param>
/// <param name="De">Le statut présent, nouveau statut de ce changement.</param>
/// <param name="RetourA">Le statut où elle ramènerait la candidature : l'ancien statut de ce changement.</param>
public sealed record CorrectionPossible(int Changement, StatutDeCandidature De, StatutDeCandidature RetourA);

/// <summary>Une relance de la candidature (#14).</summary>
/// <param name="Id">L'identifiant de la relance.</param>
/// <param name="Echeance">Son échéance, un jour du calendrier.</param>
/// <param name="Etat">À faire, faite ou abandonnée.</param>
/// <param name="Destinataire">Son destinataire, ou rien.</param>
public sealed record RelanceLue(int Id, DateOnly Echeance, EtatDeRelance Etat, ContactConnu? Destinataire);

/// <summary>Un événement de l'historique. Sa sorte (<c>sorte</c> en JSON) dit lequel.</summary>
/// <param name="Id">L'identifiant de l'événement.</param>
/// <param name="SurvenuLe">Son instant, en UTC.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "sorte")]
[JsonDerivedType(typeof(ChangementDeStatutLu), "changementDeStatut")]
[JsonDerivedType(typeof(EtapeLue), "etape")]
[JsonDerivedType(typeof(ClotureDeRelanceLue), "clotureDeRelance")]
public abstract record EvenementDeLHistorique(int Id, DateTime SurvenuLe)
{
    /// <summary>L'événement tel que la fiche le montre ; le destinataire d'une relance se lit parmi ceux de la fiche.</summary>
    public static EvenementDeLHistorique De(Candidature candidature, EvenementDeCandidature evenement, Func<Relance, ContactConnu?> destinataire)
    {
        ArgumentNullException.ThrowIfNull(candidature);
        ArgumentNullException.ThrowIfNull(destinataire);
        return evenement switch
        {
            ChangementDeStatut changement => new ChangementDeStatutLu(
                changement.Id,
                changement.SurvenuLe,
                changement.Ancien,
                changement.Nouveau,
                changement.Corrige?.Id,
                candidature.EstCorrige(changement)),
            Etape etape => new EtapeLue(etape.Id, etape.SurvenuLe, etape.Type, etape.Note, etape.Corrige?.Id, candidature.EstCorrigee(etape)),
            ClotureDeRelance cloture => new ClotureDeRelanceLue(
                cloture.Id,
                cloture.SurvenuLe,
                cloture.Relance.Id,
                cloture.Relance.Etat,
                cloture.Relance.Echeance,
                destinataire(cloture.Relance)),
            _ => throw new ArgumentOutOfRangeException(nameof(evenement), evenement?.GetType().Name, "Sorte d'événement inconnue."),
        };
    }
}

/// <summary>Un changement de statut : un passage, ou la correction d'un changement précédent.</summary>
/// <param name="Id">L'identifiant de l'événement.</param>
/// <param name="SurvenuLe">Son instant, en UTC, donné par l'horloge.</param>
/// <param name="Ancien">Le statut d'avant.</param>
/// <param name="Nouveau">Le statut d'après.</param>
/// <param name="Corrige">Pour une correction, l'identifiant du changement corrigé ; rien pour un passage.</param>
/// <param name="EstCorrige">Vrai si une correction l'a corrigé depuis : il reste dans l'historique, marqué comme tel.</param>
public sealed record ChangementDeStatutLu(
    int Id,
    DateTime SurvenuLe,
    StatutDeCandidature Ancien,
    StatutDeCandidature Nouveau,
    int? Corrige,
    bool EstCorrige) : EvenementDeLHistorique(Id, SurvenuLe);

/// <summary>Une étape, qui ne change pas le statut : notée, ou la correction d'une autre (#25).</summary>
/// <param name="Id">L'identifiant de l'événement.</param>
/// <param name="SurvenuLe">Son instant, en UTC, donné par l'utilisateur.</param>
/// <param name="Type">Son type.</param>
/// <param name="Note">Sa note, ou rien.</param>
/// <param name="Corrige">Pour une correction, l'identifiant de l'étape corrigée ; rien pour une étape notée.</param>
/// <param name="EstCorrige">Vrai si une correction l'a corrigée depuis : elle reste dans l'historique, marquée comme telle.</param>
public sealed record EtapeLue(int Id, DateTime SurvenuLe, TypeDEtape Type, string? Note, int? Corrige, bool EstCorrige)
    : EvenementDeLHistorique(Id, SurvenuLe);

/// <summary>La clôture d'une relance (#14) : marquée faite, ou abandonnée, par l'utilisateur ou à la clôture de la candidature.</summary>
/// <param name="Id">L'identifiant de l'événement.</param>
/// <param name="SurvenuLe">Son instant, en UTC, donné par l'horloge.</param>
/// <param name="Relance">L'identifiant de la relance close.</param>
/// <param name="Etat">Faite, ou abandonnée.</param>
/// <param name="Echeance">L'échéance de la relance.</param>
/// <param name="Destinataire">Son destinataire, ou rien.</param>
public sealed record ClotureDeRelanceLue(int Id, DateTime SurvenuLe, int Relance, EtatDeRelance Etat, DateOnly Echeance, ContactConnu? Destinataire)
    : EvenementDeLHistorique(Id, SurvenuLe);
