namespace Sollicito.Application.Export;

/// <summary>
/// Les formes de l'export (#49, ADR-0020) : le JSON, qui garde chaque valeur telle qu'en base.
/// </summary>
public enum FormatDExport
{
    /// <summary>Un seul fichier JSON.</summary>
    Json,
}

/// <summary>Une entreprise, telle qu'en base.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Nom">Son nom affiché.</param>
/// <param name="Cle">Sa clé, calculée par Sollicito à partir du nom.</param>
public sealed record EntrepriseExportee(int Id, string Nom, string Cle);

/// <summary>Un contact, tel qu'en base.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="EntrepriseId">L'entreprise du contact.</param>
/// <param name="Nom">Son nom.</param>
/// <param name="Email">Son adresse électronique.</param>
public sealed record ContactExporte(int Id, int EntrepriseId, string Nom, string Email);

/// <summary>Une offre, telle qu'en base.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Source">Le connecteur qui l'a fait entrer, en toutes lettres.</param>
/// <param name="Empreinte">Son empreinte, calculée par Sollicito à son entrée.</param>
/// <param name="EntrepriseId">L'entreprise qui la publie, si l'offre la nomme.</param>
/// <param name="Poste">Le poste.</param>
/// <param name="PosteNormalise">Le poste normalisé, calculé par Sollicito.</param>
/// <param name="Lieu">Le lieu, s'il est connu.</param>
/// <param name="PublieeLe">La date de publication, si elle est connue.</param>
/// <param name="Adresse">L'adresse de l'annonce, si elle est connue.</param>
/// <param name="Texte">Le texte de l'offre, s'il y en a un.</param>
/// <param name="AjouteeLe">L'instant où elle est entrée, en UTC.</param>
public sealed record OffreExportee(
    int Id,
    string Source,
    string Empreinte,
    int? EntrepriseId,
    string Poste,
    string PosteNormalise,
    string? Lieu,
    DateOnly? PublieeLe,
    string? Adresse,
    string? Texte,
    DateTime AjouteeLe);

/// <summary>Une candidature, telle qu'en base.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="EntrepriseId">Son entreprise.</param>
/// <param name="OffreId">L'offre d'où elle vient, s'il y en a une.</param>
/// <param name="Poste">Le poste.</param>
/// <param name="EnvoyeeLe">L'instant de l'envoi, en UTC.</param>
/// <param name="Statut">Son statut, en toutes lettres.</param>
public sealed record CandidatureExportee(int Id, int EntrepriseId, int? OffreId, string Poste, DateTime EnvoyeeLe, string Statut);

/// <summary>Une relance, telle qu'en base.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="CandidatureId">Sa candidature.</param>
/// <param name="DestinataireId">Le contact à qui l'adresser, s'il y en a un.</param>
/// <param name="Echeance">Son échéance, un jour du calendrier.</param>
/// <param name="Etat">Son état, en toutes lettres.</param>
public sealed record RelanceExportee(int Id, int CandidatureId, int? DestinataireId, DateOnly Echeance, string Etat);

/// <summary>
/// Un événement de l'historique, tel qu'en base : une ligne de la table des trois sortes, avec toutes ses colonnes. Les
/// colonnes d'une autre sorte sont toujours vides dans une base de Sollicito.
/// </summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="CandidatureId">Sa candidature.</param>
/// <param name="Sorte">Sa sorte : <c>ChangementDeStatut</c>, <c>Etape</c> ou <c>ClotureDeRelance</c>.</param>
/// <param name="SurvenuLe">Son instant, en UTC.</param>
/// <param name="Ancien">Le statut d'avant, pour un changement de statut.</param>
/// <param name="Nouveau">Le statut d'après, pour un changement de statut.</param>
/// <param name="CorrigeId">Le changement de statut qu'il corrige, s'il en corrige un.</param>
/// <param name="Type">Le type d'une étape.</param>
/// <param name="Note">La note d'une étape, s'il y en a une.</param>
/// <param name="EtapeCorrigeeId">L'étape qu'elle corrige, si elle en corrige une.</param>
/// <param name="RelanceId">La relance close, pour une clôture de relance.</param>
public sealed record EvenementExporte(
    int Id,
    int CandidatureId,
    string Sorte,
    DateTime SurvenuLe,
    string? Ancien,
    string? Nouveau,
    int? CorrigeId,
    string? Type,
    string? Note,
    int? EtapeCorrigeeId,
    int? RelanceId);

/// <summary>Le nombre de lignes de chaque entité qu'un export a écrites : ce que le journal en dit.</summary>
public sealed record ComptesDeLExport(int Entreprises, int Contacts, int Offres, int Candidatures, int Relances, int Evenements);

/// <summary>
/// Où en est un export, pour les tests et la version de test de la fenêtre : rien ne s'y passe dans la version publiée.
/// Chaque table lue se signale une fois sa dernière ligne écrite.
/// </summary>
public enum EtapeDeLExport
{
    /// <summary>Les entreprises sont lues et écrites.</summary>
    EntreprisesEcrites,

    /// <summary>Les contacts sont lus et écrits.</summary>
    ContactsEcrits,

    /// <summary>Les offres sont lues et écrites.</summary>
    OffresEcrites,

    /// <summary>Les candidatures sont lues et écrites.</summary>
    CandidaturesEcrites,

    /// <summary>Les relances sont lues et écrites.</summary>
    RelancesEcrites,

    /// <summary>Les événements sont lus et écrits.</summary>
    EvenementsEcrits,

    /// <summary>Le fichier provisoire est complet, et poussé sur le disque : il ne reste qu'à le renommer.</summary>
    FichierEcrit,
}

/// <summary>
/// L'issue d'un export, que la fenêtre rend à la page sans chemin. Seule <see cref="Exporte"/> a écrit un fichier sous le
/// nom choisi ; toutes les autres n'y ont rien laissé, et n'ont remplacé aucun fichier.
/// </summary>
public enum IssueDeLExport
{
    /// <summary>Le fichier est écrit sous le nom choisi.</summary>
    Exporte,

    /// <summary>L'utilisateur a fermé le dialogue, ou la fenêtre s'est fermée pendant l'export.</summary>
    Annule,

    /// <summary>Une restauration, ou un autre dialogue de l'hôte, occupe Sollicito.</summary>
    Occupe,

    /// <summary>Un fichier de ce nom est apparu sans que l'utilisateur ait accepté de le remplacer.</summary>
    Existe,

    /// <summary>Le nom choisi ne finit pas par l'extension du format.</summary>
    Extension,

    /// <summary>Le fichier choisi est dans le dossier de données de Sollicito.</summary>
    DossierDeDonnees,

    /// <summary>Sollicito n'a pas le droit d'écrire dans ce dossier, ou de remplacer ce fichier.</summary>
    AccesRefuse,

    /// <summary>La place manque sur ce disque.</summary>
    PlaceManquante,

    /// <summary>Le dossier, ou son support, a disparu.</summary>
    DossierIntrouvable,

    /// <summary>Toute autre erreur, de la base ou du disque.</summary>
    Impossible,
}
