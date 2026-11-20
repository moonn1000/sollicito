using System.Text.Json.Serialization;
using Sollicito.Domain.Candidatures;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// La fiche d'une candidature, telle que l'écran la montre (#12) : la candidature, ce qu'on peut en faire, et son
/// historique. Les passages permis et la correction possible viennent du domaine : l'écran n'en garde pas de copie.
/// </summary>
/// <param name="Id">L'identifiant de la candidature.</param>
/// <param name="Entreprise">Son entreprise.</param>
/// <param name="Poste">Le poste visé.</param>
/// <param name="EnvoyeeLe">L'instant de l'envoi, en UTC : l'historique commence là.</param>
/// <param name="Statut">Son statut.</param>
/// <param name="PassagesPermis">Les statuts où elle peut passer, dans l'ordre du modèle de domaine ; aucun si elle est close.</param>
/// <param name="Correction">La correction du dernier changement, si un changement est à corriger.</param>
/// <param name="Historique">Ses événements, du plus ancien au plus récent ; à égalité, dans l'ordre d'enregistrement.</param>
public sealed record FicheDUneCandidature(
    int Id,
    EntrepriseConnue Entreprise,
    string Poste,
    DateTime EnvoyeeLe,
    StatutDeCandidature Statut,
    IReadOnlyList<StatutDeCandidature> PassagesPermis,
    CorrectionPossible? Correction,
    IReadOnlyList<EvenementDeLHistorique> Historique)
{
    /// <summary>La fiche d'une candidature et de son entreprise.</summary>
    public static FicheDUneCandidature De(CandidatureSuivie suivie)
    {
        ArgumentNullException.ThrowIfNull(suivie);
        var candidature = suivie.Candidature;
        var aCorriger = candidature.ChangementACorriger;

        return new FicheDUneCandidature(
            candidature.Id,
            suivie.Entreprise,
            candidature.Poste,
            candidature.EnvoyeeLe,
            candidature.Statut,
            candidature.PassagesPermis,
            aCorriger is null ? null : new CorrectionPossible(aCorriger.Id, aCorriger.Nouveau, aCorriger.Ancien),
            [.. candidature.Historique.Select(evenement => EvenementDeLHistorique.De(candidature, evenement))]);
    }
}

/// <summary>Ce que ferait la correction du dernier changement de statut.</summary>
/// <param name="Changement">L'identifiant du changement qu'elle corrigerait.</param>
/// <param name="De">Le statut présent, nouveau statut de ce changement.</param>
/// <param name="RetourA">Le statut où elle ramènerait la candidature : l'ancien statut de ce changement.</param>
public sealed record CorrectionPossible(int Changement, StatutDeCandidature De, StatutDeCandidature RetourA);

/// <summary>Un événement de l'historique. Sa sorte (<c>sorte</c> en JSON) dit lequel.</summary>
/// <param name="Id">L'identifiant de l'événement.</param>
/// <param name="SurvenuLe">Son instant, en UTC.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "sorte")]
[JsonDerivedType(typeof(ChangementDeStatutLu), "changementDeStatut")]
[JsonDerivedType(typeof(EtapeLue), "etape")]
public abstract record EvenementDeLHistorique(int Id, DateTime SurvenuLe)
{
    /// <summary>L'événement tel que la fiche le montre.</summary>
    public static EvenementDeLHistorique De(Candidature candidature, EvenementDeCandidature evenement)
    {
        ArgumentNullException.ThrowIfNull(candidature);
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
