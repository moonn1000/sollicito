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
    public static bool DemandeLeJeton(string? source, string? messageEnJson, Uri origine)
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
                && type.ValueEquals("prete");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Le message qui remet le jeton à l'interface.</summary>
    public static string MessageDuJeton(string jeton) => JsonSerializer.Serialize(new { type = "jeton", jeton });
}
