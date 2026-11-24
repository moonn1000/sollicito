using System.Globalization;
using System.Text;
using Sollicito.Domain.Offres;
using Sollicito.Domain.Textes;

namespace Sollicito.Application.Offres;

/// <summary>Une colonne du format de l'import CSV (#40, ADR-0015 ; <c>docs/import-csv.md</c>), dans l'ordre du format.</summary>
public enum ColonneDuFichier
{
    /// <summary><c>poste</c>, obligatoire.</summary>
    Poste,

    /// <summary><c>entreprise</c>, facultative.</summary>
    Entreprise,

    /// <summary><c>lieu</c>, facultatif.</summary>
    Lieu,

    /// <summary><c>publieeLe</c>, facultative : <c>JJ/MM/AAAA</c> ou <c>AAAA-MM-JJ</c>.</summary>
    PublieeLe,

    /// <summary><c>adresse</c>, facultative.</summary>
    Adresse,

    /// <summary><c>texte</c>, facultatif.</summary>
    Texte,
}

/// <summary>
/// Un fichier CSV d'offres lu par <see cref="LectureDUnFichierCsv"/> : ses colonnes, dans l'ordre de son en-tête, ses
/// lignes d'offres, et le nombre de lignes vides ignorées.
/// </summary>
/// <param name="Colonnes">Les colonnes de l'en-tête, dans son ordre.</param>
/// <param name="Lignes">Les lignes d'offres, dans l'ordre du fichier, lignes vides retirées.</param>
/// <param name="LignesVides">Les lignes vides, ou dont toutes les cellules sont vides, ignorées.</param>
public sealed record FichierCsv(IReadOnlyList<ColonneDuFichier> Colonnes, IReadOnlyList<LigneDuFichier> Lignes, int LignesVides);

/// <summary>
/// Une ligne d'offre d'un fichier : son numéro, celui d'un tableur (l'en-tête est la ligne 1, et une cellule sur plusieurs
/// lignes ne décale pas la suite), et ses cellules par colonne, telles que lues, guillemets décodés. Rien si elle n'a pas
/// autant de cellules que l'en-tête : elle est alors invalide.
/// </summary>
public sealed record LigneDuFichier(int Numero, IReadOnlyDictionary<ColonneDuFichier, string>? Cellules);

/// <summary>Pourquoi un fichier est refusé en entier : un mot fixé par le code, que le journal peut écrire (ADR-0015).</summary>
public enum SorteDeRefusDuFichier
{
    /// <summary>Le fichier est vide.</summary>
    Vide,

    /// <summary>Le fichier dépasse 2 Mo.</summary>
    Taille,

    /// <summary>Le fichier n'est pas en UTF-8.</summary>
    Encodage,

    /// <summary>L'en-tête ne nomme pas les colonnes du format.</summary>
    EnTete,

    /// <summary>Un guillemet mal placé ou jamais fermé.</summary>
    Structure,

    /// <summary>Plus de 1 000 lignes sous l'en-tête.</summary>
    Lignes,

    /// <summary>Aucune ligne d'offre sous l'en-tête.</summary>
    AucuneOffre,

    /// <summary>Une cellule dépasse la borne de son champ.</summary>
    Borne,
}

/// <summary>Ce que rend la lecture d'un fichier CSV : le fichier lu, ou son refus.</summary>
public abstract record LectureDuFichier
{
    private LectureDuFichier()
    {
    }

    /// <summary>Le fichier est lu.</summary>
    public sealed record Lu(FichierCsv Fichier) : LectureDuFichier;

    /// <summary>Le fichier est refusé en entier : sa sorte, et le message pour l'utilisateur, qui ne cite jamais une cellule.</summary>
    public sealed record Refuse(SorteDeRefusDuFichier Sorte, string Message) : LectureDuFichier;
}

