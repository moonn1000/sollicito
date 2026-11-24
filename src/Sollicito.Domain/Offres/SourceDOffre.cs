namespace Sollicito.Domain.Offres;

/// <summary>
/// La source d'une offre : le connecteur qui l'a fait entrer (ADR-0001, D6 ; modèle de domaine, « Dédoublonner les
/// offres »). Elle s'écrit en base en toutes lettres, sur 20 caractères au plus.
/// </summary>
public enum SourceDOffre
{
    /// <summary>Le texte d'une offre, collé par l'utilisateur (#36).</summary>
    TexteColle,

    /// <summary>Une ligne d'un fichier CSV d'offres, importé par l'utilisateur (#40, ADR-0015).</summary>
    FichierCsv,
}
