using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Relances;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Une candidature envoyée à une entreprise, pour un poste (modèle de domaine, « Statuts d'une candidature »). Elle
/// part toujours du statut Envoyée, avec sa date et son heure d'envoi, gardées en UTC et jamais dans le futur. Elle
/// désigne son entreprise par son identifiant : l'entreprise vit à part.
/// <para>
/// C'est la racine de son agrégat : ses événements ne s'ajoutent que par elle, qui tient ainsi ses règles. Son statut
/// ne change que par un passage du diagramme d'états ou par la correction du dernier changement, et chaque changement
/// entre dans l'historique. L'historique se fait par ajouts seulement.
/// </para>
/// <para>
/// Son entreprise, son poste et sa date d'envoi se corrigent (#25), sans toucher ni au statut ni à l'historique. Comme
/// l'historique en vigueur commence à l'envoi, la date d'envoi ne passe jamais après son premier événement en vigueur.
/// Une étape se corrige aussi, par ajout : sa correction la désigne, et la remplace comme version en vigueur.
/// </para>
/// <para>
/// Ses relances (#14) font aussi partie de son agrégat : elle en planifie une à la fois, sur une candidature en cours,
/// et chaque relance faite ou abandonnée entre dans l'historique. Clore la candidature abandonne sa relance à faire ; la
/// rattacher à une autre entreprise en retire le destinataire.
/// </para>
/// <para>
/// Faite depuis une offre (« Postuler », #38), elle désigne cette offre par son identifiant, pour toujours : l'offre vit
/// à part, et ni sa correction ni la modification de la candidature ne défont le lien. Une offre fait l'objet d'au plus
/// une candidature.
/// </para>
/// </summary>
public sealed class Candidature
{
    /// <summary>Les mots qui désignent le poste dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuPoste = new("Indiquez le poste.", "Le poste");

    /// <summary>Le motif du refus d'une correction quand aucun changement n'est à corriger.</summary>
    public const string RienACorriger = "Aucun changement de statut n'est à corriger : la candidature est à son statut de départ.";

    /// <summary>Le motif du refus d'une relance sur une candidature close.</summary>
    public const string RelanceSurCandidatureClose = "La candidature est close : elle ne reçoit plus de relance.";

    /// <summary>Le motif du refus d'une seconde relance à faire.</summary>
    public const string RelanceDejaAFaire =
        "La candidature a déjà une relance à faire : changez-la, marquez-la faite ou abandonnez-la d'abord.";

    /// <summary>Le motif du refus d'une action sur une relance qui n'est plus à faire, ou qui n'est pas de cette candidature.</summary>
    public const string RelancePasAFaire =
        "Cette relance n'est plus à faire : elle a déjà été marquée faite ou abandonnée, ou la fiche a changé entre-temps.";

    // Dans l'ordre d'enregistrement : celui de la base pour les événements relus (Entity Framework Core les y range,
    // par identifiant), puis ceux ajoutés depuis.
    private readonly List<EvenementDeCandidature> _evenements = [];

    // Dans l'ordre de leur planification, comme les événements.
    private readonly List<Relance> _relances = [];

    // Pour Entity Framework Core, qui lit une candidature enregistrée.
    private Candidature(int entrepriseId, string poste, DateTime envoyeeLe, StatutDeCandidature statut)
    {
        EntrepriseId = entrepriseId;
        Poste = poste;
        EnvoyeeLe = envoyeeLe;
        Statut = statut;
    }

    /// <summary>L'identifiant technique, donné par la base.</summary>
    public int Id { get; private set; }

    /// <summary>L'identifiant de l'entreprise à qui la candidature est envoyée.</summary>
    public int EntrepriseId { get; private set; }

    /// <summary>L'identifiant de l'offre d'où elle vient (#38), ou rien pour une candidature saisie sans offre (#11).</summary>
    public int? OffreId { get; private set; }

    /// <summary>Le poste visé.</summary>
    public string Poste { get; private set; }

    /// <summary>L'instant de l'envoi, en UTC.</summary>
    public DateTime EnvoyeeLe { get; private set; }

    /// <summary>
    /// Le statut : Envoyée à l'enregistrement, puis le nouveau statut du dernier changement, correction comprise
    /// (modèle de domaine, « Règle de cohérence »).
    /// </summary>
    public StatutDeCandidature Statut { get; private set; }

    /// <summary>Les événements, dans l'ordre de leur enregistrement.</summary>
    public IReadOnlyList<EvenementDeCandidature> Evenements => _evenements;

    /// <summary>
    /// L'historique tel qu'il se lit : par <see cref="EvenementDeCandidature.SurvenuLe"/>, du plus ancien au plus
    /// récent ; à égalité, dans l'ordre d'enregistrement.
    /// </summary>
    public IEnumerable<EvenementDeCandidature> Historique => _evenements.OrderBy(evenement => evenement.SurvenuLe);

    /// <summary>Les statuts où la candidature peut passer depuis son statut présent.</summary>
    public IReadOnlyList<StatutDeCandidature> PassagesPermis => PassagesDeStatut.Depuis(Statut);

    /// <summary>Vrai pour une candidature close : Acceptée, Refusée, Retirée ou Sans réponse.</summary>
    public bool EstClose => PassagesDeStatut.EstClos(Statut);

    /// <summary>Ses relances, dans l'ordre de leur planification.</summary>
    public IReadOnlyList<Relance> Relances => _relances;

    /// <summary>Sa relance à faire, s'il y en a une : jamais plus d'une à la fois.</summary>
    public Relance? RelanceAFaire => _relances.FirstOrDefault(relance => relance.EstAFaire);

    /// <summary>
    /// Le changement que corrigerait <see cref="EssayerDeCorrigerLeDernierChangement"/> : le dernier enregistré qui
    /// n'est ni corrigé ni lui-même une correction. Rien s'il n'y en a aucun.
    /// </summary>
    public ChangementDeStatut? ChangementACorriger =>
        _evenements.OfType<ChangementDeStatut>().LastOrDefault(changement => !changement.EstUneCorrection && !EstCorrige(changement));

    /// <summary>
    /// L'instant du premier événement en vigueur de l'historique, quelle que soit sa sorte : un événement marqué comme
    /// corrigé ne compte plus. Rien s'il n'y en a aucun. L'historique en vigueur commence à l'envoi : une date d'envoi
    /// corrigée ne suit pas ce premier événement (<see cref="PeutEtreEnvoyeeLe"/>).
    /// </summary>
    public DateTime? PremierEvenement =>
        _evenements.Where(EstEnVigueur).Select(evenement => (DateTime?)evenement.SurvenuLe).Min();

    /// <summary>Lit un poste saisi (<see cref="TexteDUneLigne"/>).</summary>
    public static bool EssayerDeLireLePoste(string? saisie, [NotNullWhen(true)] out string? poste, [NotNullWhen(false)] out string? erreur) =>
        TexteDUneLigne.EssayerDeLire(saisie, LibelleDuPoste, out poste, out erreur);

    /// <summary>
    /// Enregistre une candidature envoyée : elle part au statut <see cref="StatutDeCandidature.Envoyee"/>. Le poste doit
    /// être admis par <see cref="EssayerDeLireLePoste"/>, et l'envoi lu par <see cref="DateDEnvoi.EssayerDeLire"/>.
    /// </summary>
    /// <param name="entrepriseId">L'identifiant de l'entreprise, déjà enregistrée.</param>
    /// <param name="poste">Le poste saisi.</param>
    /// <param name="envoyeeLe">L'instant de l'envoi, en UTC.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    public static Candidature Enregistrer(int entrepriseId, string? poste, DateTime envoyeeLe, DateTime maintenant)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entrepriseId);
        if (!EssayerDeLireLePoste(poste, out var posteLu, out var erreur))
        {
            throw new ArgumentException(erreur, nameof(poste));
        }

        if (envoyeeLe.Kind != DateTimeKind.Utc || maintenant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("L'envoi et l'instant présent se donnent en UTC.", nameof(envoyeeLe));
        }

        if (envoyeeLe > maintenant)
        {
            throw new ArgumentException(DateDEnvoi.DansLeFutur, nameof(envoyeeLe));
        }

        return new Candidature(entrepriseId, posteLu, envoyeeLe, StatutDeCandidature.Envoyee);
    }

    /// <summary>
    /// Postule à une offre (#38) : une candidature enregistrée comme par <see cref="Enregistrer"/>, aux mêmes règles, qui
    /// désigne l'offre d'où elle vient. L'appelant lui donne l'entreprise et le poste de l'offre, et a vérifié que l'offre
    /// n'a pas encore de candidature.
    /// </summary>
    /// <param name="offreId">L'identifiant de l'offre, déjà enregistrée.</param>
    /// <param name="entrepriseId">L'identifiant de l'entreprise de l'offre.</param>
    /// <param name="poste">Le poste de l'offre.</param>
    /// <param name="envoyeeLe">L'instant de l'envoi, en UTC.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    public static Candidature Postuler(int offreId, int entrepriseId, string? poste, DateTime envoyeeLe, DateTime maintenant)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offreId);
        var candidature = Enregistrer(entrepriseId, poste, envoyeeLe, maintenant);
        candidature.OffreId = offreId;
        return candidature;
    }

    /// <summary>Vrai si une correction désigne ce changement : il reste dans l'historique, marqué comme corrigé.</summary>
    public bool EstCorrige(ChangementDeStatut changement) =>
        _evenements.OfType<ChangementDeStatut>().Any(correction => ReferenceEquals(correction.Corrige, changement));

    /// <summary>Vrai si une correction désigne cette étape (#25) : elle reste dans l'historique, marquée comme corrigée.</summary>
    public bool EstCorrigee(Etape etape) =>
        _evenements.OfType<Etape>().Any(correction => ReferenceEquals(correction.Corrige, etape));

    /// <summary>Vrai pour un événement en vigueur : aucune correction ne le désigne.</summary>
    public bool EstEnVigueur(EvenementDeCandidature evenement) => evenement switch
    {
        ChangementDeStatut changement => !EstCorrige(changement),
        Etape etape => !EstCorrigee(etape),
        _ => true,
    };

    /// <summary>L'étape de cet identifiant, si elle est la version en vigueur : rien si elle est corrigée, ou absente.</summary>
    public Etape? EtapeEnVigueur(int id) => _evenements.OfType<Etape>().FirstOrDefault(etape => etape.Id == id && !EstCorrigee(etape));

    /// <summary>
    /// Fait passer la candidature à un nouveau statut, si le diagramme d'états le permet : le changement entre dans
    /// l'historique, daté par l'horloge. Un statut qui clôt la candidature abandonne sa relance à faire, et la clôture
    /// de cette relance suit le changement dans l'historique. Sinon, rien ne change, et <paramref name="refus"/> dit
    /// pourquoi.
    /// </summary>
    /// <param name="nouveau">Le statut voulu.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    /// <param name="refus">Le motif du refus, lisible, si le passage n'est pas permis.</param>
    public bool EssayerDeChangerDeStatut(StatutDeCandidature nouveau, DateTime maintenant, [NotNullWhen(false)] out string? refus)
    {
        if (!PassagesDeStatut.EssayerDePasser(Statut, nouveau, out refus))
        {
            return false;
        }

        _evenements.Add(ChangementDeStatut.Passage(Statut, nouveau, maintenant));
        Statut = nouveau;
        if (EstClose && RelanceAFaire is { } relance)
        {
            Clore(relance, EtatDeRelance.Abandonnee, maintenant);
        }

        return true;
    }

    /// <summary>
    /// Planifie une relance (#14) : son échéance est la date locale de l'envoi plus <paramref name="delai"/> jours, et
    /// son destinataire un contact de l'entreprise de la candidature, ou personne. Une échéance déjà passée est admise.
    /// Refusé si la candidature est close, ou si elle a déjà une relance à faire : rien ne change, et
    /// <paramref name="refus"/> dit pourquoi.
    /// </summary>
    /// <param name="delai">Le délai en jours, admis par <see cref="DelaiDeRelance.EstAdmis"/>.</param>
    /// <param name="destinataireId">Le contact destinataire, dont l'appelant a vérifié l'entreprise ; rien pour personne.</param>
    /// <param name="fuseau">Le fuseau du poste, où se lit la date de l'envoi.</param>
    /// <param name="refus">Le motif du refus, lisible.</param>
    public bool EssayerDePlanifierUneRelance(int delai, int? destinataireId, TimeZoneInfo fuseau, [NotNullWhen(false)] out string? refus)
    {
        if (EstClose)
        {
            refus = RelanceSurCandidatureClose;
            return false;
        }

        if (RelanceAFaire is not null)
        {
            refus = RelanceDejaAFaire;
            return false;
        }

        _relances.Add(Relance.Planifiee(DelaiDeRelance.Echeance(EnvoyeeLe, fuseau, delai), destinataireId));
        refus = null;
        return true;
    }

    /// <summary>
    /// Change le délai et le destinataire de la relance à faire <paramref name="relanceId"/>. Refusé si ce n'est pas la
    /// relance à faire de la candidature : rien ne change.
    /// </summary>
    /// <param name="relanceId">L'identifiant de la relance que l'écran montrait.</param>
    /// <param name="delai">Le nouveau délai en jours, compté depuis la date locale de l'envoi.</param>
    /// <param name="destinataireId">Le nouveau destinataire, dont l'appelant a vérifié l'entreprise ; rien pour personne.</param>
    /// <param name="fuseau">Le fuseau du poste.</param>
    /// <param name="refus">Le motif du refus, lisible.</param>
    public bool EssayerDeChangerLaRelance(int relanceId, int delai, int? destinataireId, TimeZoneInfo fuseau, [NotNullWhen(false)] out string? refus)
    {
        if (RelanceAFaire is not { } relance || relance.Id != relanceId)
        {
            refus = RelancePasAFaire;
            return false;
        }

        relance.Changer(DelaiDeRelance.Echeance(EnvoyeeLe, fuseau, delai), destinataireId);
        refus = null;
        return true;
    }

    /// <summary>
    /// Marque faite ou abandonnée la relance à faire <paramref name="relanceId"/>, pour de bon : sa clôture entre dans
    /// l'historique, datée par l'horloge. Refusé si ce n'est pas la relance à faire de la candidature : rien ne change.
    /// </summary>
    /// <param name="relanceId">L'identifiant de la relance que l'écran montrait.</param>
    /// <param name="issue">Faite, ou abandonnée.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    /// <param name="refus">Le motif du refus, lisible.</param>
    public bool EssayerDeCloreLaRelance(int relanceId, EtatDeRelance issue, DateTime maintenant, [NotNullWhen(false)] out string? refus)
    {
        if (RelanceAFaire is not { } relance || relance.Id != relanceId)
        {
            refus = RelancePasAFaire;
            return false;
        }

        Clore(relance, issue, maintenant);
        refus = null;
        return true;
    }

    private void Clore(Relance relance, EtatDeRelance issue, DateTime maintenant)
    {
        relance.Clore(issue);
        _evenements.Add(ClotureDeRelance.De(relance, maintenant));
    }

    /// <summary>
    /// Corrige le dernier changement de statut (<see cref="ChangementACorriger"/>) : un nouveau changement, daté par
    /// l'horloge, ramène la candidature au statut d'avant et désigne le changement corrigé, qui reste dans l'historique.
    /// Répétée, la correction remonte jusqu'à Envoyée ; elle reprend aussi une candidature close. S'il n'y a rien à
    /// corriger, rien ne change, et <paramref name="refus"/> le dit.
    /// </summary>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    /// <param name="refus">Le motif du refus, si aucun changement n'est à corriger.</param>
    public bool EssayerDeCorrigerLeDernierChangement(DateTime maintenant, [NotNullWhen(false)] out string? refus)
    {
        var aCorriger = ChangementACorriger;
        if (aCorriger is null)
        {
            refus = RienACorriger;
            return false;
        }

        // Le statut présent est toujours le nouveau statut du changement à corriger : chaque passage s'ajoute à la
        // suite des changements non corrigés, et chaque correction retire le dernier d'entre eux.
        if (aCorriger.Nouveau != Statut)
        {
            throw new InvalidOperationException("L'historique ne s'accorde pas avec le statut de la candidature.");
        }

        _evenements.Add(ChangementDeStatut.Correction(aCorriger, maintenant));
        Statut = aCorriger.Ancien;
        refus = null;
        return true;
    }

    /// <summary>
    /// Note une étape, qui ne change pas le statut. Sa date doit être lue par <see cref="DateDUneEtape.EssayerDeLire"/>,
    /// et sa note admise par <see cref="Etape.EssayerDeLireLaNote"/>.
    /// </summary>
    /// <param name="type">Le type de l'étape.</param>
    /// <param name="survenueLe">L'instant de l'étape, en UTC : jamais avant l'envoi.</param>
    /// <param name="note">La note, ou rien.</param>
    public Etape NoterUneEtape(TypeDEtape type, DateTime survenueLe, string? note)
    {
        if (survenueLe < EnvoyeeLe)
        {
            throw new ArgumentException(DateDUneEtape.AvantLEnvoi, nameof(survenueLe));
        }

        var etape = Etape.Nouvelle(type, survenueLe, note);
        _evenements.Add(etape);
        return etape;
    }

    /// <summary>
    /// Corrige une étape en vigueur (#25) : la correction, une étape aux mêmes règles, s'ajoute à l'historique et désigne
    /// l'étape corrigée, qui y reste, marquée comme corrigée. Le statut ne change pas. La date doit être lue par
    /// <see cref="DateDUneEtape.EssayerDeLire"/>, et la note admise par <see cref="Etape.EssayerDeLireLaNote"/>.
    /// </summary>
    /// <param name="corrigee">L'étape à corriger, de cette candidature et en vigueur (<see cref="EtapeEnVigueur"/>).</param>
    /// <param name="type">Le type corrigé.</param>
    /// <param name="survenueLe">L'instant corrigé, en UTC : jamais avant l'envoi.</param>
    /// <param name="note">La note corrigée, ou rien.</param>
    public Etape CorrigerUneEtape(Etape corrigee, TypeDEtape type, DateTime survenueLe, string? note)
    {
        ArgumentNullException.ThrowIfNull(corrigee);
        if (!_evenements.Contains(corrigee) || EstCorrigee(corrigee))
        {
            throw new InvalidOperationException("Seule une étape en vigueur de cette candidature se corrige.");
        }

        if (survenueLe < EnvoyeeLe)
        {
            throw new ArgumentException(DateDUneEtape.AvantLEnvoi, nameof(survenueLe));
        }

        var correction = Etape.Correction(corrigee, type, survenueLe, note);
        _evenements.Add(correction);
        return correction;
    }

    /// <summary>
    /// Rattache la candidature à une entreprise déjà enregistrée : celle que reconnaît le nom saisi, par sa clé (#25).
    /// L'entreprise quittée n'est ni renommée ni supprimée. Ni le statut ni l'historique ne changent. Une autre
    /// entreprise retire le destinataire de la relance à faire, qui n'est plus l'un de ses contacts (#14) ; les
    /// relances closes gardent le leur.
    /// </summary>
    /// <param name="entrepriseId">L'identifiant de l'entreprise, déjà enregistrée.</param>
    public void RattacherA(int entrepriseId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entrepriseId);
        if (entrepriseId != EntrepriseId)
        {
            RelanceAFaire?.RetirerLeDestinataire();
        }

        EntrepriseId = entrepriseId;
    }

    /// <summary>
    /// Change le poste (#25), qui doit être admis par <see cref="EssayerDeLireLePoste"/>. Ni le statut ni l'historique ne
    /// changent.
    /// </summary>
    /// <param name="poste">Le poste saisi.</param>
    public void ChangerDePoste(string? poste)
    {
        if (!EssayerDeLireLePoste(poste, out var posteLu, out var erreur))
        {
            throw new ArgumentException(erreur, nameof(poste));
        }

        Poste = posteLu;
    }

    /// <summary>
    /// Vrai si la date d'envoi peut être corrigée à cet instant (#25) : jamais dans le futur, et jamais après le premier
    /// événement en vigueur de l'historique (<see cref="PremierEvenement"/>) ; à l'instant même de ce premier événement,
    /// elle est admise. Sinon, <paramref name="refus"/> dit pourquoi.
    /// </summary>
    /// <param name="envoyeeLe">L'instant d'envoi voulu, en UTC.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    /// <param name="refus">Le motif du refus, lisible.</param>
    public bool PeutEtreEnvoyeeLe(DateTime envoyeeLe, DateTime maintenant, [NotNullWhen(false)] out string? refus)
    {
        if (envoyeeLe.Kind != DateTimeKind.Utc || maintenant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("L'envoi et l'instant présent se donnent en UTC.", nameof(envoyeeLe));
        }

        if (envoyeeLe > maintenant)
        {
            refus = DateDEnvoi.DansLeFutur;
            return false;
        }

        if (PremierEvenement is { } premier && envoyeeLe > premier)
        {
            refus = DateDEnvoi.ApresLePremierEvenement;
            return false;
        }

        refus = null;
        return true;
    }

    /// <summary>
    /// Corrige l'instant de l'envoi (#25), qui doit être admis par <see cref="PeutEtreEnvoyeeLe"/>. Ni le statut ni
    /// l'historique ne changent : chaque événement garde sa date.
    /// </summary>
    /// <param name="envoyeeLe">Le nouvel instant de l'envoi, en UTC.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    public void CorrigerLEnvoi(DateTime envoyeeLe, DateTime maintenant)
    {
        if (!PeutEtreEnvoyeeLe(envoyeeLe, maintenant, out var refus))
        {
            throw new ArgumentException(refus, nameof(envoyeeLe));
        }

        EnvoyeeLe = envoyeeLe;
    }
}
