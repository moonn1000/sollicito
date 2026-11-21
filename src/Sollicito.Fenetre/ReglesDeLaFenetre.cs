using System.Text.Json;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Fenetre;

/// <summary>
/// Les règles de la fenêtre (ADR-0001, D9, « Fenêtre » ; ADR-0003, « Durcissement de la fenêtre ») : ce qui reste
/// dans la fenêtre, ce qui s'ouvre dans le navigateur par défaut, quel message reçoit le jeton. Elles ne dépendent
/// pas de WebView2 : la fenêtre les applique à ses événements.
/// </summary>
internal static class ReglesDeLaFenetre
{
    /// <summary>
    /// Vrai si l'adresse est de l'origine de l'interface : même schéma, même hôte, même port, et sans identifiants.
    /// Une adresse relative, <c>about:</c>, <c>data:</c>, <c>blob:</c> ou <c>file:</c> n'en est pas.
    /// </summary>
    public static bool EstDeLOrigine(string? adresse, Uri origine) =>
        Uri.TryCreate(adresse, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, origine.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, origine.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == origine.Port
        && uri.UserInfo.Length == 0;

    /// <summary>
    /// Le lien qu'une nouvelle fenêtre ouvre dans le navigateur par défaut, ou <c>null</c> : seulement une adresse
    /// absolue en <c>http</c> ou <c>https</c>, sans identifiants, et seulement si la demande vient d'un geste de
    /// l'utilisateur (<c>IsUserInitiated</c>) : un clic, ou un script lancé pendant l'activation que ce geste ouvre,
    /// même par une minuterie. L'activation expire au bout d'environ cinq secondes.
    /// </summary>
    public static Uri? LienPourLeNavigateur(string? adresse, bool demandeParLUtilisateur)
    {
        if (!demandeParLUtilisateur || !Uri.TryCreate(adresse, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var schemaDuWeb = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        return schemaDuWeb && uri.UserInfo.Length == 0 && uri.Host.Length > 0 ? uri : null;
    }

    /// <summary>
    /// Vrai si le message vient de l'interface (la source est de son origine) et signale qu'elle est prête : la
    /// fenêtre lui remet alors le jeton. Le message ne porte que son type : un champ de plus, un message mal formé ou
    /// d'un autre type ne reçoit rien (#88).
    /// </summary>
    public static bool DemandeLeJeton(string? source, string? messageEnJson, Uri origine) =>
        EstUnMessageDeLInterface(source, messageEnJson, origine, "prete", champs: 1);

    /// <summary>
    /// Vrai si le message vient de l'interface et demande le choix du dossier des sauvegardes (#26) : la fenêtre ouvre
    /// alors le dialogue de dossier de Windows. Sous le même contrôle de la source que le canal du jeton. Le message ne
    /// porte que son type, aucun chemin : seul le dialogue en donne un. Un champ de plus, répété compris, un message mal
    /// formé ou d'un autre type n'ouvre rien (#88).
    /// </summary>
    public static bool DemandeLeChoixDuDossier(string? source, string? messageEnJson, Uri origine) =>
        EstUnMessageDeLInterface(source, messageEnJson, origine, "choisirLeDossierDesSauvegardes", champs: 1);

    /// <summary>
    /// Vrai si le message vient de l'interface et demande la restauration d'une copie gardée (#28, ADR-0012) : la fenêtre
    /// ouvre alors son dialogue de confirmation. Sous le même contrôle de la source que le canal du jeton. Le message
    /// porte son type et l'identifiant de la copie, seize caractères hexadécimaux en minuscules, et rien d'autre : jamais
    /// un chemin. Un champ de plus, répété compris, un identifiant d'une autre forme, un message mal formé ou d'un autre
    /// type n'ouvre rien.
    /// </summary>
    public static bool DemandeLaRestauration(string? source, string? messageEnJson, Uri origine, out string copie)
    {
        copie = "";
        if (!EstUnMessageDeLInterface(source, messageEnJson, origine, "restaurerUneSauvegarde", champs: 2))
        {
            return false;
        }

        using var message = JsonDocument.Parse(messageEnJson!, new JsonDocumentOptions { MaxDepth = 8 });
        if (message.RootElement.TryGetProperty("copie", out var identifiant)
            && identifiant.ValueKind == JsonValueKind.String
            && IdentifiantsDesCopies.AEnLaForme(identifiant.GetString()))
        {
            copie = identifiant.GetString()!;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Le message qui rend à l'interface où en est la restauration de cette copie, sans chemin ni donnée : en cours après
    /// le oui, puis son issue.
    /// </summary>
    public static string MessageDeLaRestauration(string copie, IssueDansLaFenetre issue) => JsonSerializer.Serialize(new
    {
        type = "restauration",
        copie,
        issue = issue switch
        {
            IssueDansLaFenetre.EnCours => "enCours",
            IssueDansLaFenetre.Annulee => "annulee",
            IssueDansLaFenetre.Restauree => "restauree",
            IssueDansLaFenetre.RestaureeDepuisUneBaseAbimee => "restaureeDepuisUneBaseAbimee",
            IssueDansLaFenetre.Abimee => "abimee",
            IssueDansLaFenetre.Etrangere => "etrangere",
            IssueDansLaFenetre.PlusRecente => "plusRecente",
            IssueDansLaFenetre.Introuvable => "introuvable",
            IssueDansLaFenetre.Changee => "changee",
            IssueDansLaFenetre.BaseChangee => "baseChangee",
            IssueDansLaFenetre.Occupee => "occupee",
            IssueDansLaFenetre.CopieDAvantImpossible => "copieDAvantImpossible",
            _ => "impossible",
        },
    });

    /// <summary>L'issue d'une restauration, telle que la fenêtre la rend à la page.</summary>
    public static IssueDansLaFenetre DansLaFenetre(IssueDeLaRestauration issue) => issue switch
    {
        IssueDeLaRestauration.Restauree => IssueDansLaFenetre.Restauree,
        IssueDeLaRestauration.RestaureeDepuisUneBaseAbimee => IssueDansLaFenetre.RestaureeDepuisUneBaseAbimee,
        IssueDeLaRestauration.Abimee => IssueDansLaFenetre.Abimee,
        IssueDeLaRestauration.Etrangere => IssueDansLaFenetre.Etrangere,
        IssueDeLaRestauration.PlusRecente => IssueDansLaFenetre.PlusRecente,
        IssueDeLaRestauration.Introuvable => IssueDansLaFenetre.Introuvable,
        IssueDeLaRestauration.Changee => IssueDansLaFenetre.Changee,
        IssueDeLaRestauration.BaseChangee => IssueDansLaFenetre.BaseChangee,
        IssueDeLaRestauration.Occupee => IssueDansLaFenetre.Occupee,
        IssueDeLaRestauration.CopieDAvantImpossible => IssueDansLaFenetre.CopieDAvantImpossible,
        _ => IssueDansLaFenetre.Impossible,
    };

    /// <summary>
    /// Vrai si le message vient de l'interface et demande le brouillon d'une relance (#14, ADR-0011) : la fenêtre
    /// l'ouvre alors dans la messagerie de l'utilisateur. Sous le même contrôle de la source que le canal du jeton. Le
    /// message porte son type et l'identifiant de la relance, un entier positif, et rien d'autre : un champ de plus, et
    /// rien ne s'ouvre, pas plus que pour un message mal formé, d'un autre type, ou sans identifiant.
    /// </summary>
    public static bool DemandeLeBrouillon(string? source, string? messageEnJson, Uri origine, out int relance)
    {
        relance = 0;
        if (!EstUnMessageDeLInterface(source, messageEnJson, origine, "ouvrirLeBrouillon"))
        {
            return false;
        }

        using var message = JsonDocument.Parse(messageEnJson!, new JsonDocumentOptions { MaxDepth = 8 });
        // Deux champs, dont le type et la relance : la forme est stricte, un champ répété compte aussi.
        return message.RootElement.EnumerateObject().Count() == 2
            && message.RootElement.TryGetProperty("relance", out var identifiant)
            && identifiant.ValueKind == JsonValueKind.Number
            && identifiant.TryGetInt32(out relance)
            && relance > 0;
    }

    /// <summary>Le message qui remet le jeton à l'interface.</summary>
    public static string MessageDuJeton(string jeton) => JsonSerializer.Serialize(new { type = "jeton", jeton });

    /// <summary>
    /// Le message qui rend à l'interface l'issue de l'ouverture du brouillon de cette relance, sans rien du brouillon :
    /// ni adresse, ni texte.
    /// </summary>
    public static string MessageDuBrouillon(int relance, IssueDuBrouillon issue) => JsonSerializer.Serialize(new
    {
        type = "brouillonDeRelance",
        relance,
        issue = issue switch
        {
            IssueDuBrouillon.Ouvert => "ouvert",
            IssueDuBrouillon.Impossible => "impossible",
            _ => "refuse",
        },
    });

    /// <summary>Le message qui rend à l'interface l'issue du choix du dossier, sans chemin : la page relit l'état par l'API.</summary>
    public static string MessageDuChoixDuDossier(IssueDuChoixDuDossier issue) => JsonSerializer.Serialize(new
    {
        type = "dossierDesSauvegardes",
        issue = issue switch
        {
            IssueDuChoixDuDossier.Choisi => "choisi",
            IssueDuChoixDuDossier.Annule => "annule",
            _ => "refuse",
        },
    });

    // Un message de l'interface, du type attendu : une source de l'origine, un objet JSON, un type en texte, et, si
    // `champs` est donné, exactement ce nombre de champs, un champ répété comptant pour deux.
    private static bool EstUnMessageDeLInterface(string? source, string? messageEnJson, Uri origine, string typeAttendu, int? champs = null)
    {
        if (!EstDeLOrigine(source, origine) || string.IsNullOrEmpty(messageEnJson))
        {
            return false;
        }

        try
        {
            using var message = JsonDocument.Parse(messageEnJson, new JsonDocumentOptions { MaxDepth = 8 });
            return message.RootElement.ValueKind == JsonValueKind.Object
                && message.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.ValueEquals(typeAttendu)
                && (champs is null || message.RootElement.EnumerateObject().Count() == champs);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>L'issue du choix du dossier des sauvegardes, telle que la fenêtre la rend à l'interface.</summary>
internal enum IssueDuChoixDuDossier
{
    /// <summary>Une copie s'est faite dans le dossier choisi : c'est désormais celui des sauvegardes.</summary>
    Choisi,

    /// <summary>L'utilisateur a fermé le dialogue sans choisir : rien ne change.</summary>
    Annule,

    /// <summary>La copie n'a pas pu se faire dans le dossier choisi : l'ancien reste.</summary>
    Refuse,
}

/// <summary>Où en est une restauration, telle que la fenêtre le rend à l'interface (#28).</summary>
internal enum IssueDansLaFenetre
{
    /// <summary>L'utilisateur a dit oui : la restauration est en cours.</summary>
    EnCours,

    /// <summary>L'utilisateur a fermé le dialogue sans restaurer : rien n'a changé.</summary>
    Annulee,

    /// <summary>La base est remplacée par la copie ; sa copie d'avant se restaure.</summary>
    Restauree,

    /// <summary>La base abîmée est remplacée par la copie ; ses fichiers sont gardés tels quels.</summary>
    RestaureeDepuisUneBaseAbimee,

    /// <summary>La copie ne se relit pas saine.</summary>
    Abimee,

    /// <summary>Le fichier n'est pas une copie que cette version sait lire.</summary>
    Etrangere,

    /// <summary>La copie vient d'une version plus récente.</summary>
    PlusRecente,

    /// <summary>La copie, ou son dossier, a disparu.</summary>
    Introuvable,

    /// <summary>La copie a changé depuis l'affichage de la liste.</summary>
    Changee,

    /// <summary>L'état de la base a changé depuis la confirmation.</summary>
    BaseChangee,

    /// <summary>Une autre restauration, ou des requêtes en cours, occupent la base.</summary>
    Occupee,

    /// <summary>La base en cours n'a pas pu se copier : elle n'a pas été remplacée.</summary>
    CopieDAvantImpossible,

    /// <summary>Une erreur, comme la place qui manque : rien n'a changé.</summary>
    Impossible,
}
