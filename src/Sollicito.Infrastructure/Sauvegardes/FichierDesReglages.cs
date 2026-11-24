using System.Collections.Concurrent;
using System.Text;
using Sollicito.Application.IA;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// Le fichier des réglages, <c>reglages.json</c>, dans le dossier de données, hors de la base (ADR-0008). Il s'écrit d'un
/// bloc : un fichier provisoire, puis un renommage, pour qu'un arrêt en pleine écriture ne laisse jamais un fichier à
/// moitié écrit. Chaque écriture ne change qu'un réglage, et relit le fichier sous un verrou que partagent tous ses
/// écrivains, sauvegardes (#26), relances (#14) et IA (#43) : aucune n'efface ce qu'une autre vient d'écrire. La clé d'IA,
/// rangée à part dans <c>cle-ia.json</c>, s'écrit et s'efface sous ce même verrou (ADR-0016).
/// </summary>
public sealed class FichierDesReglages(DossierDeLaBase dossier)
{
    // Un verrou par fichier, commun à toutes les instances qui l'écrivent.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Verrous = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lit les réglages ; un fichier absent rend ceux par défaut, un fichier illisible aussi, en le disant.</summary>
    public LectureDesReglages Lire()
    {
        var texte = LireLeTexte(out var present);
        return present && texte is null ? LectureDesReglages.IllisibleEnEntier : ReglagesDeLApplication.Lire(texte);
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

    /// <summary>
    /// Écrit le fournisseur d'IA (#43), en gardant le reste du fichier. « Une clé, une adresse » (ADR-0001, D7 ;
    /// ADR-0016) : un autre fournisseur que celui du fichier passe d'abord par <paramref name="effacerLaCle"/>, et ne
    /// s'écrit que si la clé est effacée. Le même fournisseur ne change rien, et la clé reste. Comme le délai, il ne
    /// s'écrit pas sur un fichier illisible en entier. Une écriture impossible lève son erreur d'entrée-sortie.
    /// </summary>
    public async Task<IssueDuFournisseurDIA> EcrireLeFournisseurDIAAsync(FournisseurDIA fournisseur, Func<Task<bool>> effacerLaCle)
    {
        ArgumentNullException.ThrowIfNull(effacerLaCle);
        var verrou = LeVerrou();
        await verrou.WaitAsync();
        try
        {
            var (avant, present) = await LireAvantDEcrireAsync();
            var lecture = Relire(avant, present);
            if (!lecture.FichierLisible)
            {
                return IssueDuFournisseurDIA.FichierIllisible;
            }

            if (lecture.FournisseurLisible && lecture.Reglages.Fournisseur == fournisseur)
            {
                return IssueDuFournisseurDIA.Inchange;
            }

            if (!await effacerLaCle())
            {
                return IssueDuFournisseurDIA.CleNonEffacee;
            }

            await EcrireLeTexteAsync(ReglagesDeLApplication.AvecLeFournisseurDIA(avant, fournisseur));
            return IssueDuFournisseurDIA.Change;
        }
        finally
        {
            verrou.Release();
        }
    }

    /// <summary>
    /// Relit les réglages sous le verrou de leurs écrivains, puis lance <paramref name="operation"/>, qu'aucune écriture
    /// des réglages ne croise : l'enregistrement et le retrait de la clé d'IA (ADR-0016).
    /// </summary>
    public async Task<T> SousLeVerrouAsync<T>(Func<LectureDesReglages, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var verrou = LeVerrou();
        await verrou.WaitAsync();
        try
        {
            var (avant, present) = await LireAvantDEcrireAsync();
            return await operation(Relire(avant, present));
        }
        finally
        {
            verrou.Release();
        }
    }

    private async Task<bool> EcrireAsync(Func<string?, string> changer, bool surUnFichierIllisible)
    {
        var verrou = LeVerrou();
        await verrou.WaitAsync();
        try
        {
            var (avant, present) = await LireAvantDEcrireAsync();
            if (!surUnFichierIllisible && present && (avant is null || !ReglagesDeLApplication.Lire(avant).FichierLisible))
            {
                return false;
            }

            await EcrireLeTexteAsync(changer(avant));
            return true;
        }
        finally
        {
            verrou.Release();
        }
    }

    // Le verrou du fichier, commun à toutes les instances qui l'écrivent.
    private SemaphoreSlim LeVerrou() => Verrous.GetOrAdd(Path.GetFullPath(dossier.FichierDesReglages), _ => new SemaphoreSlim(1, 1));

    // Les réglages d'un texte lu avant une écriture : présent mais illisible, il l'est en entier.
    private static LectureDesReglages Relire(string? avant, bool present) =>
        present && avant is null ? LectureDesReglages.IllisibleEnEntier : ReglagesDeLApplication.Lire(avant);

    // D'un bloc : un fichier provisoire, puis un renommage, repris tant qu'un autre programme tient le fichier un instant.
    private async Task EcrireLeTexteAsync(string texte)
    {
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

/// <summary>L'issue de l'écriture du fournisseur d'IA (#43).</summary>
public enum IssueDuFournisseurDIA
{
    /// <summary>Le fournisseur est écrit, et la clé d'un autre fournisseur effacée.</summary>
    Change,

    /// <summary>C'était déjà le fournisseur du fichier : rien ne change, et la clé reste.</summary>
    Inchange,

    /// <summary>Le fichier des réglages ne se lit pas en entier : rien n'est écrit.</summary>
    FichierIllisible,

    /// <summary>La clé enregistrée n'a pas pu être effacée : le fournisseur ne change pas.</summary>
    CleNonEffacee,
}
