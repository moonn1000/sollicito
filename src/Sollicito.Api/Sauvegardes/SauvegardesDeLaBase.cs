using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// Les copies du jour de la base (#26, ADR-0008) : une par jour d'utilisation, dans le dossier des sauvegardes, qui garde
/// les sept dernières de l'installation. La copie du jour se fait au lancement si elle manque, se refait à la fermeture,
/// et se fait le lendemain si Sollicito reste ouvert (<see cref="SauvegardesAutomatiques"/>) ; « Sauvegarder maintenant »
/// la refait aussitôt.
/// <list type="bullet">
/// <item>Le dossier choisi ne vient que du dialogue de la fenêtre hôte (<see cref="ChoisirLeDossierAsync"/>) : aucune
/// route de l'API ne reçoit un chemin. Il ne devient celui des sauvegardes qu'après une copie réussie, qui prouve qu'on
/// peut y écrire.</item>
/// <item>Un dossier choisi qui manque n'est jamais recréé, et aucune copie ne part ailleurs : l'état le dit, jusqu'à la
/// prochaine copie réussie. Seul le dossier par défaut, dans le dossier de données, se crée au besoin.</item>
/// <item>Les copies, les choix et la lecture du dossier passent un à un. L'état se tient en mémoire : le lire ne touche
/// pas au dossier.</item>
/// <item>Les journaux ne citent aucun chemin, ni celui d'un dossier, ni celui d'une copie : seulement ce qui s'est passé,
/// et le type d'une erreur.</item>
/// </list>
/// </summary>
public sealed partial class SauvegardesDeLaBase
{
    /// <summary>L'intervalle entre deux vérifications de la copie du jour, par la minuterie de l'API.</summary>
    public static readonly TimeSpan PeriodeDesVerifications = TimeSpan.FromHours(1);

    /// <summary>Le temps au-delà duquel la copie de la fermeture est abandonnée : elle ne retient pas la fermeture.</summary>
    public static readonly TimeSpan DelaiDeLaCopieDeFermeture = TimeSpan.FromSeconds(10);

    private readonly DossierDeLaBase _base;
    private readonly FichierDesReglages _reglages;
    private readonly TimeProvider _horloge;
    private readonly ILogger<SauvegardesDeLaBase> _journal;
    private readonly SemaphoreSlim _uneALaFois = new(1, 1);
    private readonly string _installation;

    // Lus et changés sous _uneALaFois.
    private bool _charges;
    private string? _dossierChoisi;
    private bool _reglageIllisible;
    private DateTime? _derniereCopie;
    private EchecDUneCopie? _echec;

    // L'état publié, remplacé d'un bloc : sa lecture ne prend aucun verrou.
    private EtatDesSauvegardes _etat;

    /// <summary>Les copies de la base de ce dossier, datées par cette horloge.</summary>
    public SauvegardesDeLaBase(DossierDeLaBase dossierDeLaBase, TimeProvider horloge, ILogger<SauvegardesDeLaBase> journal)
    {
        ArgumentNullException.ThrowIfNull(dossierDeLaBase);
        _base = dossierDeLaBase;
        _reglages = new FichierDesReglages(dossierDeLaBase);
        _horloge = horloge;
        _journal = journal;
        _installation = NomsDesCopies.Installation(Environment.MachineName, dossierDeLaBase.Chemin);
        _etat = new EtatDesSauvegardes(
            dossierDeLaBase.DossierDesSauvegardes,
            ParDefaut: true,
            dossierDeLaBase.DossierDesSauvegardes,
            DerniereCopie: null,
            Copies: [],
            ReglageIllisible: false,
            Echec: null,
            Verifie: false);
    }

    /// <summary>L'identifiant de cette installation, qui nomme ses copies du jour.</summary>
    public string Installation => _installation;

    /// <summary>L'état des sauvegardes, lu en mémoire.</summary>
    public EtatDesSauvegardes Etat => Volatile.Read(ref _etat);

