using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Offres;

/// <summary>
/// Une offre d'emploi importée (modèle de domaine, « Dédoublonner les offres » et « Coller une offre » ; ADR-0013) : en
/// V1, un texte collé (#36), puis une ligne d'un fichier CSV (#40). Elle garde sa source et son empreinte, qui la
/// reconnaissent (règle 2), et son poste normalisé, qui signale un doublon possible (règle 3). Elle désigne son
/// entreprise par son identifiant, ou n'en a pas : une offre anonyme. Son texte ne change plus après l'import ; ses
/// champs (entreprise, poste, lieu, date de publication, adresse de l'annonce) se corrigent.
/// <para>
/// L'identifiant externe d'une source qui en fournit un (règle 1) attend le Forem, en V2 : aucune source de la V1 n'en
/// donne.
/// </para>
/// </summary>
public sealed class Offre
{
    /// <summary>Les mots qui désignent le poste dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuPoste = new("Indiquez le poste.", "Le poste");

    /// <summary>Les mots qui désignent le lieu dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuLieu = new("Indiquez le lieu.", "Le lieu");

    /// <summary>Le message d'un poste sans lettre ni chiffre, dont la forme normalisée serait vide.</summary>
    public const string PosteSansLettre = "Le poste doit contenir au moins une lettre ou un chiffre.";

    // Pour Entity Framework Core, qui lit une offre enregistrée.
    private Offre(SourceDOffre source, string empreinte, string poste, string posteNormalise, DateTime ajouteeLe)
    {
        Source = source;
        Empreinte = empreinte;
        Poste = poste;
        PosteNormalise = posteNormalise;
        AjouteeLe = ajouteeLe;
    }

    /// <summary>L'identifiant technique, donné par la base.</summary>
    public int Id { get; private set; }

    /// <summary>Le connecteur qui l'a fait entrer.</summary>
    public SourceDOffre Source { get; private set; }

    /// <summary>Son empreinte (<see cref="EmpreinteDUneOffre"/>) : avec la source, unique en base.</summary>
    public string Empreinte { get; private set; }

    /// <summary>L'identifiant de son entreprise, ou rien pour une offre anonyme.</summary>
    public int? EntrepriseId { get; private set; }

    /// <summary>Le poste offert.</summary>
    public string Poste { get; private set; }

    /// <summary>Le poste normalisé (<see cref="Offres.PosteNormalise"/>), jamais vide.</summary>
    public string PosteNormalise { get; private set; }

    /// <summary>Le lieu, s'il est indiqué.</summary>
    public string? Lieu { get; private set; }

    /// <summary>Le jour de sa publication, s'il est indiqué.</summary>
    public DateOnly? PublieeLe { get; private set; }

    /// <summary>L'adresse de l'annonce (<see cref="AdresseDUneAnnonce"/>), si elle est indiquée.</summary>
    public string? Adresse { get; private set; }

    /// <summary>Son texte, gardé tel quel (<see cref="TexteDUneOffre"/>) ; rien pour une ligne de CSV qui n'en a pas (#40).</summary>
    public string? Texte { get; private set; }

    /// <summary>L'instant de l'import, en UTC.</summary>
    public DateTime AjouteeLe { get; private set; }

    /// <summary>Lit un poste saisi : un texte d'une ligne (<see cref="TexteDUneLigne"/>), avec au moins une lettre ou un chiffre.</summary>
    public static bool EssayerDeLireLePoste(string? saisie, [NotNullWhen(true)] out string? poste, [NotNullWhen(false)] out string? erreur)
    {
        if (!TexteDUneLigne.EssayerDeLire(saisie, LibelleDuPoste, out poste, out erreur))
        {
            return false;
        }

        if (Offres.PosteNormalise.De(poste).Length == 0)
        {
            poste = null;
            erreur = PosteSansLettre;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Lit un lieu saisi, facultatif : vide (rien, ou que des blancs), il ne fait aucun lieu ; sinon, un texte d'une ligne
    /// (<see cref="TexteDUneLigne"/>).
    /// </summary>
    public static bool EssayerDeLireLeLieu(string? saisie, out string? lieu, [NotNullWhen(false)] out string? erreur)
    {
        lieu = null;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = null;
            return true;
        }

        return TexteDUneLigne.EssayerDeLire(saisie, LibelleDuLieu, out lieu, out erreur);
    }

    /// <summary>
    /// Une offre importée. Ses champs doivent être admis par leurs règles : <see cref="EssayerDeLireLePoste"/>,
    /// <see cref="EssayerDeLireLeLieu"/>, <see cref="DateDePublication.EssayerDeLire"/>,
    /// <see cref="AdresseDUneAnnonce.EssayerDeLire"/> et, s'il y en a un, <see cref="TexteDUneOffre.EssayerDeLire"/>.
    /// </summary>
    /// <param name="source">Le connecteur qui l'a fait entrer.</param>
    /// <param name="empreinte">Son empreinte, calculée par <see cref="EmpreinteDUneOffre.De"/>.</param>
    /// <param name="entrepriseId">L'identifiant de son entreprise, déjà enregistrée, ou rien.</param>
    /// <param name="champs">Ses champs.</param>
    /// <param name="texte">Son texte, ou rien.</param>
    /// <param name="ajouteeLe">L'instant de l'import, en UTC.</param>
    public static Offre Importer(SourceDOffre source, string empreinte, int? entrepriseId, ChampsDUneOffre champs, string? texte, DateTime ajouteeLe)
    {
        ArgumentNullException.ThrowIfNull(champs);
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Source inconnue.");
        }

        if (!EmpreinteDUneOffre.APourForme(empreinte))
        {
            throw new ArgumentException("Une empreinte s'écrit en 64 chiffres hexadécimaux minuscules.", nameof(empreinte));
        }

        if (texte is not null && (!TexteDUneOffre.EssayerDeLire(texte, out var lu, out _) || lu != texte))
        {
            throw new ArgumentException("Le texte doit être admis, et déjà retouché, par TexteDUneOffre.", nameof(texte));
        }

        if (ajouteeLe.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Un instant se donne en UTC.", nameof(ajouteeLe));
        }

        var offre = new Offre(source, empreinte, champs.Poste, Offres.PosteNormalise.De(champs.Poste), ajouteeLe) { Texte = texte };
        offre.Corriger(entrepriseId, champs);
        return offre;
    }

