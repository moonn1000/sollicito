using Sollicito.Api.IA;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.Candidatures;
using Sollicito.Application.IA;
using Sollicito.Application.Relances;
using Sollicito.Domain.Candidatures;

namespace Sollicito.Api.Relances;

/// <summary>
/// Le premier jet d'une relance par l'IA (#47, ADR-0019), et le texte relu que la fenêtre hôte ouvrira :
/// <list type="bullet">
/// <item>une demande au fournisseur réglé, par le port (#48), sur un geste de l'utilisateur seulement, qui ne porte que le
/// poste, l'entreprise et la date d'envoi (<see cref="PremierJetDeRelance"/>) ; refusée, sans requête, sans IA prête, pour
/// une relance qui n'est plus à faire, ou sans donnée ;</item>
/// <item>la porte de la base tenue le temps de lire la relance, jamais pendant l'appel : une restauration n'attend les
/// requêtes entrées que dix secondes ;</item>
/// <item>la réponse nettoyée, puis placée entre le salut et la formule de politesse du brouillon de #14 ; jamais écrite
/// dans un journal ;</item>
/// <item>le texte relu, vérifié puis déposé pour l'hôte (<see cref="BrouillonsRelus"/>).</item>
/// </list>
/// </summary>
public sealed partial class PremiersJetsDesRelances(
    IServiceScopeFactory portees,
    TimeProvider horloge,
    PorteDeLaBase porte,
    ReglageDeLIA reglage,
    BrouillonsRelus relus,
    ILogger<PremiersJetsDesRelances> journal)
{
    /// <summary>Le titre d'un premier jet refusé sans requête ; son motif suit, en détail.</summary>
    public const string TitreDuRefus = "Le premier jet n'est pas rédigé.";

    /// <summary>Le motif d'une relance sans donnée à confier à l'IA : rien ne part.</summary>
    public const string SansDonnee = "Cette relance n'a aucune donnée à confier à l'IA.";

    /// <summary>
    /// Demande le premier jet de la relance à faire <paramref name="relance"/> de la candidature <paramref name="candidature"/>.
    /// Seule l'annulation lève une exception.
    /// </summary>
    public async Task<IssueDuPremierJet> RedigerAsync(int candidature, int relance, CancellationToken annulation)
    {
        var adaptateur = reglage.Fournisseur();
        if (adaptateur is AucuneIA aucune)
        {
            var (motif, message) = aucune.Raison switch
            {
                RaisonDIndisponibilite.CleAbsente => ("clé absente", MessagesDeLIA.SansCle),
                RaisonDIndisponibilite.CleIllisible => ("clé illisible", MessagesDeLIA.CleIllisible),
                _ => ("aucun fournisseur en ligne", MessagesDeLIA.SansFournisseurEnLigne),
            };
            PremierJetRefuse(motif);
            return new IssueDuPremierJet.Refuse(message);
        }

        if (!porte.EssayerDEntrer())
        {
            PremierJetRefuse("restauration en cours");
            return new IssueDuPremierJet.PorteFermee();
        }

        CandidatureSuivie? suivie;
        try
        {
            await using var portee = portees.CreateAsyncScope();
            suivie = await portee.ServiceProvider.GetRequiredService<IDepotDesCandidatures>().TrouverAsync(candidature, annulation);
        }
        finally
        {
            porte.Sortir();
        }

        if (suivie is null)
        {
            PremierJetRefuse("candidature introuvable");
            return new IssueDuPremierJet.CandidatureIntrouvable();
        }

        if (suivie.Candidature.RelanceAFaire is not { } aFaire || aFaire.Id != relance)
        {
            PremierJetRefuse("relance pas à faire");
            return new IssueDuPremierJet.Refuse(Candidature.RelancePasAFaire);
        }

        var lue = new RelancePourLePremierJet(
            aFaire.Id,
            suivie.Candidature.Poste,
            suivie.Entreprise.Nom,
            suivie.Candidature.EnvoyeeLe,
            suivie.Destinataires.FirstOrDefault(contact => contact.Id == aFaire.DestinataireId));
        if (PremierJetDeRelance.Demande(lue, horloge.LocalTimeZone) is not { } demande)
        {
            PremierJetRefuse("sans donnée");
            return new IssueDuPremierJet.Refuse(SansDonnee);
        }

        var fournisseur = adaptateur.Fournisseur;
        var reponse = await adaptateur.DemanderAsync(demande, annulation);
        switch (reponse)
        {
            case ReponseDeLIA.Texte texte:
                var paragraphe = PremierJetDeRelance.Nettoyer(texte.Contenu);
                if (paragraphe.Length == 0)
                {
                    PremierJetVide();
                    return Echoue(SorteDEchec.ReponseIllisible, fournisseur);
                }

                return new IssueDuPremierJet.Fait(new PremierJet(Reussi: true, PremierJetDeRelance.Assembler(lue.Destinataire, paragraphe), Echec: null, Message: null));
            case ReponseDeLIA.Echec { Sorte: SorteDEchec.UneDemandeEnCours }:
                PremierJetRefuse("une demande en cours");
                return new IssueDuPremierJet.Refuse(MessagesDeLIA.UneDemandeEnCours);
            case ReponseDeLIA.Echec echec:
                return Echoue(echec.Sorte, fournisseur);
            default:
                // La clé a disparu entre le choix de l'adaptateur et l'appel : retirée, ou devenue illisible.
                var illisible = reponse is ReponseDeLIA.Indisponible { Raison: RaisonDIndisponibilite.CleIllisible };
                PremierJetRefuse(illisible ? "clé illisible" : "clé absente");
                return new IssueDuPremierJet.Refuse(illisible ? MessagesDeLIA.CleIllisible : MessagesDeLIA.SansCle);
        }
    }

    /// <summary>
    /// Vérifie le texte relu de la relance à faire <paramref name="relance"/> de la candidature <paramref name="candidature"/>,
    /// puis le dépose pour la fenêtre hôte. Lu par le dépôt de la requête, sous la porte de la base.
    /// </summary>
    public async Task<IssueDuDepot> DeposerAsync(IDepotDesCandidatures depot, int candidature, int relance, string? saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(depot);
        if (!PremierJetDeRelance.EssayerDeLireLeTexteRelu(saisie, out var texte, out var erreur))
        {
            DepotRefuse("texte refusé");
            return new IssueDuDepot.Refuse(erreur);
        }

        var suivie = await depot.TrouverAsync(candidature, annulation);
        if (suivie is null)
        {
            DepotRefuse("candidature introuvable");
            return new IssueDuDepot.CandidatureIntrouvable();
        }

        if (suivie.Candidature.RelanceAFaire is not { } aFaire || aFaire.Id != relance)
        {
            DepotRefuse("relance pas à faire");
            return new IssueDuDepot.NonPermis(Candidature.RelancePasAFaire);
        }

        var destinataire = suivie.Destinataires.FirstOrDefault(contact => contact.Id == aFaire.DestinataireId);
        if (PremierJetDeRelance.BrouillonRelu(destinataire?.Email, suivie.Candidature.Poste, texte) is null)
        {
            DepotRefuse("lien trop long");
            return new IssueDuDepot.Refuse(PremierJetDeRelance.LienTropLong);
        }

        relus.Deposer(aFaire.Id, texte);
        return new IssueDuDepot.Depose();
    }

    private IssueDuPremierJet Echoue(SorteDEchec sorte, FournisseurDIA fournisseur) =>
        new IssueDuPremierJet.Fait(new PremierJet(Reussi: false, Texte: null, sorte, sorte.Message(fournisseur)));

    [LoggerMessage(EventId = 120, Level = LogLevel.Information, Message = "Premier jet d'une relance refusé ({Motif}).")]
    private partial void PremierJetRefuse(string motif);

    [LoggerMessage(EventId = 121, Level = LogLevel.Warning, Message = "Premier jet d'une relance : la réponse de l'IA était vide une fois nettoyée.")]
    private partial void PremierJetVide();

    [LoggerMessage(EventId = 122, Level = LogLevel.Information, Message = "Texte relu d'un brouillon refusé ({Motif}).")]
    private partial void DepotRefuse(string motif);
}