    /// <summary>
    /// Fait la copie du jour si le dossier ne l'a pas encore : au lancement, puis à chaque heure, pour le lendemain d'une
    /// séance restée ouverte et pour un dossier qui revient. Ne lève rien, hors annulation.
    /// </summary>
    public async Task FaireLaCopieDuJourSiBesoinAsync(CancellationToken annulation)
    {
        await _uneALaFois.WaitAsync(annulation);
        try
        {
            Charger();
            var dossier = DossierDesCopies;
            var noms = LireLesNoms(dossier);
            // Après un échec, chaque essai refait la copie : un dossier revenu efface ainsi l'alerte.
            if (noms is null || _echec is not null || PlanDesCopies.ACopier(noms, _installation, Aujourdhui))
            {
                await CopierAsync(dossier, annulation);
            }

            Publier();
        }
        finally
        {
            _uneALaFois.Release();
        }
    }

    /// <summary>
    /// Refait la copie du jour dans le dossier des sauvegardes : « Sauvegarder maintenant », « Réessayer », et la copie de
    /// la fermeture. Ne lève rien, hors annulation : l'état dit si la copie a réussi.
    /// </summary>
    public async Task<EtatDesSauvegardes> RefaireLaCopieDuJourAsync(CancellationToken annulation)
    {
        await _uneALaFois.WaitAsync(annulation);
        try
        {
            Charger();
            await CopierAsync(DossierDesCopies, annulation);
            return Publier();
        }
        finally
        {
            _uneALaFois.Release();
        }
    }

    /// <summary>
    /// La copie de la fermeture : la copie du jour refaite, pour qu'elle garde la fin de la séance. Elle est abandonnée
    /// au-delà de <see cref="DelaiDeLaCopieDeFermeture"/>, et la copie d'avant reste : rend faux dans ce cas.
    /// </summary>
    public async Task<bool> FaireLaCopieDeFermetureAsync(CancellationToken annulation)
    {
        try
        {
            // Sur un autre fil : les appels de SQLite ne rendent la main qu'une fois finis, et une base occupée les
            // retient jusqu'au délai de ses commandes. Le délai de la fermeture court ainsi dès le début de la copie.
            await Task.Run(() => RefaireLaCopieDuJourAsync(CancellationToken.None), CancellationToken.None)
                .WaitAsync(DelaiDeLaCopieDeFermeture, _horloge, annulation);
            return true;
        }
        catch (Exception erreur) when (erreur is TimeoutException or OperationCanceledException)
        {
            CopieDeFermetureAbandonnee(_journal);
            return false;
        }
    }

