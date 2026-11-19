namespace Sollicito.Api.Securite;

/// <summary>
/// Les en-têtes de sécurité, posés sur chaque réponse, refus compris (ADR-0001, D9, « Fenêtre » ; ADR-0003) :
/// <list type="bullet">
/// <item>une politique de sécurité du contenu stricte, envoyée en en-tête : scripts, styles, images, polices et
/// connexions de l'origine de l'interface seulement ; aucun cadre, aucun worker, aucun objet, aucun formulaire
/// envoyé, aucune balise <c>base</c> ; et la page ne s'affiche dans le cadre d'aucune autre ;</item>
/// <item><c>nosniff</c> : un fichier ne s'exécute que sous le type que le serveur déclare ;</item>
/// <item><c>no-referrer</c> : l'adresse d'une page de l'interface ne part jamais dans l'en-tête <c>Referer</c> ;</item>
/// <item><c>no-store</c> : rien ne se garde dans le cache du profil du navigateur intégré, ni un fichier, ni une
/// réponse de l'API.</item>
/// </list>
/// </summary>
internal sealed class EnTetesDeSecurite(RequestDelegate suite)
{
    /// <summary>
    /// La politique de sécurité du contenu. <c>default-src 'none'</c> couvre aussi les objets, les médias et le
    /// manifeste. Les workers se rabattraient sur <c>script-src</c>, donc sur l'origine : <c>worker-src</c> les
    /// interdit. <c>base-uri</c>, <c>form-action</c> et <c>frame-ancestors</c> ne se déduisent pas de
    /// <c>default-src</c>, d'où leur mention.
    /// </summary>
    internal const string PolitiqueDuContenu =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; "
        + "frame-src 'none'; worker-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext http)
    {
        Poser(http.Response.Headers);
        return suite(http);
    }

    /// <summary>Pose les en-têtes de sécurité sur une réponse ; la réponse à une exception les reçoit de nouveau.</summary>
    internal static void Poser(IHeaderDictionary enTetes)
    {
        enTetes.ContentSecurityPolicy = PolitiqueDuContenu;
        enTetes.XContentTypeOptions = "nosniff";
        enTetes["Referrer-Policy"] = "no-referrer";
        enTetes.CacheControl = "no-store";
    }
}
