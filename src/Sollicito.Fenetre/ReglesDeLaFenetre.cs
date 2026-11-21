using System.Text.Json;

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
    /// fenêtre lui remet alors le jeton. Un message mal formé ou d'un autre type ne reçoit rien.
    /// </summary>
    public static bool DemandeLeJeton(string? source, string? messageEnJson, Uri origine) =>
        EstUnMessageDeLInterface(source, messageEnJson, origine, "prete");

    /// <summary>
    /// Vrai si le message vient de l'interface et demande le choix du dossier des sauvegardes (#26) : la fenêtre ouvre
    /// alors le dialogue de dossier de Windows. Sous le même contrôle de la source que le canal du jeton ; un message
    /// mal formé ou d'un autre type n'ouvre rien. Le message ne porte aucun chemin : seul le dialogue en donne un.
    /// </summary>
    public static bool DemandeLeChoixDuDossier(string? source, string? messageEnJson, Uri origine) =>
        EstUnMessageDeLInterface(source, messageEnJson, origine, "choisirLeDossierDesSauvegardes");

    /// <summary>
    /// Vrai si le message vient de l'interface et demande le brouillon d'une relance (#14, ADR-0011) : la fenêtre
    /// l'ouvre alors dans la messagerie de l'utilisateur. Sous le même contrôle de la source que le canal du jeton. Le
    /// message ne porte que l'identifiant de la relance, un entier positif : un autre champ n'est jamais lu, et un message
    /// mal formé, d'un autre type, ou sans identifiant, n'ouvre rien.
    /// </summary>
    public static bool DemandeLeBrouillon(string? source, string? messageEnJson, Uri origine, out int relance)
    {
        relance = 0;
        if (!EstUnMessageDeLInterface(source, messageEnJson, origine, "ouvrirLeBrouillon"))
        {
            return false;
        }

        using var message = JsonDocument.Parse(messageEnJson!, new JsonDocumentOptions { MaxDepth = 8 });
        return message.RootElement.TryGetProperty("relance", out var identifiant)
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

    // Un message de l'interface, du type attendu : une source de l'origine, un objet JSON, un type en texte.
    private static bool EstUnMessageDeLInterface(string? source, string? messageEnJson, Uri origine, string typeAttendu)
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
                && type.ValueEquals(typeAttendu);
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
