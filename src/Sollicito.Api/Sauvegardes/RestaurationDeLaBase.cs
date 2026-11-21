using Microsoft.Data.Sqlite;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// La restauration d'une copie gardée (#28, ADR-0012), que seule la fenêtre hôte demande, en mémoire, après le oui de
/// l'utilisateur dans son dialogue : aucune route de l'API ne restaure. Dans l'ordre, une seule à la fois, sous le
/// sémaphore des copies :
/// <list type="number">
/// <item>la copie choisie se retrouve sur le disque, sous le nom et l'instant que la liste montrait ;</item>
/// <item>elle se recopie dans le dossier de données : seule cette copie de travail s'ouvre par SQLite, se vérifie, et se
/// migre si elle est plus ancienne (<see cref="VerificationDUneCopie"/>) ;</item>
/// <item>la porte de la base se ferme : les routes et les brouillons sont refusés, et ce qui était entré ressort ;</item>
/// <item>la base en cours se copie avant d'être remplacée : vérifiée si elle est saine, brute si elle est abîmée ;</item>
/// <item>la copie de travail remplace le contenu de la base par l'API de sauvegarde de SQLite, en une transaction ;</item>
/// <item>la porte se rouvre, et l'état des sauvegardes se republie.</item>
/// </list>
/// Un échec, à toute étape, laisse la base telle qu'avant. Les journaux ne disent que l'étape, le motif et le type d'une
/// erreur : ni chemin, ni nom de copie, ni donnée.
/// </summary>
public sealed partial class RestaurationDeLaBase(
    SauvegardesDeLaBase sauvegardes,
    PorteDeLaBase porte,
    DossierDeLaBase dossier,
    TimeProvider horloge,
    IEtapesDeLaRestauration etapes,
    ILogger<RestaurationDeLaBase> journal)
{
    /// <summary>Le temps laissé à la vérification de la copie de travail, migration comprise.</summary>
    public static readonly TimeSpan DelaiDeLaVerification = TimeSpan.FromMinutes(2);

    /// <summary>Le temps laissé aux requêtes en cours pour finir, porte fermée.</summary>
    public static readonly TimeSpan DelaiDesRequetesEnCours = TimeSpan.FromSeconds(10);

    /// <summary>Le temps laissé à une autre opération sur les copies (une copie du jour sur un partage lent) pour finir.</summary>
    public static readonly TimeSpan AttenteDesCopies = TimeSpan.FromSeconds(30);

    private int _enCours;

    /// <summary>
    /// Prépare la restauration de la copie que désigne cet identifiant, avant la confirmation : la copie telle que la
    /// liste la montre, et l'état de la base en cours, lu par <c>PRAGMA quick_check</c> sans l'écrire. Un identifiant
    /// d'une autre forme, ou qui ne désigne aucune copie de la liste, est refusé.
    /// </summary>
    public async Task<PreparationDeLaRestauration> PreparerAsync(string copie, CancellationToken annulation)
    {
        var trouvee = IdentifiantsDesCopies.AEnLaForme(copie)
            ? sauvegardes.Etat.Copies.FirstOrDefault(gardee => gardee.Id == copie)
            : null;
        if (trouvee is null)
        {
            return new PreparationDeLaRestauration(null, Refuser(IssueDeLaRestauration.Introuvable));
        }

        if (Volatile.Read(ref _enCours) != 0 || !porte.EssayerDEntrer())
        {
            return new PreparationDeLaRestauration(null, Refuser(IssueDeLaRestauration.Occupee));
        }

        try
        {
            var saine = await CopieDeLaBase.SeRelitSaineAsync(dossier.Fichier, annulation);
            return new PreparationDeLaRestauration(
                new DemandeDeRestauration(trouvee.Id, trouvee.Nom, trouvee.Lieu, trouvee.Sorte, trouvee.FaiteLe, saine ? EtatDeLaBase.Saine : EtatDeLaBase.Abimee),
                null);
        }
        catch (Exception erreur) when (erreur is SqliteException or IOException or UnauthorizedAccessException)
        {
            RestaurationImpossible(journal, "Preparation", erreur.GetType().Name);
            return new PreparationDeLaRestauration(null, IssueDeLaRestauration.Impossible);
        }
        finally
        {
            porte.Sortir();
        }
    }

    /// <summary>
    /// Restaure la copie, une fois le oui donné. L'annulation (la fenêtre qui se ferme) interrompt la préparation et la
    /// copie d'avant ; un remplacement commencé va au bout. Ne lève rien d'autre que l'annulation.
    /// </summary>
    public async Task<IssueDeLaRestauration> RestaurerAsync(DemandeDeRestauration demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        if (Interlocked.CompareExchange(ref _enCours, 1, 0) != 0)
        {
            return Refuser(IssueDeLaRestauration.Occupee);
        }

        try
        {
            var issue = await sauvegardes.RestaurerSeuleAsync(() => MenerAsync(demande, annulation), AttenteDesCopies, annulation);
            return issue == IssueDeLaRestauration.Occupee ? Refuser(issue) : issue;
        }
        finally
        {
            Volatile.Write(ref _enCours, 0);
        }
    }

    private async Task<IssueDeLaRestauration> MenerAsync(DemandeDeRestauration demande, CancellationToken annulation)
    {
        var (source, refus) = sauvegardes.RetrouverLaCopie(demande);
        if (refus is { } motif)
        {
            return Refuser(motif);
        }

        var copieDeTravail = Path.Combine(dossier.Chemin, RestesDUnArret.NomDUneCopieARestaurer());
        try
        {
            VerdictDUneCopie verdict;
            try
            {
                await CopiesBrutes.CopierLeFichierAsync(source!, copieDeTravail, annulation);
                etapes.Atteinte(EtapeDeLaRestauration.CopieDeTravailEcrite);
                // La copie se relit après sa recopie : un instant qui a changé pendant elle dit qu'elle a été réécrite, et la
                // copie de travail n'est peut-être plus celle que l'utilisateur a confirmée.
                if (sauvegardes.RetrouverLaCopie(demande).Refus is { } changement)
                {
                    return Refuser(changement);
                }

                using var borne = new CancellationTokenSource(DelaiDeLaVerification, horloge);
                using var liee = CancellationTokenSource.CreateLinkedTokenSource(annulation, borne.Token);
                verdict = await VerificationDUneCopie.VerifierEtMettreAJourAsync(copieDeTravail, dossier.Fichier, liee.Token);
            }
            catch (Exception erreur) when (erreur is FileNotFoundException or DirectoryNotFoundException)
            {
                return Refuser(IssueDeLaRestauration.Introuvable);
            }
            catch (OperationCanceledException) when (!annulation.IsCancellationRequested)
            {
                RestaurationImpossible(journal, "Verification", nameof(TimeoutException));
                return IssueDeLaRestauration.Impossible;
            }
            catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
            {
                RestaurationImpossible(journal, "CopieDeTravail", erreur.GetType().Name);
                return IssueDeLaRestauration.Impossible;
            }

            switch (verdict.Sorte)
            {
                case SorteDeVerdict.Abimee:
                    return Refuser(IssueDeLaRestauration.Abimee);
                case SorteDeVerdict.Etrangere:
                    return Refuser(IssueDeLaRestauration.Etrangere);
                case SorteDeVerdict.PlusRecente:
                    return Refuser(IssueDeLaRestauration.PlusRecente);
            }

            etapes.Atteinte(EtapeDeLaRestauration.CopieDeTravailVerifiee);
            if (!await porte.FermerAsync(DelaiDesRequetesEnCours, horloge, annulation))
            {
                return Refuser(IssueDeLaRestauration.Occupee);
            }

            try
            {
                etapes.Atteinte(EtapeDeLaRestauration.PorteFermee);
                return await CopierPuisRemplacerAsync(demande, copieDeTravail, verdict, annulation);
            }
            finally
            {
                porte.Ouvrir();
            }
        }
        finally
        {
            await CopiesVerifiees.EffacerAsync(copieDeTravail);
        }
    }

    // Porte fermée : l'état de la base, la copie d'avant, puis le remplacement.
    private async Task<IssueDeLaRestauration> CopierPuisRemplacerAsync(DemandeDeRestauration demande, string copieDeTravail, VerdictDUneCopie verdict, CancellationToken annulation)
    {
        bool saine;
        try
        {
            saine = await CopieDeLaBase.SeRelitSaineAsync(dossier.Fichier, annulation);
        }
        catch (Exception erreur) when (erreur is SqliteException or IOException or UnauthorizedAccessException)
        {
            RestaurationImpossible(journal, "EtatDeLaBase", erreur.GetType().Name);
            return IssueDeLaRestauration.Impossible;
        }

        if ((saine ? EtatDeLaBase.Saine : EtatDeLaBase.Abimee) != demande.EtatDeLaBase)
        {
            return Refuser(IssueDeLaRestauration.BaseChangee);
        }

        var instant = InstantLibre(horloge.GetUtcNow());
        try
        {
            Directory.CreateDirectory(dossier.DossierDesSauvegardes);
            if (saine)
            {
                var copieDAvant = Path.Combine(dossier.DossierDesSauvegardes, NomsDesCopies.AvantRestauration(instant));
                await CopiesVerifiees.EcrireAsync(dossier.Fichier, copieDAvant, annulation, async (fichier, jeton) =>
                {
                    etapes.Atteinte(EtapeDeLaRestauration.CopieDAvantEcrite);
                    return await CopieDeLaBase.EstSaineAsync(fichier, jeton);
                });
                await CopiesBrutes.ForcerSurLeDisqueAsync(copieDAvant, annulation);
            }
            else
            {
                var brute = Path.Combine(dossier.DossierDesSauvegardes, NomsDesCopies.AvantRestaurationBrute(instant));
                await CopiesBrutes.EcrireUneCopieBruteAsync(dossier.Fichier, brute, annulation, () => etapes.Atteinte(EtapeDeLaRestauration.CopieDAvantEcrite));
            }
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            RestaurationImpossible(journal, "CopieDAvant", erreur.GetType().Name);
            return IssueDeLaRestauration.CopieDAvantImpossible;
        }

        CopieDAvantFaite(journal, saine ? "verifiee" : "brute");
        etapes.Atteinte(EtapeDeLaRestauration.CopieDAvantFaite);

        var premiereTranche = true;
        try
        {
            // Sans annulation : un remplacement commencé va au bout, en une transaction.
            await CopieDeLaBase.RemplacerAsync(copieDeTravail, dossier.Fichier, CancellationToken.None, () =>
            {
                if (premiereTranche)
                {
                    premiereTranche = false;
                    etapes.Atteinte(EtapeDeLaRestauration.PendantLeRemplacement);
                }
            });
        }
        catch (Exception erreur) when (erreur is SqliteException or IOException or UnauthorizedAccessException)
        {
            RestaurationImpossible(journal, "Remplacement", erreur.GetType().Name);
            return IssueDeLaRestauration.Impossible;
        }

        await GarderLeModeWalAsync();
        etapes.Atteinte(EtapeDeLaRestauration.BaseRemplacee);
        RestaurationFaite(journal, demande.Sorte, verdict.MigrationsAppliquees);
        return saine ? IssueDeLaRestauration.Restauree : IssueDeLaRestauration.RestaureeDepuisUneBaseAbimee;
    }

    // L'instant qui nomme la copie d'avant, sans jamais reprendre le nom d'une autre : une horloge qui recule, ou deux
    // restaurations dans la même milliseconde, ne remplacent pas une copie d'avant gardée. Porte fermée et sous le
    // sémaphore des copies, personne d'autre n'en écrit.
    private DateTimeOffset InstantLibre(DateTimeOffset instant)
    {
        while (File.Exists(Path.Combine(dossier.DossierDesSauvegardes, NomsDesCopies.AvantRestauration(instant)))
            || File.Exists(Path.Combine(dossier.DossierDesSauvegardes, NomsDesCopies.AvantRestaurationBrute(instant))))
        {
            instant = instant.AddMilliseconds(1);
        }

        return instant;
    }

    // L'API de sauvegarde garde le mode WAL de la base ; s'il manquait, le lancement suivant l'y remettrait.
    private async Task GarderLeModeWalAsync()
    {
        try
        {
            if (!string.Equals(await CopieDeLaBase.ModeDuJournalAsync(dossier.Fichier, CancellationToken.None), "wal", StringComparison.OrdinalIgnoreCase))
            {
                await CopieDeLaBase.MettreEnModeWalAsync(dossier.Fichier, CancellationToken.None);
            }
        }
        catch (SqliteException erreur)
        {
            ModeWalNonRemis(journal, erreur.GetType().Name);
        }
    }

    private IssueDeLaRestauration Refuser(IssueDeLaRestauration motif)
    {
        RestaurationRefusee(journal, motif);
        return motif;
    }

    [LoggerMessage(EventId = 50, Level = LogLevel.Information, Message = "Base restaurée depuis une copie ({Sorte}, {Migrations} migration(s) appliquée(s) à la copie).")]
    private static partial void RestaurationFaite(ILogger journal, SorteDeCopie sorte, int migrations);

    [LoggerMessage(EventId = 51, Level = LogLevel.Warning, Message = "Restauration refusée ({Motif}) : la base n'a pas changé.")]
    private static partial void RestaurationRefusee(ILogger journal, IssueDeLaRestauration motif);

    [LoggerMessage(EventId = 52, Level = LogLevel.Error, Message = "Restauration impossible ({Etape}, {TypeDErreur}) : la base n'a pas changé.")]
    private static partial void RestaurationImpossible(ILogger journal, string etape, string typeDErreur);

    [LoggerMessage(EventId = 53, Level = LogLevel.Information, Message = "Copie d'avant la restauration faite ({Sorte}).")]
    private static partial void CopieDAvantFaite(ILogger journal, string sorte);

    [LoggerMessage(EventId = 54, Level = LogLevel.Warning, Message = "Base restaurée, mais pas remise en mode WAL ({TypeDErreur}) : le lancement suivant l'y remettra.")]
    private static partial void ModeWalNonRemis(ILogger journal, string typeDErreur);
}
