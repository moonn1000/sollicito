namespace Sollicito.Application.IA;

/// <summary>
/// Le port du fournisseur d'IA (ADR-0001, D7 et D9 ; ADR-0016) : un seul geste, demander. Le module IA ne connaît que ce
/// port ; changer de fournisseur ne touche pas au reste. Le port ne donne à l'IA ni outil, ni adresse, ni en-tête : rien
/// ne lui permet d'agir. Il sépare les consignes, écrites dans le code, des données, qui ne sont pas fiables et ne lui
/// parviennent que comme données à analyser. Sa réponse est un texte borné, ou une issue fermée ; jamais une exception
/// qui porterait le contenu. Sont branchés : l'adaptateur <see cref="AucuneIA"/> (#43), puis ceux de Claude et d'OpenAI
/// (#48) et d'Ollama (#45). Les tests de contrat les éprouvent tous de la même façon.
/// </summary>
public interface IFournisseurDIA
{
    /// <summary>Le fournisseur que cet adaptateur sert.</summary>
    FournisseurDIA Fournisseur { get; }

    /// <summary>
    /// Pose la demande à l'IA. Rend son texte, au plus <see cref="DemandeALIA.CaracteresDeLaReponse"/> caractères, ou une
    /// issue fermée : indisponible, avec sa raison, ou un échec typé. Seule l'annulation lève une exception.
    /// </summary>
    Task<ReponseDeLIA> DemanderAsync(DemandeALIA demande, CancellationToken annulation);
}

/// <summary>
/// Une demande à l'IA : des consignes, écrites dans le code, et des données, qui ne sont pas fiables (le texte d'une
/// offre collée, par exemple) et que l'adaptateur transmet comme données à analyser, jamais comme instructions
/// (ADR-0001, D9, « Contenu non fiable »). Elle borne la réponse, en caractères et en durée. Son texte
/// (<see cref="ToString"/>) ne montre ni les consignes ni les données.
/// </summary>
public sealed class DemandeALIA
{
    /// <summary>Les consignes les plus longues qu'une demande porte.</summary>
    public const int TailleMaximaleDesConsignes = 20_000;

    /// <summary>Les données les plus longues qu'une demande porte : le texte d'une offre et sa marge.</summary>
    public const int TailleMaximaleDesDonnees = 60_000;

    /// <summary>La plus longue réponse qu'une demande admet.</summary>
    public const int TailleMaximaleDeLaReponse = 20_000;

    /// <summary>La plus longue durée qu'une demande admet.</summary>
    public static readonly TimeSpan DureeMaximale = TimeSpan.FromMinutes(3);

    /// <summary>Une demande, dont les bornes sont vérifiées.</summary>
    /// <param name="consignes">Ce que l'IA doit faire, écrit dans le code ; jamais une saisie de l'utilisateur.</param>
    /// <param name="donnees">Ce qu'elle analyse, qui n'est pas fiable ; vide s'il n'y a rien.</param>
    /// <param name="caracteresDeLaReponse">Le nombre de caractères au-delà duquel la réponse est refusée.</param>
    /// <param name="duree">La durée au-delà de laquelle la demande échoue.</param>
    public DemandeALIA(string consignes, string donnees, int caracteresDeLaReponse, TimeSpan duree)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consignes);
        ArgumentNullException.ThrowIfNull(donnees);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(consignes.Length, TailleMaximaleDesConsignes, nameof(consignes));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(donnees.Length, TailleMaximaleDesDonnees, nameof(donnees));
        ArgumentOutOfRangeException.ThrowIfLessThan(caracteresDeLaReponse, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(caracteresDeLaReponse, TailleMaximaleDeLaReponse);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duree, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duree, DureeMaximale);

        Consignes = consignes;
        Donnees = donnees;
        CaracteresDeLaReponse = caracteresDeLaReponse;
        Duree = duree;
    }

    /// <summary>Ce que l'IA doit faire, écrit dans le code.</summary>
    public string Consignes { get; }

    /// <summary>Ce qu'elle analyse : une donnée, jamais une instruction.</summary>
    public string Donnees { get; }

    /// <summary>Le nombre de caractères au-delà duquel la réponse est refusée.</summary>
    public int CaracteresDeLaReponse { get; }

    /// <summary>La durée au-delà de laquelle la demande échoue.</summary>
    public TimeSpan Duree { get; }

    /// <summary>Un texte qui ne montre ni les consignes ni les données.</summary>
    public override string ToString() => "Demande à l'IA";
}

