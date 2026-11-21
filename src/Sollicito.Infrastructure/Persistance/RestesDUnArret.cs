using System.Text.RegularExpressions;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Les restes qu'un arrêt brutal laisse dans le dossier de données (#26, ADR-0008) : la copie de travail d'une mise à jour
/// interrompue, de la taille de la base, et son journal ; les fichiers provisoires des réglages et de la dernière copie.
/// Chacun ne vit que le temps d'une opération, que le processus arrêté n'a pas finie. Ils se reconnaissent à leur nom,
/// avec sa marque tirée au hasard : ni la base, ni ses journaux, ni aucun autre fichier n'y répondent.
/// </summary>
public static partial class RestesDUnArret
{
    // Le fichier lui-même, puis les journaux que SQLite range à côté d'une base.
    private static readonly string[] SuffixesDUnReste = ["", "-journal", "-wal", "-shm"];

    /// <summary>
    /// Le nom d'une nouvelle copie de travail d'une mise à jour, dans le dossier de données : la base, puis une marque tirée
    /// au hasard. <see cref="RetirerAsync"/> reconnaît ce nom, et lui seul.
    /// </summary>
    public static string NomDUneCopieDeTravail() => $"{DossierDeLaBase.NomDuFichier}.{Guid.NewGuid():N}.migration";

    /// <summary>
    /// Retire ces restes, et rend le nombre de ceux qui ont disparu : une copie et ses journaux comptent pour un. À
    /// n'appeler que sous le <see cref="VerrouDeLInstance"/> du dossier, avant toute mise à jour : aucune opération de
    /// Sollicito ne peut alors les tenir. Un reste qu'un autre programme tient reste, jusqu'au lancement suivant.
    /// </summary>
    public static async Task<int> RetirerAsync(DossierDeLaBase dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        string[] noms;
        try
        {
            noms = [.. Directory.EnumerateFiles(dossier.Chemin).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        // La copie et ses journaux d'un bloc : un journal resté seul désigne la même copie.
        var restes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var nom in noms)
        {
            if (CopieDeTravail().Match(nom) is { Success: true } copie)
            {
                restes.Add(copie.Groups["copie"].Value);
            }
            else if (FichierProvisoire().IsMatch(nom))
            {
                restes.Add(nom);
            }
        }

        var retires = 0;
        foreach (var reste in restes)
        {
            var chemin = Path.Combine(dossier.Chemin, reste);
            await CopiesVerifiees.EffacerAsync(chemin);
            if (!SuffixesDUnReste.Any(suffixe => File.Exists(chemin + suffixe)))
            {
                retires++;
            }
        }

        return retires;
    }

    [GeneratedRegex(@"^(?<copie>sollicito\.db\.[0-9a-f]{32}\.migration)(-journal|-wal|-shm)?$", RegexOptions.CultureInvariant)]
    private static partial Regex CopieDeTravail();

    [GeneratedRegex(@"^(reglages|sauvegardes)\.json\.[0-9a-f]{32}\.provisoire$", RegexOptions.CultureInvariant)]
    private static partial Regex FichierProvisoire();
}
