using Sollicito.Domain.Offres;

namespace Sollicito.Application.Offres;

/// <summary>
/// Le modèle d'offre commun (ADR-0001, D6 ; #34, absorbée par #36) : ce qu'un connecteur fait d'une offre que sa source
/// lui donne, avant toute validation. Le cœur (<see cref="IImportDesOffres"/>) valide ensuite les champs, calcule
/// l'empreinte sur les parties que le connecteur annonce, dédoublonne et enregistre : ajouter une source ne touche ni au
/// domaine, ni au cœur.
/// </summary>
/// <param name="Source">La source de l'offre, celle du connecteur.</param>
/// <param name="PartiesDeLEmpreinte">
/// Les parties sur lesquelles se calcule l'empreinte (<see cref="EmpreinteDUneOffre.De"/>), dans l'ordre : le texte
/// d'une offre collée, seule partie ; les champs d'une ligne de CSV, dans l'ordre de son format (#40). Le cœur ne les
/// joint jamais lui-même avant de les normaliser.
/// </param>
/// <param name="Champs">Les champs de l'offre, tels que la source les donne.</param>
public sealed record OffreRecue(SourceDOffre Source, IReadOnlyList<string> PartiesDeLEmpreinte, ChampsRecus Champs);

/// <summary>Les champs d'une offre reçue, tels que la source les donne : rien n'y est lu, retouché ni converti.</summary>
/// <param name="Texte">Le texte de l'offre, s'il y en a un.</param>
/// <param name="Entreprise">Le nom de l'entreprise, s'il y en a un.</param>
/// <param name="Poste">Le poste.</param>
/// <param name="Lieu">Le lieu.</param>
/// <param name="PublieeLe">La date de publication, <c>AAAA-MM-JJ</c>.</param>
/// <param name="Adresse">L'adresse de l'annonce.</param>
public sealed record ChampsRecus(string? Texte, string? Entreprise, string? Poste, string? Lieu, string? PublieeLe, string? Adresse);

/// <summary>
/// Un connecteur d'offres (ADR-0001, D6) : l'adaptateur d'une source, qui fait des offres reçues, le modèle commun, de ce
/// que sa source lui donne. Le banc de tests de contrat (<c>ContratDUnConnecteurDOffres</c>) vérifie chacun.
/// </summary>
/// <typeparam name="TEntree">Ce que la source donne : une saisie de l'interface, un fichier…</typeparam>
public interface IConnecteurDOffres<in TEntree>
{
    /// <summary>La source de toutes les offres qu'il reçoit.</summary>
    SourceDOffre Source { get; }

    /// <summary>Les offres reçues de cette entrée, sans rien valider : c'est l'affaire du cœur.</summary>
    IReadOnlyList<OffreRecue> Recevoir(TEntree entree);
}

/// <summary>
/// Le premier connecteur : l'offre collée (#36). Une saisie de l'interface fait une offre reçue, dont l'empreinte se
/// calcule sur une seule partie, le texte collé.
/// </summary>
public sealed class ConnecteurDuTexteColle : IConnecteurDOffres<SaisieDUneOffre>
{
    public SourceDOffre Source => SourceDOffre.TexteColle;

    public IReadOnlyList<OffreRecue> Recevoir(SaisieDUneOffre entree)
    {
        ArgumentNullException.ThrowIfNull(entree);
        return
        [
            new OffreRecue(
                Source,
                [entree.Texte ?? ""],
                new ChampsRecus(entree.Texte, entree.Entreprise, entree.Poste, entree.Lieu, entree.PublieeLe, entree.Adresse)),
        ];
    }
}
