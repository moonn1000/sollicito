using System.Security.Cryptography;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.Export;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Api.Export;

/// <summary>
/// L'export de toutes les données de la base (#49, ADR-0020), que seule la fenêtre hôte demande, en mémoire, avec le
/// fichier que l'utilisateur a choisi dans son dialogue : aucune route de l'API n'exporte ni ne reçoit un chemin. Dans
/// l'ordre :
/// <list type="number">
/// <item>le nom choisi finit par l'extension du format, et n'est pas dans le dossier de données de Sollicito ;</item>
/// <item>l'export entre dans la porte de la base : pendant une restauration, il est refusé ;</item>
/// <item>un fichier provisoire, dans le dossier choisi, reçoit l'export, lu dans un seul instantané de la base et écrit
/// au fil de la lecture, puis il est poussé sur le disque ;</item>
/// <item>il prend le nom choisi. Il ne remplace un fichier que si l'utilisateur l'a accepté dans le dialogue, et s'il est
/// encore celui que le dialogue a vu.</item>
/// </list>
/// Un échec, à toute étape, ou la fermeture de la fenêtre, efface le provisoire : rien ne reste sous le nom choisi, et un
/// fichier qu'on allait remplacer reste intact. Les journaux disent l'issue, le nombre de lignes par entité, la durée, et
/// le type d'une erreur : jamais un chemin, un nom de fichier, une donnée ni le message d'une exception.
/// </summary>
public sealed partial class ExportDesDonnees(
    ISourceDeLExport source,
    PorteDeLaBase porte,
    DossierDeLaBase dossier,
    TimeProvider horloge,
    IEtapesDeLExport etapes,
    ILogger<ExportDesDonnees> journal)
{
    /// <summary>Le début du nom du fichier provisoire, suivi d'une marque tirée au hasard.</summary>
    public const string PrefixeDuProvisoire = "sollicito-export-";

    /// <summary>La fin du nom du fichier provisoire : un nom reconnaissable, si un arrêt brutal le laisse.</summary>
    public const string SuffixeDuProvisoire = ".partiel";

    // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL, ERROR_DISK_QUOTA_EXCEEDED.
    private static readonly int[] PlaceQuiManque = [unchecked((int)0x80070070), unchecked((int)0x80070027), unchecked((int)0x8007050F)];

    // ERROR_ALREADY_EXISTS, ERROR_FILE_EXISTS : au renommage, un fichier de ce nom est apparu.
    private static readonly int[] FichierExistant = [unchecked((int)0x800700B7), unchecked((int)0x80070050)];

    // ERROR_PATH_NOT_FOUND, ERROR_NOT_READY, ERROR_BAD_NETPATH, ERROR_DEV_NOT_EXIST, ERROR_NETNAME_DELETED, ERROR_BAD_NET_NAME.
    private static readonly int[] DossierDisparu =
    [
        unchecked((int)0x80070003), unchecked((int)0x80070015), unchecked((int)0x80070035), unchecked((int)0x80070037),
        unchecked((int)0x80070040), unchecked((int)0x80070043),
    ];

    /// <summary>
    /// Exporte toute la base dans ce fichier, choisi dans le dialogue de la fenêtre. <paramref name="aRemplacer"/> est le
    /// fichier existant que l'utilisateur a accepté d'y remplacer, tel que le dialogue l'a vu : sans lui, aucun fichier n'est
    /// remplacé, même apparu entre-temps ; avec lui, seul ce fichier-là l'est, encore inchangé juste avant le renommage
    /// (#118). L'annulation, la fenêtre qui se ferme, interrompt l'export, qui n'écrit alors rien. Ne lève rien.
    /// </summary>
    public async Task<IssueDeLExport> ExporterAsync(FormatDExport format, string chemin, FichierVu? aRemplacer, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(chemin);
        if (!FormatDeLExport.AExtensionDe(format, chemin))
        {
            return Refuser(format, IssueDeLExport.Extension, "Extension");
        }

        if (!Path.IsPathFullyQualified(chemin))
        {
            return Refuser(format, IssueDeLExport.Impossible, "CheminRelatif");
        }

        var cible = Path.GetFullPath(chemin);
        if (EstDansLeDossierDeDonnees(cible))
        {
            return Refuser(format, IssueDeLExport.DossierDeDonnees, "DossierDeDonnees");
        }

        if (!porte.EssayerDEntrer())
        {
            return Refuser(format, IssueDeLExport.Occupe, "RestaurationEnCours");
        }

        var debut = horloge.GetTimestamp();
        string? provisoire = null;
        var etape = "Ouverture";
        try
        {
            var dossierChoisi = Path.GetDirectoryName(cible) ?? throw new IOException("Le fichier choisi n'a pas de dossier.");
            var nomDuProvisoire = Path.Combine(dossierChoisi, NomDUnProvisoire());
            var maintenant = horloge.GetUtcNow();
            ComptesDeLExport comptes;
            await using (var flux = new FileStream(nomDuProvisoire, FileMode.CreateNew, FileAccess.Write, FileShare.None, FormatDeLExport.Tampon, FileOptions.Asynchronous))
            {
                // Le provisoire existe : seul lui s'efface ensuite, et jamais un fichier qu'un autre aurait mis là.
                provisoire = nomDuProvisoire;
                etape = "Ecriture";
                await using (var instantane = await source.OuvrirAsync(annulation))
                {
                    comptes = format switch
                    {
                        FormatDExport.Json => await EcrivainJson.EcrireAsync(instantane, flux, maintenant.UtcDateTime, etapes.Atteinte, annulation),
                        FormatDExport.Csv => await EcrivainCsv.EcrireAsync(instantane, flux, maintenant.UtcDateTime, horloge.LocalTimeZone, etapes.Atteinte, annulation),
                        _ => throw new ArgumentOutOfRangeException(nameof(format)),
                    };
                }

                await flux.FlushAsync(annulation);
                flux.Flush(flushToDisk: true);
            }

            etapes.Atteinte(EtapeDeLExport.FichierEcrit);
            etape = "Renommage";
            // Une fois l'export interrompu, le provisoire ne prend plus son nom ; un renommage lancé, immédiat, va au bout. Le
            // fichier à remplacer se compare juste avant chaque essai : un fichier changé depuis le dialogue, même pendant
            // l'attente d'un autre programme qui le tenait, n'est plus celui que l'utilisateur a accepté de remplacer.
            var change = false;
            await CopiesVerifiees.ReessayerAsync(
                () =>
                {
                    if (aRemplacer is not null && FichierVu.Lire(cible) is { } present && present != aRemplacer)
                    {
                        change = true;
                        return;
                    }

                    File.Move(provisoire, cible, overwrite: aRemplacer is not null);
                },
                annulation);
            if (change)
            {
                ExportImpossible(journal, format, etape, "FichierChange", IssueDeLExport.Existe, Duree(debut));
                return IssueDeLExport.Existe;
            }

            provisoire = null;
            ExportFait(
                journal,
                format,
                comptes.Entreprises,
                comptes.Contacts,
                comptes.Offres,
                comptes.Candidatures,
                comptes.Relances,
                comptes.Evenements,
                Duree(debut),
                aRemplacer is not null);
            return IssueDeLExport.Exporte;
        }
        catch (OperationCanceledException) when (annulation.IsCancellationRequested)
        {
            ExportInterrompu(journal, format);
            return IssueDeLExport.Annule;
        }
        catch (Exception erreur)
        {
            // Toute erreur, hors annulation : celle de la base comme celle du disque. Son message, qui citerait le chemin, ne
            // s'écrit pas.
            var issue = IssueDUneErreur(erreur, etape);
            ExportImpossible(journal, format, etape, erreur.GetType().Name, issue, Duree(debut));
            return issue;
        }
        finally
        {
            if (provisoire is not null)
            {
                await EffacerLeProvisoireAsync(format, provisoire);
            }

            porte.Sortir();
        }
    }

    /// <summary>Note que l'utilisateur a fermé le dialogue sans choisir de fichier : rien n'a été écrit.</summary>
    public void NoterLAnnulation(FormatDExport format) => ExportAnnule(journal, format);

    /// <summary>
    /// Note que la fenêtre s'est fermée sans attendre la fin d'un export interrompu : son fichier provisoire a pu rester
    /// dans le dossier choisi.
    /// </summary>
    public void NoterLaFermetureAvantLaFin(FormatDExport format) => FermetureAvantLaFin(journal, format);

    /// <summary>
    /// Note que la fenêtre s'est fermée pendant le dialogue, avant que l'export ne commence : rien n'a été écrit.
    /// </summary>
    public void NoterLInterruption(FormatDExport format) => ExportInterrompu(journal, format);

    // Le dossier de données, sous-dossiers compris, après normalisation : un oui à l'écrasement y remplacerait la base, les
    // réglages ou la clé.
    private bool EstDansLeDossierDeDonnees(string cible)
    {
        var donnees = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dossier.Chemin));
        var comparee = SansPrefixeDePeripherique(cible);
        return string.Equals(comparee, donnees, StringComparison.OrdinalIgnoreCase)
            || comparee.StartsWith(donnees + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // Un chemin de périphérique (« \\?\C:\… », « \\.\C:\… », « \\?\UNC\serveur\… ») désigne le même fichier que sans son
    // préfixe, que GetFullPath garde tel quel : il se compare sans lui, normalisé.
    private static string SansPrefixeDePeripherique(string chemin)
    {
        foreach (var prefixe in new[] { @"\\?\", @"\\.\" })
        {
            if (chemin.StartsWith(prefixe, StringComparison.Ordinal))
            {
                var reste = chemin[prefixe.Length..];
                return Path.GetFullPath(reste.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + reste[4..] : reste);
            }
        }

        return chemin;
    }

    private static string NomDUnProvisoire() =>
        PrefixeDuProvisoire + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4)) + SuffixeDuProvisoire;

    private static IssueDeLExport IssueDUneErreur(Exception erreur, string etape) => erreur switch
    {
        UnauthorizedAccessException => IssueDeLExport.AccesRefuse,
        DirectoryNotFoundException or DriveNotFoundException => IssueDeLExport.DossierIntrouvable,
        IOException { HResult: var code } when PlaceQuiManque.Contains(code) => IssueDeLExport.PlaceManquante,
        IOException { HResult: var code } when etape == "Renommage" && FichierExistant.Contains(code) => IssueDeLExport.Existe,
        IOException { HResult: var code } when DossierDisparu.Contains(code) => IssueDeLExport.DossierIntrouvable,
        _ => IssueDeLExport.Impossible,
    };

    private async Task EffacerLeProvisoireAsync(FormatDExport format, string provisoire)
    {
        try
        {
            await CopiesVerifiees.ReessayerAsync(() => File.Delete(provisoire));
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            ProvisoireReste(journal, format, erreur.GetType().Name);
        }
    }

    private long Duree(long debut) => (long)horloge.GetElapsedTime(debut).TotalMilliseconds;

    private IssueDeLExport Refuser(FormatDExport format, IssueDeLExport issue, string motif)
    {
        ExportRefuse(journal, format, motif);
        return issue;
    }

    [LoggerMessage(EventId = 130, Level = LogLevel.Information, Message = "Export {Format} fait : {Entreprises} entreprise(s), {Contacts} contact(s), {Offres} offre(s), {Candidatures} candidature(s), {Relances} relance(s) et {Evenements} événement(s), en {Duree} ms ; fichier existant remplacé : {Remplace}.")]
    private static partial void ExportFait(ILogger journal, FormatDExport format, int entreprises, int contacts, int offres, int candidatures, int relances, int evenements, long duree, bool remplace);

    [LoggerMessage(EventId = 131, Level = LogLevel.Information, Message = "Export {Format} annulé dans le dialogue : rien n'a été écrit.")]
    private static partial void ExportAnnule(ILogger journal, FormatDExport format);

    [LoggerMessage(EventId = 132, Level = LogLevel.Warning, Message = "Export {Format} refusé ({Motif}) : rien n'a été écrit.")]
    private static partial void ExportRefuse(ILogger journal, FormatDExport format, string motif);

    [LoggerMessage(EventId = 133, Level = LogLevel.Error, Message = "Export {Format} impossible ({Etape}, {TypeDErreur}, {Issue}), en {Duree} ms : rien n'a été écrit sous le nom choisi.")]
    private static partial void ExportImpossible(ILogger journal, FormatDExport format, string etape, string typeDErreur, IssueDeLExport issue, long duree);

    [LoggerMessage(EventId = 134, Level = LogLevel.Warning, Message = "Export {Format} interrompu par la fermeture de la fenêtre : rien n'a été écrit sous le nom choisi.")]
    private static partial void ExportInterrompu(ILogger journal, FormatDExport format);

    [LoggerMessage(EventId = 135, Level = LogLevel.Warning, Message = "Le fichier provisoire d'un export {Format} n'a pas pu être effacé ({TypeDErreur}) : il reste dans le dossier choisi.")]
    private static partial void ProvisoireReste(ILogger journal, FormatDExport format, string typeDErreur);

    [LoggerMessage(EventId = 136, Level = LogLevel.Warning, Message = "La fenêtre s'est fermée avant la fin d'un export {Format} interrompu : son fichier provisoire a pu rester dans le dossier choisi.")]
    private static partial void FermetureAvantLaFin(ILogger journal, FormatDExport format);
}