/// <summary>
/// Le premier jet d'une relance, tel que la page le reçoit : réussi, avec le texte à relire, ou l'échec et son texte ;
/// jamais la réponse brute du fournisseur.
/// </summary>
/// <param name="Reussi">Vrai si l'IA a répondu par un texte lisible.</param>
/// <param name="Texte">Le texte à relire : le salut, le paragraphe de l'IA nettoyé, la formule de politesse.</param>
/// <param name="Echec">Comment la demande a échoué, si elle a échoué.</param>
/// <param name="Message">Ce que l'écran dit d'un échec.</param>
public sealed record PremierJet(bool Reussi, string? Texte, SorteDEchec? Echec, string? Message)
{
    /// <summary>Un texte qui ne montre pas le premier jet.</summary>
    public override string ToString() => Reussi ? "Premier jet rédigé" : $"Premier jet échoué : {Echec}";
}

/// <summary>L'issue d'une demande de premier jet.</summary>
public abstract record IssueDuPremierJet
{
    private IssueDuPremierJet()
    {
    }

    /// <summary>La demande est partie : son résultat, réussi ou non.</summary>
    public sealed record Fait(PremierJet Resultat) : IssueDuPremierJet;

    /// <summary>La candidature n'existe pas : rien n'est parti.</summary>
    public sealed record CandidatureIntrouvable : IssueDuPremierJet;

    /// <summary>La demande est refusée sans requête, pour ce motif lisible.</summary>
    public sealed record Refuse(string Motif) : IssueDuPremierJet;

    /// <summary>Une restauration de la base est en cours : rien n'est parti.</summary>
    public sealed record PorteFermee : IssueDuPremierJet;
}

/// <summary>L'issue du dépôt d'un texte relu.</summary>
public abstract record IssueDuDepot
{
    private IssueDuDepot()
    {
    }

    /// <summary>Le texte est déposé pour la fenêtre hôte.</summary>
    public sealed record Depose : IssueDuDepot;

    /// <summary>Le texte est refusé par sa règle, pour ce motif, qui ne le reprend jamais.</summary>
    public sealed record Refuse(string Motif) : IssueDuDepot;

    /// <summary>La candidature n'existe pas.</summary>
    public sealed record CandidatureIntrouvable : IssueDuDepot;

    /// <summary>La relance n'est plus à faire, ou n'est pas de cette candidature, pour ce motif lisible.</summary>
    public sealed record NonPermis(string Motif) : IssueDuDepot;
}

/// <summary>Le texte relu d'un premier jet, tel que la page l'envoie. Son texte ne le montre pas.</summary>
/// <param name="Texte">Le texte du brouillon, que l'utilisateur a relu et corrigé.</param>
public sealed record SaisieDuBrouillonRelu(string? Texte)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Texte = "texte";
    }

    /// <summary>Un texte qui ne montre pas le brouillon.</summary>
    public override string ToString() => "Saisie d'un brouillon relu";
}
