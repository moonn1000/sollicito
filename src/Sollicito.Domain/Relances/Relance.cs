namespace Sollicito.Domain.Relances;

/// <summary>
/// Une relance prévue d'une candidature (modèle de domaine, « Relances »). Elle n'existe que par sa candidature, qui
/// seule la planifie, la change ou la clôt, et qui n'en a qu'une à faire à la fois. Son échéance est une date du
/// calendrier, enregistrée telle quelle : corriger ensuite la date d'envoi ne la déplace pas. Son destinataire est un
/// contact de l'entreprise de la candidature, ou personne.
/// </summary>
public sealed class Relance
{
    /// <summary>Le nombre de jours qui suivent aujourd'hui où une relance à faire est « de la semaine ».</summary>
    public const int JoursDeLaSemaine = 7;

    // Pour Entity Framework Core, qui lit une relance enregistrée.
    private Relance(DateOnly echeance, EtatDeRelance etat, int? destinataireId)
    {
        Echeance = echeance;
        Etat = etat;
        DestinataireId = destinataireId;
    }

    /// <summary>L'identifiant technique, donné par la base.</summary>
    public int Id { get; private set; }

    /// <summary>Le jour où relancer, sans heure ni fuseau.</summary>
    public DateOnly Echeance { get; private set; }

    /// <summary>À faire, faite ou abandonnée.</summary>
    public EtatDeRelance Etat { get; private set; }

    /// <summary>L'identifiant du contact à qui l'adresser, ou rien : le brouillon s'ouvre alors sans destinataire.</summary>
    public int? DestinataireId { get; private set; }

    /// <summary>Vrai tant qu'elle n'est ni faite ni abandonnée.</summary>
    public bool EstAFaire => Etat == EtatDeRelance.AFaire;

    /// <summary>Vrai pour une relance à faire dont l'échéance tombe aujourd'hui ou avant, en date locale.</summary>
    public bool EstEchueLe(DateOnly aujourdhui) => EstAFaire && EstUneEcheanceEchue(Echeance, aujourdhui);

    /// <summary>
    /// Vrai pour une relance à faire dont l'échéance tombe dans les <see cref="JoursDeLaSemaine"/> jours qui suivent
    /// aujourd'hui : demain compris, aujourd'hui non (elle est alors échue).
    /// </summary>
    public bool EstDeLaSemaineDu(DateOnly aujourdhui) => EstAFaire && EstUneEcheanceDeLaSemaine(Echeance, aujourdhui);

    /// <summary>Vrai pour une échéance qui tombe aujourd'hui ou avant : celle d'une relance échue.</summary>
    public static bool EstUneEcheanceEchue(DateOnly echeance, DateOnly aujourdhui) => echeance <= aujourdhui;

    /// <summary>Vrai pour une échéance qui tombe dans les <see cref="JoursDeLaSemaine"/> jours qui suivent aujourd'hui.</summary>
    public static bool EstUneEcheanceDeLaSemaine(DateOnly echeance, DateOnly aujourdhui) =>
        echeance > aujourdhui && echeance <= FinDeLaSemaine(aujourdhui);

    /// <summary>Le dernier jour « de la semaine » : aujourd'hui plus <see cref="JoursDeLaSemaine"/> jours.</summary>
    public static DateOnly FinDeLaSemaine(DateOnly aujourdhui) => aujourdhui.AddDays(JoursDeLaSemaine);

    /// <summary>Une relance à faire, que la candidature planifie après avoir vérifié ses règles.</summary>
    internal static Relance Planifiee(DateOnly echeance, int? destinataireId)
    {
        VerifierLeDestinataire(destinataireId);
        return new Relance(echeance, EtatDeRelance.AFaire, destinataireId);
    }

    /// <summary>Change l'échéance et le destinataire d'une relance à faire.</summary>
    internal void Changer(DateOnly echeance, int? destinataireId)
    {
        VerifierQuElleEstAFaire();
        VerifierLeDestinataire(destinataireId);
        Echeance = echeance;
        DestinataireId = destinataireId;
    }

    /// <summary>
    /// Retire le destinataire d'une relance à faire, quand la candidature change d'entreprise (#25) : il n'est plus un
    /// contact de son entreprise. Une relance close garde le sien.
    /// </summary>
    internal void RetirerLeDestinataire()
    {
        VerifierQuElleEstAFaire();
        DestinataireId = null;
    }

    /// <summary>Marque la relance faite ou abandonnée, pour de bon.</summary>
    internal void Clore(EtatDeRelance issue)
    {
        if (issue is not (EtatDeRelance.Faite or EtatDeRelance.Abandonnee))
        {
            throw new ArgumentOutOfRangeException(nameof(issue), issue, "Une relance se clôt faite ou abandonnée.");
        }

        VerifierQuElleEstAFaire();
        Etat = issue;
    }

    private void VerifierQuElleEstAFaire()
    {
        if (!EstAFaire)
        {
            throw new InvalidOperationException("Une relance faite ou abandonnée ne change plus.");
        }
    }

    private static void VerifierLeDestinataire(int? destinataireId)
    {
        if (destinataireId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinataireId), destinataireId, "Un destinataire est un contact enregistré.");
        }
    }
}
