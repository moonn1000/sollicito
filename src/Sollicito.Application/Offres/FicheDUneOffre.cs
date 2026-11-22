using Sollicito.Application.Candidatures;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Offres;

/// <summary>La fiche d'une offre (#36) : ses champs, son texte et ses doublons possibles (règle 3), relus à chaque lecture.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Source">Le connecteur qui l'a fait entrer.</param>
/// <param name="Entreprise">Son entreprise, sous son nom affiché, ou rien pour une offre anonyme.</param>
/// <param name="Poste">Le poste offert.</param>
/// <param name="Lieu">Le lieu, ou rien.</param>
/// <param name="PublieeLe">Le jour de sa publication, ou rien.</param>
/// <param name="Adresse">L'adresse de l'annonce, ou rien.</param>
/// <param name="Texte">Son texte, tel quel, ou rien.</param>
/// <param name="AjouteeLe">L'instant de l'import, en UTC.</param>
/// <param name="DoublonsPossibles">Les autres offres de même entreprise et de même poste normalisé, les plus anciennes d'abord.</param>
public sealed record FicheDUneOffre(
    int Id,
    SourceDOffre Source,
    EntrepriseConnue? Entreprise,
    string Poste,
    string? Lieu,
    DateOnly? PublieeLe,
    string? Adresse,
    string? Texte,
    DateTime AjouteeLe,
    IReadOnlyList<OffreSignalee> DoublonsPossibles);

/// <summary>Une offre signalée comme doublon possible d'une autre (règle 3) : de quoi la reconnaître, et l'ouvrir.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Poste">Son poste.</param>
/// <param name="Entreprise">Son entreprise, sous son nom affiché.</param>
/// <param name="Lieu">Son lieu, ou rien.</param>
/// <param name="AjouteeLe">L'instant de son import, en UTC.</param>
public sealed record OffreSignalee(int Id, string Poste, EntrepriseConnue Entreprise, string? Lieu, DateTime AjouteeLe);

/// <summary>L'offre déjà enregistrée qu'un même texte recollé retrouve (règle 2).</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="AjouteeLe">L'instant de son import, en UTC.</param>
public sealed record OffreDejaEnregistree(int Id, DateTime AjouteeLe);