    /// <summary>
    /// Le dossier que l'utilisateur a choisi dans le dialogue de la fenêtre hôte. Sollicito y copie aussitôt la base :
    /// réussie, la copie en fait le dossier des sauvegardes ; manquée, l'ancien reste. Les copies de l'ancien y restent.
    /// </summary>
    public async Task<IssueDuChoix> ChoisirLeDossierAsync(string dossier, CancellationToken annulation)
    {
        if (!Path.IsPathFullyQualified(dossier))
        {
            DossierRefuse(_journal, "chemin relatif");
            return IssueDuChoix.Refuse;
        }

        var choisi = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dossier));
        return EstLeDossierParDefaut(choisi)
            ? await RevenirAuDossierParDefautAsync(annulation)
            : await AdopterAsync(choisi, annulation);
    }

    /// <summary>Revient au dossier par défaut, sur le disque de la base, après une copie qui y réussit.</summary>
    public Task<IssueDuChoix> RevenirAuDossierParDefautAsync(CancellationToken annulation) => AdopterAsync(dossier: null, annulation);

    // Le dossier des copies du jour : le dossier choisi, ou celui par défaut.
    private string DossierDesCopies => _dossierChoisi ?? _base.DossierDesSauvegardes;

    private DateOnly Aujourdhui => PlanDesCopies.JourLocal(_horloge.GetUtcNow(), _horloge.LocalTimeZone);

    private async Task<IssueDuChoix> AdopterAsync(string? dossier, CancellationToken annulation)
    {
        await _uneALaFois.WaitAsync(annulation);
        try
        {
            Charger();
            var cible = dossier ?? _base.DossierDesSauvegardes;
            var motif = await EssayerDeCopierAsync(cible, annulation);
            if (motif is not null)
            {
                DossierRefuse(_journal, motif.Value.ToString());
                return IssueDuChoix.Refuse;
            }

            try
            {
                _reglages.Ecrire(new ReglagesDeLApplication(dossier));
            }
            catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
            {
                // La copie est faite, mais le choix ne se garderait pas : l'ancien dossier reste.
                ReglageNonEcrit(_journal, erreur.GetType().Name);
                return IssueDuChoix.Refuse;
            }

            _dossierChoisi = dossier;
            _reglageIllisible = false;
            ReussirLaCopie();
            DossierChoisi(_journal, dossier is null);
            Publier();
            return IssueDuChoix.Choisi;
        }
        finally
        {
            _uneALaFois.Release();
        }
    }

    // Copie dans le dossier des copies du jour, et note la réussite ou l'échec dans l'état.
    private async Task CopierAsync(string dossier, CancellationToken annulation)
    {
        var motif = await EssayerDeCopierAsync(dossier, annulation);
        if (motif is null)
        {
            ReussirLaCopie();
            return;
        }

        // Le début de la série d'échecs : l'alerte dit depuis quand rien n'a réussi.
        _echec = new EchecDUneCopie(motif.Value, _echec?.Depuis ?? _horloge.GetUtcNow().UtcDateTime);
    }

    // L'instant de la copie se relit sur son fichier, quand l'état se publie.
    private void ReussirLaCopie() => _echec = null;

    // La copie du jour dans ce dossier, puis la rotation ; le motif de l'échec, ou rien si elle a réussi.
    private async Task<MotifDEchec?> EssayerDeCopierAsync(string dossier, CancellationToken annulation)
    {
        try
        {
            if (string.Equals(dossier, _base.DossierDesSauvegardes, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(dossier);
            }
            else if (!Directory.Exists(dossier))
            {
                // Jamais recréé : une autre clé a pu prendre la même lettre.
                CopieManquee(_journal, nameof(MotifDEchec.DossierIntrouvable));
                return MotifDEchec.DossierIntrouvable;
            }

            await RetirerLesProvisoiresAsync(dossier);
            await CopiesVerifiees.EcrireAsync(_base.Fichier, Path.Combine(dossier, NomsDesCopies.DuJour(_installation, Aujourdhui)), annulation);
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            CopieManquee(_journal, erreur.GetType().Name);
            return erreur is DirectoryNotFoundException ? MotifDEchec.DossierIntrouvable : MotifDEchec.CopieImpossible;
        }

        var effacees = 0;
        foreach (var nom in PlanDesCopies.AEffacer(LireLesNoms(dossier) ?? [], _installation))
        {
            try
            {
                File.Delete(Path.Combine(dossier, nom));
                effacees++;
            }
            catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
            {
                // Une copie ancienne qui reste n'enlève rien aux autres : la rotation suivante la reprendra.
                CopieAncienneGardee(_journal, erreur.GetType().Name);
            }
        }

        CopieFaite(_journal, effacees);
        return null;
    }

    // Les fichiers provisoires que cette installation a laissés, par une copie interrompue : ils ne désignent aucune copie.
    private async Task RetirerLesProvisoiresAsync(string dossier)
    {
        foreach (var nom in LireLesNoms(dossier) ?? [])
        {
            if (NomsDesCopies.EstUnProvisoireDe(nom, _installation))
            {
                await CopiesVerifiees.EffacerAsync(Path.Combine(dossier, nom));
            }
        }
    }

    // Lit le réglage du dossier une fois, au premier usage.
    private void Charger()
    {
        if (_charges)
        {
            return;
        }

        var lecture = _reglages.Lire();
        _dossierChoisi = lecture.Reglages.DossierDesSauvegardes is { } dossier && !EstLeDossierParDefaut(dossier)
            ? Path.TrimEndingDirectorySeparator(dossier)
            : null;
        _reglageIllisible = !lecture.Lisible;
        if (_reglageIllisible)
        {
            ReglageIllisible(_journal);
        }

        _charges = true;
    }

    private bool EstLeDossierParDefaut(string dossier) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(dossier),
            Path.TrimEndingDirectorySeparator(_base.DossierDesSauvegardes),
            StringComparison.OrdinalIgnoreCase);

    // Relit les copies gardées, puis publie l'état. Un dossier qui ne se lit plus garde la date de la dernière copie lue.
    private EtatDesSauvegardes Publier()
    {
        var copies = new List<CopieGardee>();
        var dossier = DossierDesCopies;
        var noms = LireLesNoms(dossier);
        copies.AddRange(Garder(dossier, PlanDesCopies.CopiesDuJour(noms ?? [], _installation), SorteDeCopie.DuJour));
        if (noms is not null)
        {
            _derniereCopie = copies.Select(copie => (DateTime?)copie.FaiteLe).Max();
        }

        copies.AddRange(Garder(_base.DossierDesSauvegardes, PlanDesCopies.CopiesDAvantUneMiseAJour(LireLesNoms(_base.DossierDesSauvegardes) ?? []), SorteDeCopie.AvantMiseAJour));
        var etat = new EtatDesSauvegardes(
            dossier,
            ParDefaut: _dossierChoisi is null,
            _base.DossierDesSauvegardes,
            _derniereCopie,
            copies,
            _reglageIllisible,
            _echec,
            Verifie: true);
        Volatile.Write(ref _etat, etat);
        return etat;
    }

    private static IEnumerable<CopieGardee> Garder(string dossier, IEnumerable<CopieNommee> copies, SorteDeCopie sorte)
    {
        foreach (var copie in copies)
        {
            DateTime faiteLe;
            try
            {
                faiteLe = File.GetLastWriteTimeUtc(Path.Combine(dossier, copie.Nom));
            }
            catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // Une copie effacée entre la lecture du dossier et celle de sa date rend le 1er janvier 1601 : elle n'est plus.
            if (faiteLe > DateTime.FromFileTimeUtc(0))
            {
                yield return new CopieGardee(copie.Nom, sorte, DateTime.SpecifyKind(faiteLe, DateTimeKind.Utc));
            }
        }
    }

    // Les noms des fichiers du dossier, ou null s'il ne se lit pas : absent, débranché, refusé.
    private static string[]? LireLesNoms(string dossier)
    {
        try
        {
            return [.. Directory.EnumerateFiles(dossier).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "Copie du jour faite ; {Effacees} copie(s) ancienne(s) effacée(s) par la rotation.")]
    private static partial void CopieFaite(ILogger journal, int effacees);

    [LoggerMessage(EventId = 31, Level = LogLevel.Warning, Message = "Copie du jour manquée ({Motif}).")]
    private static partial void CopieManquee(ILogger journal, string motif);

    [LoggerMessage(EventId = 32, Level = LogLevel.Warning, Message = "Une copie ancienne n'a pas pu être effacée ({TypeDErreur}) : la rotation suivante la reprendra.")]
    private static partial void CopieAncienneGardee(ILogger journal, string typeDErreur);

    [LoggerMessage(EventId = 33, Level = LogLevel.Information, Message = "Dossier des sauvegardes choisi (par défaut : {ParDefaut}).")]
    private static partial void DossierChoisi(ILogger journal, bool parDefaut);

    [LoggerMessage(EventId = 34, Level = LogLevel.Warning, Message = "Dossier des sauvegardes refusé ({Motif}) : l'ancien reste.")]
    private static partial void DossierRefuse(ILogger journal, string motif);

    [LoggerMessage(EventId = 35, Level = LogLevel.Warning, Message = "Réglages illisibles : les copies vont dans le dossier par défaut.")]
    private static partial void ReglageIllisible(ILogger journal);

    [LoggerMessage(EventId = 36, Level = LogLevel.Warning, Message = "Réglages non écrits ({TypeDErreur}) : l'ancien dossier des sauvegardes reste.")]
    private static partial void ReglageNonEcrit(ILogger journal, string typeDErreur);

    [LoggerMessage(EventId = 37, Level = LogLevel.Warning, Message = "Copie de la fermeture abandonnée : la copie d'avant reste.")]
    private static partial void CopieDeFermetureAbandonnee(ILogger journal);
}
