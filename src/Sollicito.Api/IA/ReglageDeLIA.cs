using Sollicito.Application.IA;
using Sollicito.Infrastructure.IA;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Api.IA;

/// <summary>
/// Le réglage de l'IA (#43, ADR-0016) : le fournisseur choisi, troisième réglage de <c>reglages.json</c>, et sa clé,
/// chiffrée à part par <see cref="GardeDeLaCle"/>. Aucune IA par défaut, et tout fonctionne sans elle : un réglage
/// illisible, une clé absente ou illisible ne cassent rien, l'état le dit, et l'adaptateur « aucune IA » sert.
/// <list type="bullet">
/// <item>« Une clé, une adresse » (ADR-0001, D7) : changer de fournisseur efface d'abord la clé, puis écrit le réglage,
/// sous le verrou des réglages ; une clé qui ne s'efface pas fait refuser le changement.</item>
/// <item>La clé ne sort jamais d'ici, ni vers une réponse ni vers un journal : l'état ne dit que si elle est enregistrée,
/// absente ou illisible.</item>
/// </list>
/// </summary>
public sealed partial class ReglageDeLIA(DossierDeLaBase dossier, GardeDeLaCle garde, ILogger<ReglageDeLIA> journal)
{
    private readonly FichierDesReglages _fichier = new(dossier);

    /// <summary>L'état de l'IA, tel que la section « IA » et la carte du tableau de bord le montrent.</summary>
    public EtatDeLIA LireLEtat()
    {
        var lecture = _fichier.Lire();
        var fournisseur = lecture.FournisseurLisible ? lecture.Reglages.FournisseurEnVigueur : FournisseurDIA.Aucun;
        // Une valeur écrite, même illisible, est un choix : la carte ne revient pas. Un fichier illisible en entier non plus,
        // puisqu'aucun choix ne s'y écrirait.
        var choisi = lecture.Reglages.Fournisseur is not null || !lecture.FournisseurLisible;
        return new EtatDeLIA(fournisseur, choisi, lecture.FournisseurLisible, lecture.FichierLisible, LireLaCle(fournisseur));
    }

    /// <summary>
    /// L'adaptateur qui sert l'IA choisie, ou « aucune IA » avec sa raison. En #43, aucun fournisseur n'est encore branché :
    /// Claude et OpenAI le seront par #48, Ollama par #45.
    /// </summary>
    public IFournisseurDIA Fournisseur()
    {
        var etat = LireLEtat();
        var raison = ChoixDuFournisseurDIA.Raison(etat.Fournisseur, etat.ReglageLisible, etat.Cle);
        return new AucuneIA(raison ?? RaisonDIndisponibilite.PasEncoreBranche);
    }

    /// <summary>
    /// Choisit <paramref name="fournisseur"/>. Un autre fournisseur que celui du fichier efface d'abord la clé ; le même ne
    /// change rien. Refusé sur un fichier des réglages illisible en entier, ou si la clé ne s'efface pas.
    /// </summary>
    public async Task<IssueDuChoixDeLIA> ChoisirAsync(FournisseurDIA fournisseur)
    {
        var cleEffacee = false;
        try
        {
            var issue = await _fichier.EcrireLeFournisseurDIAAsync(fournisseur, async () =>
            {
                var presente = garde.EstPresente;
                var effacee = await garde.EffacerAsync();
                cleEffacee = presente && effacee;
                return effacee;
            });
            switch (issue)
            {
                case IssueDuFournisseurDIA.Change:
                    FournisseurChoisi(fournisseur);
                    if (cleEffacee)
                    {
                        CleEffaceeAuChangement();
                    }

                    return IssueDuChoixDeLIA.Change;
                case IssueDuFournisseurDIA.Inchange:
                    return IssueDuChoixDeLIA.Inchange;
                case IssueDuFournisseurDIA.FichierIllisible:
                    ChoixRefuse("réglages illisibles");
                    return IssueDuChoixDeLIA.FichierIllisible;
                default:
                    ChoixRefuse("clé non effacée");
                    return IssueDuChoixDeLIA.CleNonEffacee;
            }
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            ChoixImpossible(erreur.GetType().Name);
            return IssueDuChoixDeLIA.Impossible;
        }
    }

