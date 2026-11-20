using System.Diagnostics.CodeAnalysis;
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
/// </summary>
public sealed class Candidature
{
    /// <summary>Les mots qui désignent le poste dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuPoste = new("Indiquez le poste.", "Le poste");

    /// <summary>Le motif du refus d'une correction quand aucun changement n'est à corriger.</summary>
    public const string RienACorriger = "Aucun changement de statut n'est à corriger : la candidature est à son statut de départ.";

    // Dans l'ordre d'enregistrement : celui de la base pour les événements relus (Entity Framework Core les y range,
    // par identifiant), puis ceux ajoutés depuis.
    private readonly List<EvenementDeCandidature> _evenements = [];

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

    /// <summary>
    /// Le changement que corrigerait <see cref="EssayerDeCorrigerLeDernierChangement"/> : le dernier enregistré qui
    /// n'est ni corrigé ni lui-même une correction. Rien s'il n'y en a aucun.
    /// </summary>
    public ChangementDeStatut? ChangementACorriger =>
        _evenements.OfType<ChangementDeStatut>().LastOrDefault(changement => !changement.EstUneCorrection && !EstCorrige(changement));

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

    /// <summary>Vrai si une correction désigne ce changement : il reste dans l'historique, marqué comme corrigé.</summary>
    public bool EstCorrige(ChangementDeStatut changement) =>
        _evenements.OfType<ChangementDeStatut>().Any(correction => ReferenceEquals(correction.Corrige, changement));

    /// <summary>
    /// Fait passer la candidature à un nouveau statut, si le diagramme d'états le permet : le changement entre dans
    /// l'historique, daté par l'horloge. Sinon, rien ne change, et <paramref name="refus"/> dit pourquoi.
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
        return true;
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
}
