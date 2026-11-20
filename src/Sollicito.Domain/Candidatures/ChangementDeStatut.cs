namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Un changement de statut, daté par l'horloge (modèle de domaine, « Statuts d'une candidature »). C'est un passage
/// du diagramme d'états, ou la correction d'un changement précédent, qu'elle désigne par <see cref="Corrige"/> : la
/// correction ramène la candidature au statut d'avant ce changement, qui reste dans l'historique, marqué comme corrigé.
/// </summary>
public sealed class ChangementDeStatut : EvenementDeCandidature
{
    // Pour Entity Framework Core, qui lit un changement enregistré.
    private ChangementDeStatut(DateTime survenuLe, StatutDeCandidature ancien, StatutDeCandidature nouveau)
        : base(survenuLe)
    {
        Ancien = ancien;
        Nouveau = nouveau;
    }

    /// <summary>Le statut d'avant le changement.</summary>
    public StatutDeCandidature Ancien { get; private set; }

    /// <summary>Le statut d'après le changement.</summary>
    public StatutDeCandidature Nouveau { get; private set; }

    /// <summary>Pour une correction, le changement qu'elle corrige ; rien pour un passage.</summary>
    public ChangementDeStatut? Corrige { get; private set; }

    /// <summary>Vrai pour une correction : elle ne se corrige pas elle-même.</summary>
    public bool EstUneCorrection => Corrige is not null;

    /// <summary>Un passage du diagramme d'états, déjà vérifié par la candidature.</summary>
    internal static ChangementDeStatut Passage(StatutDeCandidature ancien, StatutDeCandidature nouveau, DateTime maintenant) =>
        new(maintenant, ancien, nouveau);

    /// <summary>
    /// La correction de <paramref name="corrige"/> : du statut présent, le nouveau statut du changement corrigé, elle
    /// revient à son ancien statut.
    /// </summary>
    internal static ChangementDeStatut Correction(ChangementDeStatut corrige, DateTime maintenant) =>
        new(maintenant, corrige.Nouveau, corrige.Ancien) { Corrige = corrige };
}
