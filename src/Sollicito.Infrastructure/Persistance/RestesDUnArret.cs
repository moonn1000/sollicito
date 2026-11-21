using System.Text.RegularExpressions;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Les restes qu'un arrêt brutal laisse dans le dossier de données (#26, ADR-0008) : la copie de travail d'une mise à jour
/// ou d'une restauration interrompue (#28), de la taille de la base, et ses journaux ; les fichiers provisoires des
/// réglages et de la dernière copie ; dans le dossier des sauvegardes par défaut, les fichiers provisoires d'une copie
/// d'avant une mise à jour ou une restauration, et le journal d'une copie brute resté sans elle. Chacun ne vit que le temps
/// d'une opération, que le processus arrêté n'a pas finie. Ils se reconnaissent à leur nom, avec sa marque tirée au hasard : ni la base, ni ses journaux, ni une copie
/// finie, ni aucun autre fichier n'y répondent.
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
    /// Le nom d'une nouvelle copie de travail d'une restauration (#28), dans le dossier de données : la base, une marque
    /// tirée au hasard, puis <c>.restauration</c>. <see cref="RetirerAsync"/> reconnaît ce nom, et lui seul.
    /// </summary>
    public static string NomDUneCopieARestaurer() => $"{DossierDeLaBase.NomDuFichier}.{Guid.NewGuid():N}.restauration";

    /// <summary>
    /// Retire ces restes, et rend le nombre de ceux qui ont disparu : une copie et ses journaux comptent pour un. À
    /// n'appeler que sous le <see cref="VerrouDeLInstance"/> du dossier, avant toute mise à jour : aucune opération de
    /// Sollicito ne peut alors les tenir. Un reste qu'un autre programme tient reste, jusqu'au lancement suivant.
    /// </summary>
    public static async Task<int> RetirerAsync(DossierDeLaBase dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        // La copie et ses journaux d'un bloc : un journal resté seul désigne la même copie.
        var restes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var nom in LireLesNoms(dossier.Chemin))
        {
            if (CopieDeTravail().Match(nom) is { Success: true } copie)
            {
                restes.Add(Path.Combine(dossier.Chemin, copie.Groups["copie"].Value));
            }
            else if (FichierProvisoire().IsMatch(nom))
            {
                restes.Add(Path.Combine(dossier.Chemin, nom));
            }
        }

        // Les fichiers provisoires d'une copie d'avant, que seule cette installation écrit dans son dossier par défaut ; et le
        // journal d'une copie brute resté sans elle, quand l'arrêt est tombé entre les renommages du journal et de la copie
        // (#28) : la restauration ne s'est pas faite, et ce journal n'est la copie de rien.
        var nomsParDefaut = LireLesNoms(dossier.DossierDesSauvegardes);
        foreach (var nom in nomsParDefaut)
        {
            if (NomsDesCopies.EstUnProvisoireDe(nom, installation: null))
            {
                restes.Add(Path.Combine(dossier.DossierDesSauvegardes, nom));
            }
            else if (NomsDesCopies.EstLeJournalDUneCopieBrute(nom, out var copieBrute) && !nomsParDefaut.Contains(copieBrute, StringComparer.OrdinalIgnoreCase))
            {
                restes.Add(Path.Combine(dossier.DossierDesSauvegardes, copieBrute));
            }
        }

        var retires = 0;
        foreach (var chemin in restes)
        {
            await CopiesVerifiees.EffacerAsync(chemin);
            if (!SuffixesDUnReste.Any(suffixe => File.Exists(chemin + suffixe)))
            {
                retires++;
            }
        }

        return retires;
    }

    // Les noms des fichiers d'un dossier ; aucun s'il ne se lit pas, ou s'il n'existe pas encore.
    private static string[] LireLesNoms(string dossier)
    {
        try
        {
            return Directory.Exists(dossier) ? [.. Directory.EnumerateFiles(dossier).Select(Path.GetFileName).OfType<string>()] : [];
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"^(?<copie>sollicito\.db\.[0-9a-f]{32}\.(migration|restauration))(-journal|-wal|-shm)?$", RegexOptions.CultureInvariant)]
    private static partial Regex CopieDeTravail();

    [GeneratedRegex(@"^(reglages|sauvegardes)\.json\.[0-9a-f]{32}\.provisoire$", RegexOptions.CultureInvariant)]
    private static partial Regex FichierProvisoire();
}
