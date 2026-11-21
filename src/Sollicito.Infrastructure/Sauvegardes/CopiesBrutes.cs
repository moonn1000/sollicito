using Sollicito.Application.Sauvegardes;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// Des fichiers copiés octet pour octet, sans SQLite (#28, ADR-0012) : la copie de travail d'une sauvegarde à restaurer,
/// et la copie brute d'une base abîmée, que <c>VACUUM INTO</c> ne sait plus copier. Un fichier qu'un autre programme
/// tient un instant se reprend quelques fois ; l'annulation se lit entre deux blocs.
/// </summary>
public static class CopiesBrutes
{
    private const int TailleDUnBloc = 1 << 16;

    /// <summary>
    /// Copie <paramref name="source"/> dans <paramref name="destination"/>, qui ne doit pas exister. La source se lit sans
    /// être jamais ouverte par SQLite : un journal resté à côté d'elle ne s'y rejoue pas, et elle n'est jamais écrite.
    /// </summary>
    public static async Task CopierLeFichierAsync(string source, string destination, CancellationToken annulation)
    {
        FileStream? lecture = null;
        await CopiesVerifiees.ReessayerAsync(
            () => lecture = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, TailleDUnBloc, FileOptions.Asynchronous | FileOptions.SequentialScan),
            annulation);
        await using (lecture!)
        {
            await using var ecriture = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, TailleDUnBloc, FileOptions.Asynchronous);
            await lecture!.CopyToAsync(ecriture, TailleDUnBloc, annulation);
            ecriture.Flush(flushToDisk: true);
        }
    }

    /// <summary>
    /// La copie brute d'une base : son fichier, et son journal WAL s'il en a un, copiés tels quels sous un nom provisoire,
    /// puis renommés. Le journal prend son nom le premier : la copie de la base ne paraît jamais sans le sien. À ne faire
    /// que connexions closes, porte de la base fermée. Une copie manquée retire ses fichiers provisoires.
    /// <paramref name="avantLeRenommage"/> voit les fichiers provisoires écrits.
    /// </summary>
    public static async Task EcrireUneCopieBruteAsync(string baseSource, string destination, CancellationToken annulation, Action? avantLeRenommage = null)
    {
        var dossier = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("Une copie se range dans un dossier.", nameof(destination));
        var journalSource = baseSource + "-wal";
        var provisoire = Path.Combine(dossier, NomsDesCopies.Provisoire(Path.GetFileName(destination)));
        var provisoireDuJournal = File.Exists(journalSource)
            ? Path.Combine(dossier, NomsDesCopies.Provisoire(Path.GetFileName(destination) + "-wal"))
            : null;
        try
        {
            await CopierLeFichierAsync(baseSource, provisoire, annulation);
            if (provisoireDuJournal is not null)
            {
                await CopierLeFichierAsync(journalSource, provisoireDuJournal, annulation);
            }

            avantLeRenommage?.Invoke();
            annulation.ThrowIfCancellationRequested();
            if (provisoireDuJournal is not null)
            {
                await CopiesVerifiees.ReessayerAsync(() => File.Move(provisoireDuJournal, destination + "-wal"), annulation);
            }

            await CopiesVerifiees.ReessayerAsync(() => File.Move(provisoire, destination), annulation);
        }
        finally
        {
            await EffacerSiPresentAsync(provisoire);
            if (provisoireDuJournal is not null)
            {
                await EffacerSiPresentAsync(provisoireDuJournal);
            }
        }
    }

    /// <summary>
    /// Pousse sur le disque ce que le système garde encore en mémoire d'un fichier : une copie d'avant doit survivre à une
    /// panne de courant pendant le remplacement qui la suit.
    /// </summary>
    public static void ForcerSurLeDisque(string fichier)
    {
        using var flux = new FileStream(fichier, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        flux.Flush(flushToDisk: true);
    }

    private static async Task EffacerSiPresentAsync(string fichier)
    {
        try
        {
            await CopiesVerifiees.ReessayerAsync(() => File.Delete(fichier));
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            // Laissé : le lancement suivant retire les fichiers provisoires d'une copie d'avant.
        }
    }
}
