using Sollicito.Application.Sauvegardes;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// Une copie vérifiée de la base (ADR-0008) : écrite sous un nom provisoire, relue par <c>PRAGMA integrity_check</c>, puis
/// renommée à son nom final, qu'elle remplace. Une copie manquée retire son fichier provisoire, et ne touche ni à la copie
/// qui portait déjà ce nom, ni à aucune autre.
/// </summary>
public static class CopiesVerifiees
{
    // ERROR_SHARING_VIOLATION et ERROR_LOCK_VIOLATION : un autre programme tient le fichier un instant, par exemple un
    // antivirus qui examine un fichier tout juste écrit.
    private const int PartageRefuse = unchecked((int)0x80070020);
    private const int VerrouRefuse = unchecked((int)0x80070021);

    private const int Essais = 20;
    private static readonly TimeSpan EntreDeuxEssais = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Copie la base dans <paramref name="destination"/>, son nom final, une fois la copie vérifiée par
    /// <paramref name="verifier"/>, <see cref="CopieDeLaBase.EstSaineAsync"/> par défaut. Une copie qui ne se vérifie pas
    /// lève <see cref="InvalidDataException"/>, sans toucher à la destination.
    /// </summary>
    public static async Task EcrireAsync(
        string baseSource,
        string destination,
        CancellationToken annulation,
        Func<string, CancellationToken, Task<bool>>? verifier = null)
    {
        verifier ??= CopieDeLaBase.EstSaineAsync;
        var dossier = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("Une copie se range dans un dossier.", nameof(destination));
        var provisoire = Path.Combine(dossier, NomsDesCopies.Provisoire(Path.GetFileName(destination)));
        try
        {
            await CopieDeLaBase.CopierAsync(baseSource, provisoire, annulation);
            if (!await verifier(provisoire, annulation))
            {
                throw new InvalidDataException("La copie ne se relit pas comme une base saine.");
            }

            await ReessayerAsync(() => File.Move(provisoire, destination, overwrite: true));
        }
        finally
        {
            await EffacerAsync(provisoire);
        }
    }

    /// <summary>
    /// Efface les fichiers provisoires d'un dossier que désigne <paramref name="estAMoi"/> : ceux qu'une copie interrompue
    /// a laissés, par un arrêt brutal ou une panne de courant.
    /// </summary>
    public static async Task RetirerLesProvisoiresAsync(string dossier, Func<string, bool> estAMoi)
    {
        ArgumentNullException.ThrowIfNull(estAMoi);
        string[] noms;
        try
        {
            noms = [.. Directory.EnumerateFiles(dossier).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var nom in noms.Where(estAMoi))
        {
            await EffacerAsync(Path.Combine(dossier, nom));
        }
    }

    /// <summary>
    /// Efface une base et les fichiers que SQLite tient à côté d'elle (journaux), s'ils existent. Au mieux : un dossier
    /// débranché, ou un fichier qu'un autre programme tient encore après quelques essais, ne masque pas l'erreur qui a
    /// fait échouer la copie ; le fichier provisoire restant ne désigne aucune copie, et la copie suivante le retire.
    /// </summary>
    public static async Task EffacerAsync(string fichier)
    {
        foreach (var chemin in new[] { fichier, fichier + "-journal", fichier + "-wal", fichier + "-shm" })
        {
            try
            {
                await ReessayerAsync(() => File.Delete(chemin));
            }
            catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
            {
                // Laissé : voir ci-dessus.
            }
        }
    }

    /// <summary>Une opération sur un fichier, reprise tant qu'un autre programme le tient, quelques fois au plus.</summary>
    public static async Task ReessayerAsync(Action operation)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                operation();
                return;
            }
            catch (IOException erreur) when (essai < Essais && erreur.HResult is PartageRefuse or VerrouRefuse)
            {
                await Task.Delay(EntreDeuxEssais);
            }
        }
    }
}