/// <summary>
/// La lecture d'un fichier CSV d'offres (#40, ADR-0015 ; <c>docs/import-csv.md</c>), sans aucune entrée-sortie : des
/// octets reçus, l'en-tête et les lignes numérotées, ou le refus du fichier entier. Rien n'y est interprété : une cellule
/// reste le texte qu'elle porte, formule comprise.
/// <list type="bullet">
/// <item>UTF-8, avec ou sans BOM, décodé strictement ; tout autre encodage est refusé, BOM UTF-16 compris.</item>
/// <item>L'en-tête, la première ligne, nomme les colonnes du format, sans tenir compte de la casse ni des blancs de bord,
/// dans l'ordre qu'on veut ; <c>poste</c> y est obligatoire.</item>
/// <item>Le séparateur, <c>;</c> ou <c>,</c>, se lit sur l'en-tête, dont les noms n'en contiennent aucun.</item>
/// <item>Les guillemets suivent la RFC 4180 : une cellule entre guillemets droits porte séparateurs, sauts de ligne et
/// <c>""</c> pour un guillemet. Un guillemet dans une cellule sans guillemets, autre chose qu'un séparateur ou une fin de
/// ligne après le guillemet fermant, ou un guillemet jamais fermé, refusent le fichier.</item>
/// <item>Les fins de ligne : CRLF, LF ou CR seul ; la dernière est facultative.</item>
/// <item>Les bornes : 2 Mo, 1 000 lignes sous l'en-tête, et pour chaque cellule la borne de son champ. Au-delà, le
/// fichier est refusé : un guillemet mal fermé peut avaler plusieurs lignes dans une seule cellule.</item>
/// </list>
/// </summary>
public static class LectureDUnFichierCsv
{
    /// <summary>La taille maximale d'un fichier, en octets : 2 Mo (2 097 152 octets).</summary>
    public const int TailleMaximale = 2 * 1024 * 1024;

    /// <summary>Le nombre maximal de lignes sous l'en-tête, lignes vides comprises.</summary>
    public const int LignesMaximales = 1_000;

    /// <summary>Les noms des colonnes, tels que l'en-tête les écrit, à la casse près.</summary>
    public static readonly IReadOnlyDictionary<ColonneDuFichier, string> Noms = new Dictionary<ColonneDuFichier, string>
    {
        [ColonneDuFichier.Poste] = "poste",
        [ColonneDuFichier.Entreprise] = "entreprise",
        [ColonneDuFichier.Lieu] = "lieu",
        [ColonneDuFichier.PublieeLe] = "publieeLe",
        [ColonneDuFichier.Adresse] = "adresse",
        [ColonneDuFichier.Texte] = "texte",
    };

    /// <summary>Le message d'un fichier vide.</summary>
    public const string MessageVide = "Le fichier est vide.";

    /// <summary>Le message d'un fichier trop gros.</summary>
    public const string MessageTaille = "Le fichier dépasse 2 Mo : découpez-le en plusieurs fichiers.";

    /// <summary>Le message d'un fichier qui n'est pas en UTF-8.</summary>
    public const string MessageEncodage = "Le fichier n'est pas en UTF-8 : enregistrez-le au format CSV UTF-8, puis réessayez.";

    /// <summary>Le message d'un fichier de trop de lignes.</summary>
    public const string MessageLignes = "Le fichier porte plus de 1 000 lignes d'offres : découpez-le en plusieurs fichiers.";

    /// <summary>Le message d'un fichier sans ligne d'offre.</summary>
    public const string MessageAucuneOffre = "Le fichier ne porte aucune offre sous son en-tête.";

    /// <summary>Le message d'un en-tête sans la colonne <c>poste</c>.</summary>
    public const string MessagePosteManquant = "La première ligne doit nommer les colonnes : la colonne poste y manque.";

    /// <summary>Le message d'un en-tête qui porte les deux séparateurs.</summary>
    public const string MessageDeuxSeparateurs = "L'en-tête mêle les deux séparateurs, ; et , : gardez-en un seul.";

    /// <summary>Le message d'une colonne inconnue de l'en-tête, à ce rang (la première est la colonne 1).</summary>
    public static string MessageColonneInconnue(int rang) => Format($"La colonne {rang} de l'en-tête n'est pas une colonne du format.");