    /// <summary>
    /// Enregistre la clé saisie pour le fournisseur réglé, à la place de toute autre. Elle doit avoir sa forme
    /// (<see cref="FormeDUneCle"/>), et le fournisseur réglé en prendre une.
    /// </summary>
    public Task<IssueDeLaCle> EnregistrerLaCleAsync(string? saisie) =>
        _fichier.SousLeVerrouAsync(async lecture =>
        {
            if (!lecture.FichierLisible)
            {
                CleRefusee("réglages illisibles");
                return IssueDeLaCle.FichierIllisible;
            }

            var fournisseur = lecture.Reglages.FournisseurEnVigueur;
            if (!lecture.FournisseurLisible || !fournisseur.PrendUneCle())
            {
                CleRefusee("fournisseur sans clé");
                return IssueDeLaCle.SansCle;
            }

            if (FormeDUneCle.Verifier(fournisseur, saisie, out var cle) is { } motif)
            {
                CleRefusee(motif.ToString());
                return IssueDeLaCle.Refusee(motif);
            }

            var remplacee = garde.EstPresente;
            try
            {
                await garde.EnregistrerAsync(fournisseur, cle!);
            }
            catch (Exception erreur) when (erreur is not OperationCanceledException)
            {
                CleNonEnregistree(erreur.GetType().Name);
                return IssueDeLaCle.Impossible;
            }

            if (remplacee)
            {
                CleRemplacee(fournisseur);
            }
            else
            {
                CleEnregistree(fournisseur);
            }

            return IssueDeLaCle.Enregistree;
        });

    /// <summary>Retire la clé enregistrée ; vrai si aucune ne reste, même s'il n'y en avait pas.</summary>
    public Task<bool> RetirerLaCleAsync() =>
        _fichier.SousLeVerrouAsync(async _ =>
        {
            var presente = garde.EstPresente;
            var effacee = await garde.EffacerAsync();
            if (!effacee)
            {
                CleNonRetiree();
            }
            else if (presente)
            {
                CleRetiree();
            }

            return effacee;
        });

    // L'état de la clé du fournisseur réglé : déchiffrée pour savoir si elle se relit, puis oubliée.
    private EtatDeLaCle LireLaCle(FournisseurDIA fournisseur)
    {
        if (!fournisseur.PrendUneCle())
        {
            return EtatDeLaCle.SansObjet;
        }

        var lecture = garde.Lire(fournisseur);
        if (lecture.Etat == EtatDeLaCle.Illisible)
        {
            CleIllisible(lecture.Cause ?? "inconnue");
        }

        return lecture.Etat;
    }

    [LoggerMessage(EventId = 80, Level = LogLevel.Information, Message = "Fournisseur d'IA choisi : {Fournisseur}.")]
    private partial void FournisseurChoisi(FournisseurDIA fournisseur);

    [LoggerMessage(EventId = 81, Level = LogLevel.Information, Message = "Clé d'IA effacée au changement de fournisseur.")]
    private partial void CleEffaceeAuChangement();

    [LoggerMessage(EventId = 82, Level = LogLevel.Information, Message = "Choix de l'IA refusé ({Motif}).")]
    private partial void ChoixRefuse(string motif);

    [LoggerMessage(EventId = 83, Level = LogLevel.Error, Message = "Le choix de l'IA n'a pas pu s'écrire ({TypeDErreur}).")]
    private partial void ChoixImpossible(string typeDErreur);

    [LoggerMessage(EventId = 84, Level = LogLevel.Information, Message = "Clé d'IA enregistrée pour {Fournisseur}.")]
    private partial void CleEnregistree(FournisseurDIA fournisseur);

    [LoggerMessage(EventId = 85, Level = LogLevel.Information, Message = "Clé d'IA remplacée pour {Fournisseur}.")]
    private partial void CleRemplacee(FournisseurDIA fournisseur);

    [LoggerMessage(EventId = 86, Level = LogLevel.Information, Message = "Clé d'IA refusée ({Motif}).")]
    private partial void CleRefusee(string motif);

    [LoggerMessage(EventId = 87, Level = LogLevel.Error, Message = "La clé d'IA n'a pas pu être chiffrée ni écrite ({TypeDErreur}).")]
    private partial void CleNonEnregistree(string typeDErreur);

    [LoggerMessage(EventId = 88, Level = LogLevel.Information, Message = "Clé d'IA retirée.")]
    private partial void CleRetiree();