    /// <summary>
    /// Vrai si une correction vers cette entreprise et ce poste changerait ce que compare la règle 3 : l'entreprise, ou
    /// le poste normalisé. Elle refait alors la comparaison.
    /// </summary>
    public bool ChangeraitLeDoublonnage(int? entrepriseId, ChampsDUneOffre champs)
    {
        ArgumentNullException.ThrowIfNull(champs);
        return entrepriseId != EntrepriseId || !string.Equals(Offres.PosteNormalise.De(champs.Poste), PosteNormalise, StringComparison.Ordinal);
    }

    /// <summary>
    /// Corrige ses champs (ADR-0013), jamais son texte ni sa source : son entreprise, déjà enregistrée, ou aucune ; son
    /// poste, son lieu, sa date de publication et l'adresse de son annonce.
    /// </summary>
    public void Corriger(int? entrepriseId, ChampsDUneOffre champs)
    {
        ArgumentNullException.ThrowIfNull(champs);
        if (entrepriseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entrepriseId), entrepriseId, "Une entreprise enregistrée a un identifiant positif.");
        }

        EntrepriseId = entrepriseId;
        Poste = champs.Poste;
        PosteNormalise = Offres.PosteNormalise.De(champs.Poste);
        Lieu = champs.Lieu;
        PublieeLe = champs.PublieeLe;
        Adresse = champs.Adresse;
    }
}

/// <summary>
/// Les champs d'une offre, admis par leurs règles : on ne les obtient que par <see cref="EssayerDeLire"/>, qui rend
/// sinon le motif du refus de chaque champ.
/// </summary>
public sealed class ChampsDUneOffre
{
    private ChampsDUneOffre(string poste, string? lieu, DateOnly? publieeLe, string? adresse)
    {
        Poste = poste;
        Lieu = lieu;
        PublieeLe = publieeLe;
        Adresse = adresse;
    }

    /// <summary>Le poste, requis.</summary>
    public string Poste { get; }

    /// <summary>Le lieu, ou rien.</summary>
    public string? Lieu { get; }

    /// <summary>La date de publication, ou rien.</summary>
    public DateOnly? PublieeLe { get; }

    /// <summary>L'adresse de l'annonce, ou rien.</summary>
    public string? Adresse { get; }

    /// <summary>
    /// Lit les champs saisis. Rend vrai et les champs, ou faux et, pour chaque champ refusé, son motif. Un motif ne
    /// reprend jamais le texte saisi.
    /// </summary>
    /// <param name="poste">Le poste saisi.</param>
    /// <param name="lieu">Le lieu saisi.</param>
    /// <param name="publieeLe">La date de publication saisie.</param>
    /// <param name="adresse">L'adresse saisie.</param>
    /// <param name="aujourdhui">Aujourd'hui, en date locale du poste.</param>
    /// <param name="champs">Les champs lus, s'ils sont tous admis.</param>
    /// <param name="refus">Les motifs des champs refusés.</param>
    public static bool EssayerDeLire(
        string? poste,
        string? lieu,
        string? publieeLe,
        string? adresse,
        DateOnly aujourdhui,
        [NotNullWhen(true)] out ChampsDUneOffre? champs,
        out RefusDesChamps refus)
    {
        refus = new RefusDesChamps(
            Offre.EssayerDeLireLePoste(poste, out var posteLu, out var refusDuPoste) ? null : refusDuPoste,
            Offre.EssayerDeLireLeLieu(lieu, out var lieuLu, out var refusDuLieu) ? null : refusDuLieu,
            DateDePublication.EssayerDeLire(publieeLe, aujourdhui, out var dateLue, out var refusDeLaDate) ? null : refusDeLaDate,
            AdresseDUneAnnonce.EssayerDeLire(adresse, out var adresseLue, out var refusDeLAdresse) ? null : refusDeLAdresse);
        champs = posteLu is not null && refus.Aucun ? new ChampsDUneOffre(posteLu, lieuLu, dateLue, adresseLue) : null;
        return champs is not null;
    }
}

/// <summary>Les motifs des champs refusés d'une offre ; rien pour un champ admis.</summary>
public sealed record RefusDesChamps(string? Poste, string? Lieu, string? PublieeLe, string? Adresse)
{
    /// <summary>Vrai si aucun champ n'est refusé.</summary>
    public bool Aucun => Poste is null && Lieu is null && PublieeLe is null && Adresse is null;
}
