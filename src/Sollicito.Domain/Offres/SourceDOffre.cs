namespace Sollicito.Domain.Offres;

/// <summary>
/// La source d'une offre : le connecteur qui l'a fait entrer (ADR-0001, D6 ; modèle de domaine, « Dédoublonner les
/// offres »). Elle s'écrit en base en toutes lettres. L'import d'un fichier CSV (#40) ajoutera la sienne.
/// </summary>
public enum SourceDOffre
{
    /// <summary>Le texte d'une offre, collé par l'utilisateur (#36).</summary>
    TexteColle,
}