/// <summary>
/// La réponse de l'IA : un <see cref="Texte"/>, ou une issue fermée, <see cref="Indisponible"/> ou <see cref="Echec"/>.
/// Aucune autre sorte n'existe. Aucun texte (<see cref="object.ToString"/>) ne montre ce que l'IA a répondu.
/// </summary>
public abstract class ReponseDeLIA
{
    private ReponseDeLIA()
    {
    }

    /// <summary>Le texte de l'IA, borné par la demande. Il n'est pas fiable : son affichage et sa validation suivent leurs stories (#46, #47).</summary>
    public sealed class Texte : ReponseDeLIA
    {
        /// <summary>Un texte répondu par l'IA.</summary>
        public Texte(string contenu)
        {
            ArgumentNullException.ThrowIfNull(contenu);
            Contenu = contenu;
        }

        /// <summary>Ce que l'IA a répondu.</summary>
        public string Contenu { get; }

        /// <summary>Un texte qui ne montre pas la réponse.</summary>
        public override string ToString() => "Texte de l'IA";
    }

    /// <summary>Aucune IA ne peut répondre, et l'on sait pourquoi.</summary>
    public sealed class Indisponible(RaisonDIndisponibilite raison) : ReponseDeLIA
    {
        /// <summary>Pourquoi aucune IA ne répond.</summary>
        public RaisonDIndisponibilite Raison { get; } = raison;

        /// <inheritdoc />
        public override string ToString() => $"IA indisponible : {Raison}";
    }

    /// <summary>L'IA a été demandée, et la demande a échoué.</summary>
    public sealed class Echec(SorteDEchec sorte) : ReponseDeLIA
    {
        /// <summary>Comment la demande a échoué.</summary>
        public SorteDEchec Sorte { get; } = sorte;

        /// <inheritdoc />
        public override string ToString() => $"Échec de l'IA : {Sorte}";
    }
}

/// <summary>Pourquoi aucune IA ne répond.</summary>
public enum RaisonDIndisponibilite
{
    /// <summary>Aucune IA n'est choisie : la valeur par défaut.</summary>
    AucuneIA,

    /// <summary>Le choix de l'IA ne se relit pas, ou le fichier des réglages entier : Sollicito continue sans IA.</summary>
    ReglageIllisible,

    /// <summary>Un fournisseur en ligne est choisi, sans clé enregistrée.</summary>
    CleAbsente,

    /// <summary>La clé enregistrée ne se relit pas sur ce poste : elle est à ressaisir.</summary>
    CleIllisible,

    /// <summary>Le fournisseur choisi n'a pas encore d'adaptateur : Ollama arrive avec #45.</summary>
    PasEncoreBranche,
}

/// <summary>
/// Comment une demande à l'IA a échoué (#43, #48, ADR-0017) : une liste fermée, sans jamais le message du fournisseur. Un
/// fournisseur en ligne classe une erreur par son seul statut HTTP, sans en lire le corps.
/// </summary>
public enum SorteDEchec
{
    /// <summary>La demande a dépassé sa durée, lecture de la réponse comprise.</summary>
    DelaiDepasse,

    /// <summary>La réponse dépassait la borne de la demande, ou le fournisseur l'a coupée : elle n'est pas rendue.</summary>
    ReponseTropLongue,

    /// <summary>La réponse ne se lisait pas, ou n'était pas celle qu'on attend : une redirection, un statut inattendu.</summary>
    ReponseIllisible,

    /// <summary>Le fournisseur a refusé la demande ou son contenu.</summary>
    RefusDuFournisseur,

    /// <summary>Le fournisseur n'a pas pu être joint : réseau absent, nom introuvable, connexion ou TLS refusés.</summary>
    Injoignable,

    /// <summary>Le fournisseur a refusé la clé (401).</summary>
    CleRefusee,

    /// <summary>Une limite d'usage, un crédit épuisé ou un plafond de dépense (402, 429).</summary>
    QuotaAtteint,

    /// <summary>Le fournisseur refuse l'accès avec cette clé : un pays, un compte (403).</summary>
    AccesRefuse,

    /// <summary>Le fournisseur ne sert plus le modèle de cette version de Sollicito (404).</summary>
    ModeleIndisponible,

    /// <summary>Le fournisseur est en panne ou surchargé (5xx, 529), ou n'a pas pu finir la réponse.</summary>
    FournisseurEnPanne,

    /// <summary>Une autre demande à l'IA est en cours : aucune requête n'est partie (une seule à la fois).</summary>
    UneDemandeEnCours,
}