    [LoggerMessage(EventId = 89, Level = LogLevel.Error, Message = "La clé d'IA n'a pas pu être retirée : son fichier n'a pas pu être effacé.")]
    private partial void CleNonRetiree();

    [LoggerMessage(EventId = 90, Level = LogLevel.Warning, Message = "Clé d'IA illisible ({Cause}) : Sollicito continue sans IA.")]
    private partial void CleIllisible(string cause);
}

/// <summary>L'état de l'IA, sans jamais la clé.</summary>
/// <param name="Fournisseur">Le fournisseur qui s'applique : celui du réglage, ou aucun s'il ne se lit pas.</param>
/// <param name="Choisi">Faux tant qu'aucun choix n'est enregistré : la carte du tableau de bord le propose.</param>
/// <param name="ReglageLisible">Faux si le choix de l'IA ne se lisait pas : aucune IA s'applique alors.</param>
/// <param name="FichierLisible">Faux si le fichier des réglages ne se lisait pas en entier : rien ne s'y écrit.</param>
/// <param name="Cle">La clé du fournisseur qui s'applique : sans objet, aucune, enregistrée ou illisible.</param>
public sealed record EtatDeLIA(FournisseurDIA Fournisseur, bool Choisi, bool ReglageLisible, bool FichierLisible, EtatDeLaCle Cle);

/// <summary>L'issue d'un choix de fournisseur.</summary>
public enum IssueDuChoixDeLIA
{
    /// <summary>Le fournisseur est choisi, et la clé d'un autre effacée.</summary>
    Change,

    /// <summary>C'était déjà le fournisseur choisi : rien ne change.</summary>
    Inchange,

    /// <summary>Le fichier des réglages ne se lit pas en entier : rien n'est écrit.</summary>
    FichierIllisible,

    /// <summary>La clé enregistrée n'a pas pu être effacée : le fournisseur ne change pas.</summary>
    CleNonEffacee,

    /// <summary>Le fichier des réglages n'a pas pu s'écrire.</summary>
    Impossible,
}

/// <summary>L'issue de l'enregistrement d'une clé, et le motif d'un refus de sa forme.</summary>
public sealed class IssueDeLaCle
{
    private IssueDeLaCle(SorteDIssueDeLaCle sorte, MotifDuRefusDUneCle? motif)
    {
        Sorte = sorte;
        Motif = motif;
    }

    /// <summary>La clé est enregistrée.</summary>
    public static IssueDeLaCle Enregistree { get; } = new(SorteDIssueDeLaCle.Enregistree, null);

    /// <summary>Le fournisseur réglé ne prend pas de clé, ou ne se lit pas.</summary>
    public static IssueDeLaCle SansCle { get; } = new(SorteDIssueDeLaCle.SansCle, null);

    /// <summary>Le fichier des réglages ne se lit pas en entier.</summary>
    public static IssueDeLaCle FichierIllisible { get; } = new(SorteDIssueDeLaCle.FichierIllisible, null);

    /// <summary>La clé n'a pas pu être chiffrée ni écrite.</summary>
    public static IssueDeLaCle Impossible { get; } = new(SorteDIssueDeLaCle.Impossible, null);

    /// <summary>La sorte de l'issue.</summary>
    public SorteDIssueDeLaCle Sorte { get; }

    /// <summary>Le motif d'une clé refusée par sa forme.</summary>
    public MotifDuRefusDUneCle? Motif { get; }

    /// <summary>Une clé refusée par sa forme.</summary>
    public static IssueDeLaCle Refusee(MotifDuRefusDUneCle motif) => new(SorteDIssueDeLaCle.Refusee, motif);
}

/// <summary>Les sortes d'issue de l'enregistrement d'une clé.</summary>
public enum SorteDIssueDeLaCle
{
    /// <summary>La clé est enregistrée.</summary>
    Enregistree,

    /// <summary>La clé n'a pas sa forme.</summary>
    Refusee,

    /// <summary>Le fournisseur réglé ne prend pas de clé, ou ne se lit pas.</summary>
    SansCle,

    /// <summary>Le fichier des réglages ne se lit pas en entier.</summary>
    FichierIllisible,

    /// <summary>La clé n'a pas pu être chiffrée ni écrite.</summary>
    Impossible,
}
