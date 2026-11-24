using Sollicito.Domain.Offres;

namespace Sollicito.Application.Offres;

/// <summary>
/// Le modèle d'offre commun (ADR-0001, D6 ; #34, absorbée par #36 ; ADR-0015) : ce qu'un connecteur fait d'une offre que
/// sa source lui donne, avant toute validation. Le cœur (<see cref="IImportDesOffres"/>) lit ensuite les champs
/// (<see cref="LectureDUneOffre"/>), calcule l'empreinte sur les champs que le connecteur annonce, dédoublonne et
/// enregistre : ajouter une source ne touche ni au domaine, ni au cœur.
/// </summary>
/// <param name="Source">La source de l'offre, celle du connecteur.</param>
/// <param name="ChampsDeLEmpreinte">
/// Les champs sur lesquels se calcule l'empreinte (<see cref="EmpreinteDUneOffre.De"/>), dans l'ordre : le texte d'une
/// offre collée, seul champ ; les six champs d'une ligne de CSV, dans l'ordre de son format (#40). Le connecteur ne
/// donne que leur nom : le cœur prend chacun tel qu'il l'a lu, la date sous sa forme <c>AAAA-MM-JJ</c>, et ne les joint
/// jamais avant de les normaliser.
/// </param>
/// <param name="Champs">Les champs de l'offre, tels que la source les donne.</param>
public sealed record OffreRecue(SourceDOffre Source, IReadOnlyList<ChampDeLEmpreinte> ChampsDeLEmpreinte, ChampsRecus Champs);

/// <summary>Un champ d'une offre qui peut entrer dans son empreinte (ADR-0013, ADR-0015).</summary>
public enum ChampDeLEmpreinte
{
    /// <summary>Le texte de l'offre ; vide, une partie vide.</summary>
    Texte,

    /// <summary>Le poste, toujours présent.</summary>
    Poste,

    /// <summary>Le nom de l'entreprise, tel que lu ; vide, une partie vide.</summary>
    Entreprise,

    /// <summary>Le lieu ; vide, une partie vide.</summary>
    Lieu,

    /// <summary>La date de publication, sous sa forme <c>AAAA-MM-JJ</c> quelle que soit la forme reçue ; vide, une partie vide.</summary>
    PublieeLe,

    /// <summary>L'adresse de l'annonce ; vide, une partie vide.</summary>
    Adresse,
}

/// <summary>Les champs d'une offre reçue, tels que la source les donne : rien n'y est lu, retouché ni converti.</summary>
/// <param name="Texte">Le texte de l'offre, s'il y en a un.</param>
/// <param name="Entreprise">Le nom de l'entreprise, s'il y en a un.</param>
/// <param name="Poste">Le poste.</param>
/// <param name="Lieu">Le lieu.</param>
/// <param name="PublieeLe">La date de publication : <c>AAAA-MM-JJ</c>, ou aussi <c>JJ/MM/AAAA</c> pour une ligne de CSV.</param>
/// <param name="Adresse">L'adresse de l'annonce.</param>
public sealed record ChampsRecus(string? Texte, string? Entreprise, string? Poste, string? Lieu, string? PublieeLe, string? Adresse);

/// <summary>
/// Un connecteur d'offres (ADR-0001, D6) : l'adaptateur d'une source, qui fait des offres reçues, le modèle commun, de ce
/// que sa source lui donne. Le banc de tests de contrat (<c>ContratDUnConnecteurDOffres</c>) vérifie chacun.
/// </summary>
/// <typeparam name="TEntree">Ce que la source donne : une saisie de l'interface, un fichier lu…</typeparam>
public interface IConnecteurDOffres<in TEntree>
{
    /// <summary>La source de toutes les offres qu'il reçoit.</summary>
    SourceDOffre Source { get; }

    /// <summary>Les offres reçues de cette entrée, sans rien valider : c'est l'affaire du cœur.</summary>
    IReadOnlyList<OffreRecue> Recevoir(TEntree entree);
}

/// <summary>
/// Le premier connecteur : l'offre collée (#36). Une saisie de l'interface fait une offre reçue, dont l'empreinte se
/// calcule sur un seul champ, le texte collé.
/// </summary>
public sealed class ConnecteurDuTexteColle : IConnecteurDOffres<SaisieDUneOffre>
{
    /// <summary>Le seul champ de l'empreinte d'une offre collée : son texte.</summary>
    public static readonly IReadOnlyList<ChampDeLEmpreinte> ChampsDeLEmpreinte = [ChampDeLEmpreinte.Texte];

    public SourceDOffre Source => SourceDOffre.TexteColle;

    public IReadOnlyList<OffreRecue> Recevoir(SaisieDUneOffre entree)
    {
        ArgumentNullException.ThrowIfNull(entree);
        return
        [
            new OffreRecue(
                Source,
                ChampsDeLEmpreinte,
                new ChampsRecus(entree.Texte, entree.Entreprise, entree.Poste, entree.Lieu, entree.PublieeLe, entree.Adresse)),
        ];
    }
}
