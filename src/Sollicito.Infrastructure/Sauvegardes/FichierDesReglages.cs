using System.Collections.Concurrent;
using System.Text;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// Le fichier des réglages, <c>reglages.json</c>, dans le dossier de données, hors de la base (ADR-0008). Il s'écrit d'un
/// bloc : un fichier provisoire, puis un renommage, pour qu'un arrêt en pleine écriture ne laisse jamais un fichier à
/// moitié écrit. Chaque écriture ne change qu'un réglage, et relit le fichier sous un verrou que partagent tous ses
/// écrivains, sauvegardes (#26) et relances (#14) : aucune n'efface ce qu'une autre vient d'écrire.
/// </summary>
public sealed class FichierDesReglages(DossierDeLaBase dossier)
{
    // Un verrou par fichier, commun à toutes les instances qui l'écrivent.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Verrous = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lit les réglages ; un fichier absent rend ceux par défaut, un fichier illisible aussi, en le disant.</summary>
    public LectureDesReglages Lire()
    {
        var texte = LireLeTexte(out var present);
        return present && texte is null
            ? new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: false, DelaiLisible: false, FichierLisible: false)
            : ReglagesDeLApplication.Lire(texte);
    }

    /// <summary>
    /// Écrit le dossier des sauvegardes (<c>null</c> pour le dossier par défaut), en gardant le reste du fichier lisible
    /// d'avant. Un fichier illisible en entier se réécrit avec ce seul réglage (ADR-0008). Le renommage se reprend tant
    /// qu'un autre programme, comme un antivirus, tient le fichier un instant.
    /// </summary>
    public async Task EcrireLeDossierAsync(string? dossierDesSauvegardes) =>
        await EcrireAsync(texte => ReglagesDeLApplication.AvecLeDossier(texte, dossierDesSauvegardes), surUnFichierIllisible: true);

    /// <summary>
    /// Écrit le délai par défaut des relances (<c>null</c> pour celui d'usine), en gardant le reste du fichier. Rend faux,
    /// sans rien écrire, si le fichier existe mais ne se lit pas en entier : le réécrire ferait oublier le dossier des
    /// sauvegardes perdu, et tomber l'alerte qui le dit ; seul le choix d'un dossier le réécrit.
    /// </summary>
    public Task<bool> EcrireLeDelaiDesRelancesAsync(int? jours) =>
        EcrireAsync(texte => ReglagesDeLApplication.AvecLeDelaiDesRelances(texte, jours), surUnFichierIllisible: false);

    private async Task<bool> EcrireAsync(Func<string?, string> changer, bool surUnFichierIllisible)
    {
        var verrou = Verrous.GetOrAdd(Path.GetFullPath(dossier.FichierDesReglages), _ => new SemaphoreSlim(1, 1));
        await verrou.WaitAsync();
        try
        {
            var (avant, present) = await LireAvantDEcrireAsync();
            if (!surUnFichierIllisible && present && (avant is null || !ReglagesDeLApplication.Lire(avant).FichierLisible))
            {
                return false;
            }

            var texte = changer(avant);
            var provisoire = $"{dossier.FichierDesReglages}.{Guid.NewGuid():N}.provisoire";
            try
            {
                File.WriteAllText(provisoire, texte, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await CopiesVerifiees.ReessayerAsync(() => File.Move(provisoire, dossier.FichierDesReglages, overwrite: true));
            }
            finally
            {
                File.Delete(provisoire);
            }

            return true;
        }
        finally
        {
            verrou.Release();
        }
    }

    // Le texte du fichier, ou null s'il est absent, trop gros ou illisible (présent dit alors s'il existe).
    private string? LireLeTexte(out bool present)
    {
        present = new FileInfo(dossier.FichierDesReglages).Exists;
        if (!present)
        {
            return null;
        }

        try
        {
            return LireLeTexteOuLever();
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    // La lecture d'une écriture : reprise tant qu'un autre programme, comme un antivirus, tient le fichier un instant, comme
    // le renommage qui la suit. Sans cela, le fichier tenu vaudrait « illisible », et le choix d'un dossier le réécrirait
    // sans les autres réglages, le délai des relances compris (#88, point 7). Un fichier qui reste tenu, ou qui ne se lit
    // pas, vaut « illisible », comme pour toute lecture.
    private async Task<(string? Texte, bool Present)> LireAvantDEcrireAsync()
    {
        if (!new FileInfo(dossier.FichierDesReglages).Exists)
        {
            return (null, false);
        }

        try
        {
            string? texte = null;
            await CopiesVerifiees.ReessayerAsync(() => texte = LireLeTexteOuLever());
            return (texte, true);
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return (null, true);
        }
    }

    // Le texte du fichier, ou null s'il est trop gros ; une erreur d'entrée-sortie, ou un texte qui n'est pas de l'UTF-8,
    // se lève.
    private string? LireLeTexteOuLever()
    {
        var fichier = new FileInfo(dossier.FichierDesReglages);
        if (fichier.Length > ReglagesDeLApplication.TailleMaximale)
        {
            return null;
        }

        return File.ReadAllText(fichier.FullName, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
    }
}
