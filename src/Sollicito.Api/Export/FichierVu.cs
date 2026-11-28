namespace Sollicito.Api.Export;

/// <summary>
/// Le fichier existant que l'utilisateur a accepté de remplacer, tel que le dialogue l'a vu à son retour (#118) : sa taille
/// et ses dates de création et de dernière écriture, en UTC. L'export ne le remplace que s'il est encore celui-là juste
/// avant le renommage : un fichier remplacé ou réécrit entre-temps, sous le même nom, n'est pas remplacé. Un programme du
/// même compte peut garder la taille et les dates ; l'identifiant du fichier demanderait un appel natif à Windows.
/// </summary>
/// <param name="Taille">La taille, en octets.</param>
/// <param name="CreeLe">L'instant de sa création, en UTC.</param>
/// <param name="EcritLe">L'instant de sa dernière écriture, en UTC.</param>
public sealed record FichierVu(long Taille, DateTime CreeLe, DateTime EcritLe)
{
    /// <summary>Le fichier à ce chemin, tel qu'il est maintenant, ou <c>null</c> s'il n'existe pas.</summary>
    public static FichierVu? Lire(string chemin)
    {
        ArgumentNullException.ThrowIfNull(chemin);
        var fichier = new FileInfo(chemin);
        return fichier.Exists ? new FichierVu(fichier.Length, fichier.CreationTimeUtc, fichier.LastWriteTimeUtc) : null;
    }
}
