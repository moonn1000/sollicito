using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Sollicito.Api.Interface;

/// <summary>
/// Les fichiers de l'interface React construite (<c>index.html</c>, scripts, styles), que l'API sert à la même
/// origine qu'elle (ADR-0003, « Montage »). Ils sont publics et ne portent aucune donnée : ils sont servis sans
/// jeton ni contrôle d'<c>Origin</c>, puisque la page les charge avant de recevoir le jeton, et seul leur en-tête
/// <c>Host</c> est contrôlé (ADR-0003, « Règles d'accès »). Rien n'est servi sous <c>/api</c>, qui reste aux
/// routes de l'API. Seule la fenêtre hôte les fournit : lancée seule, l'API ne sert aucun fichier.
/// </summary>
public sealed partial class FichiersDeLInterface
{
    /// <summary>Les chemins des routes de l'API : aucun fichier n'y est servi, même s'il en existait un.</summary>
    public static readonly PathString CheminsDeLApi = new("/api");

    /// <summary>Les fichiers de l'interface, à servir.</summary>
    public FichiersDeLInterface(IFileProvider fichiers)
    {
        ArgumentNullException.ThrowIfNull(fichiers);
        Fichiers = fichiers;
    }

    /// <summary>Le fournisseur des fichiers : ceux embarqués dans l'exe, ou ceux d'un test.</summary>
    public IFileProvider Fichiers { get; }

    /// <summary>
    /// Vrai si le chemin peut désigner un fichier de l'interface : hors de <c>/api</c>, et fait de segments simples
    /// (lettres, chiffres, <c>-</c>, <c>_</c>, et des points entre eux). Ni antislash, ni segment vide (<c>//</c>),
    /// ni <c>.</c> ou <c>..</c>, ni point final, ni deux-points, ni tilde : les fournisseurs de fichiers lisent ces
    /// formes à leur façon (un antislash y sépare deux dossiers, un point final disparaît sous Windows), et l'une
    /// d'elles ferait servir sans jeton un fichier rangé sous <c>/api</c>. Le chemin arrive déjà décodé par Kestrel :
    /// <c>%5C</c> y est un antislash, refusé, et <c>/%69ndex.html</c> y est <c>/index.html</c>, servi.
    /// </summary>
    public static bool PeutEtreServi(PathString chemin) =>
        chemin.HasValue
        && CheminSimple().IsMatch(chemin.Value)
        && !chemin.StartsWithSegments(CheminsDeLApi, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sert les fichiers, si un fournisseur est enregistré dans les services de l'API. Une requête sans fichier
    /// correspondant continue son chemin, et passe donc par le contrôle du jeton.
    /// </summary>
    internal static void Servir(WebApplication app)
    {
        if (app.Services.GetService<FichiersDeLInterface>() is not { } interfaceServie)
        {
            return;
        }

        var fichiers = interfaceServie.Fichiers;
        app.UseWhen(
            http => PeutEtreServi(http.Request.Path),
            branche => branche
                .UseDefaultFiles(new DefaultFilesOptions { FileProvider = fichiers, DefaultFileNames = ["index.html"] })
                .UseStaticFiles(new StaticFileOptions { FileProvider = fichiers }));
    }

    // \z, et non $ : en .NET, $ admet aussi la place juste avant un saut de ligne final.
    [GeneratedRegex(@"^(/[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)*)*/?\z", RegexOptions.CultureInvariant)]
    private static partial Regex CheminSimple();
}