    /// <summary>Le message d'une colonne vide de l'en-tête.</summary>
    public static string MessageColonneVide(int rang) => Format($"La colonne {rang} de l'en-tête est vide.");

    /// <summary>Le message d'une colonne répétée de l'en-tête.</summary>
    public static string MessageColonneRepetee(int rang) => Format($"La colonne {rang} de l'en-tête répète une colonne déjà nommée.");

    /// <summary>Le message d'un guillemet jamais fermé, ouvert à cette ligne.</summary>
    public static string MessageGuillemetNonFerme(int ligne) => Format($"Le fichier est mal formé à la ligne {ligne} : un guillemet n'est jamais fermé.");

    /// <summary>Le message d'un guillemet mal placé, à cette ligne.</summary>
    public static string MessageGuillemetMalPlace(int ligne) => Format($"Le fichier est mal formé à la ligne {ligne} : un guillemet est mal placé.");

    /// <summary>Le message d'une cellule qui dépasse la borne de son champ.</summary>
    public static string MessageBorne(int ligne, ColonneDuFichier colonne) =>
        Format($"Ligne {ligne}, colonne {Noms[colonne]} : la cellule dépasse {Borne(colonne)!.Value.Libelle} caractères.");

    /// <summary>Lit les octets d'un fichier.</summary>
    public static LectureDuFichier Lire(ReadOnlySpan<byte> contenu)
    {
        if (contenu.Length == 0)
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Vide, MessageVide);
        }

        if (contenu.Length > TailleMaximale)
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Taille, MessageTaille);
        }

        if (contenu.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || contenu.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Encodage, MessageEncodage);
        }

        if (contenu.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            contenu = contenu[3..];
        }

        string texte;
        try
        {
            texte = Utf8Strict.GetString(contenu);
        }
        catch (DecoderFallbackException)
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Encodage, MessageEncodage);
        }

        if (texte.Length == 0)
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Vide, MessageVide);
        }

        if (!SeparateurDeLEnTete(texte, out var separateur))
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.EnTete, MessageDeuxSeparateurs);
        }

        var (enregistrements, refusDuDecoupage) = Decouper(texte, separateur);
        if (refusDuDecoupage is not null)
        {
            return refusDuDecoupage;
        }

        var enTete = LireLEnTete(enregistrements[0].Cellules);
        if (enTete.Refus is { } refusDeLEnTete)
        {
            return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.EnTete, refusDeLEnTete);
        }

        var colonnes = enTete.Colonnes!;
        var lignes = new List<LigneDuFichier>();
        var vides = 0;
        foreach (var (numero, cellules) in enregistrements.Skip(1))
        {
            if (cellules.All(string.IsNullOrWhiteSpace))
            {
                vides++;
                continue;
            }

            // La borne se juge aussi sur une ligne qui n'a pas autant de cellules que l'en-tête, chaque cellule sous la
            // colonne où un tableur la montrerait : un guillemet mal fermé qui avale des lignes refuse le fichier.
            for (var i = 0; i < Math.Min(cellules.Count, colonnes.Count); i++)
            {
                if (Borne(colonnes[i]) is { } borne && Mesurer(colonnes[i], cellules[i]) > borne.Valeur)
                {
                    return new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Borne, MessageBorne(numero, colonnes[i]));
                }
            }

            if (cellules.Count != colonnes.Count)
            {
                lignes.Add(new LigneDuFichier(numero, null));
                continue;
            }

            var parColonne = new Dictionary<ColonneDuFichier, string>(colonnes.Count);
            for (var i = 0; i < colonnes.Count; i++)
            {
                parColonne[colonnes[i]] = cellules[i];
            }

            lignes.Add(new LigneDuFichier(numero, parColonne));
        }

        return lignes.Count == 0
            ? new LectureDuFichier.Refuse(SorteDeRefusDuFichier.AucuneOffre, MessageAucuneOffre)
            : new LectureDuFichier.Lu(new FichierCsv(colonnes, lignes, vides));
    }

    private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // La borne d'une colonne, mesurée comme la règle de son champ : un texte d'une ligne, l'adresse, le texte. La date n'a
    // pas de borne de longueur : sa forme la juge, ligne par ligne.
    private static (int Valeur, string Libelle)? Borne(ColonneDuFichier colonne) => colonne switch
    {
        ColonneDuFichier.Poste or ColonneDuFichier.Entreprise or ColonneDuFichier.Lieu => (TexteDUneLigne.LongueurMaximale, "200"),
        ColonneDuFichier.Adresse => (AdresseDUneAnnonce.LongueurMaximale, "2 000"),
        ColonneDuFichier.Texte => (TexteDUneOffre.LongueurMaximale, "50 000"),
        _ => null,
    };

    private static int Mesurer(ColonneDuFichier colonne, string cellule) =>
        colonne == ColonneDuFichier.Texte ? TexteDUneOffre.LongueurMesuree(cellule) : cellule.Trim().Length;

    // Le séparateur de l'en-tête : le premier ; ou , hors des guillemets de la première ligne, ou aucun pour une seule
    // colonne. Faux si l'en-tête porte les deux.
    private static bool SeparateurDeLEnTete(string texte, out char? separateur)
    {
        separateur = null;
        var pointVirgule = false;
        var virgule = false;
        var entreGuillemets = false;
        foreach (var caractere in texte)
        {
            if (caractere == '"')
            {
                entreGuillemets = !entreGuillemets;
            }
            else if (!entreGuillemets && caractere is '\r' or '\n')
            {
                break;
            }
            else if (!entreGuillemets && caractere == ';')
            {
                pointVirgule = true;
            }
            else if (!entreGuillemets && caractere == ',')
            {
                virgule = true;
            }
        }

        if (pointVirgule && virgule)
        {
            return false;
        }

        separateur = pointVirgule ? ';' : virgule ? ',' : null;
        return true;
    }

    // Les enregistrements du fichier, numérotés comme les lignes d'un tableur, ou le refus du premier problème : un
    // guillemet mal placé ou jamais fermé, ou plus de lignes que la borne.
    private static (List<(int Numero, List<string> Cellules)> Enregistrements, LectureDuFichier.Refuse? Refus) Decouper(string texte, char? separateur)
    {
        var enregistrements = new List<(int Numero, List<string> Cellules)>();
        var cellules = new List<string>();
        var cellule = new StringBuilder();
        var numero = 1;
        var i = 0;
        var n = texte.Length;
        while (true)
        {
            if (i < n && texte[i] == '"')
            {
                i++;
                while (true)
                {
                    if (i >= n)
                    {
                        return (enregistrements, new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Structure, MessageGuillemetNonFerme(numero)));
                    }

                    if (texte[i] == '"')
                    {
                        if (i + 1 < n && texte[i + 1] == '"')
                        {
                            cellule.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    cellule.Append(texte[i]);
                    i++;
                }

                if (i < n && texte[i] != separateur && texte[i] is not ('\r' or '\n'))
                {
                    return (enregistrements, new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Structure, MessageGuillemetMalPlace(numero)));
                }
            }
            else
            {
                while (i < n && texte[i] != separateur && texte[i] is not ('\r' or '\n'))
                {
                    if (texte[i] == '"')
                    {
                        return (enregistrements, new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Structure, MessageGuillemetMalPlace(numero)));
                    }

                    cellule.Append(texte[i]);
                    i++;
                }
            }

            cellules.Add(cellule.ToString());
            cellule.Clear();
            if (i < n && texte[i] == separateur)
            {
                i++;
                continue;
            }

            enregistrements.Add((numero, cellules));
            cellules = [];
            if (i >= n)
            {
                break;
            }

            // CRLF, LF ou CR seul ; la dernière fin de ligne ne fait pas de ligne de plus.
            i += texte[i] == '\r' && i + 1 < n && texte[i + 1] == '\n' ? 2 : 1;
            if (i >= n)
            {
                break;
            }

            numero++;
            if (numero > LignesMaximales + 1)
            {
                return (enregistrements, new LectureDuFichier.Refuse(SorteDeRefusDuFichier.Lignes, MessageLignes));
            }
        }

        return (enregistrements, null);
    }

    // Les colonnes de l'en-tête, ou le motif de son refus : une colonne vide, inconnue ou répétée, ou poste absente.
    private static (IReadOnlyList<ColonneDuFichier>? Colonnes, string? Refus) LireLEnTete(List<string> cellules)
    {
        var colonnes = new List<ColonneDuFichier>(cellules.Count);
        for (var i = 0; i < cellules.Count; i++)
        {
            var nom = cellules[i].Trim();
            if (nom.Length == 0)
            {
                return (null, MessageColonneVide(i + 1));
            }

            var connue = Noms.Where(paire => string.Equals(paire.Value, nom, StringComparison.OrdinalIgnoreCase)).Select(paire => (ColonneDuFichier?)paire.Key).FirstOrDefault();
            if (connue is not { } colonne)
            {
                return (null, MessageColonneInconnue(i + 1));
            }

            if (colonnes.Contains(colonne))
            {
                return (null, MessageColonneRepetee(i + 1));
            }

            colonnes.Add(colonne);
        }

        return colonnes.Contains(ColonneDuFichier.Poste) ? (colonnes, null) : (null, MessagePosteManquant);
    }

    private static string Format(FormattableString message) => message.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Le second connecteur (#40, ADR-0015) : le fichier CSV. Chaque ligne d'offre bien formée d'un fichier lu fait une
/// offre reçue, aux cellules telles que lues, guillemets décodés, sans rien retoucher ; une colonne absente donne un champ
/// absent. L'empreinte se calcule sur les six champs du format, dans son ordre, quel que soit l'ordre des colonnes du
/// fichier.
/// </summary>
public sealed class ConnecteurDuFichierCsv : IConnecteurDOffres<FichierCsv>
{
    /// <summary>Les champs de l'empreinte d'une ligne, dans l'ordre du format : poste, entreprise, lieu, publieeLe, adresse, texte.</summary>
    public static readonly IReadOnlyList<ChampDeLEmpreinte> ChampsDeLEmpreinte =
    [
        ChampDeLEmpreinte.Poste,
        ChampDeLEmpreinte.Entreprise,
        ChampDeLEmpreinte.Lieu,
        ChampDeLEmpreinte.PublieeLe,
        ChampDeLEmpreinte.Adresse,
        ChampDeLEmpreinte.Texte,
    ];

    public SourceDOffre Source => SourceDOffre.FichierCsv;

    public IReadOnlyList<OffreRecue> Recevoir(FichierCsv entree) => [.. RecevoirLesLignes(entree).Select(ligne => ligne.Offre)];

    /// <summary>
    /// Les offres reçues des lignes bien formées, chacune avec son numéro de ligne, dans l'ordre du fichier. Une ligne qui
    /// n'a pas autant de cellules que l'en-tête n'en fait aucune.
    /// </summary>
    public IReadOnlyList<(int Numero, OffreRecue Offre)> RecevoirLesLignes(FichierCsv entree)
    {
        ArgumentNullException.ThrowIfNull(entree);
        return
        [
            .. entree.Lignes
                .Where(ligne => ligne.Cellules is not null)
                .Select(ligne => (ligne.Numero, new OffreRecue(Source, ChampsDeLEmpreinte, ChampsDe(ligne.Cellules!)))),
        ];
    }

    private static ChampsRecus ChampsDe(IReadOnlyDictionary<ColonneDuFichier, string> cellules)
    {
        string? Cellule(ColonneDuFichier colonne) => cellules.TryGetValue(colonne, out var valeur) ? valeur : null;
        return new ChampsRecus(
            Cellule(ColonneDuFichier.Texte),
            Cellule(ColonneDuFichier.Entreprise),
            Cellule(ColonneDuFichier.Poste),
            Cellule(ColonneDuFichier.Lieu),
            Cellule(ColonneDuFichier.PublieeLe),
            Cellule(ColonneDuFichier.Adresse));
    }
}
