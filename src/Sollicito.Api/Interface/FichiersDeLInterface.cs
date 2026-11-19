using Microsoft.Extensions.FileProviders;

namespace Sollicito.Api.Interface;

/// <summary>
/// Les fichiers de l'interface React construite (<c>index.html</c>, scripts, styles), que l'API sert à la même
/// origine qu'elle (ADR-0003, « Montage »). Ils sont publics et ne portent aucune donnée : ils sont servis sans
/// jeton ni contrôle d'<c>Origin</c>, puisque la page les charge avant de recevoir le jeton, et seul leur en-tête
/// <c>Host</c> est contrôlé (ADR-0003, « Règles d'accès »). Rien n'est servi sous <c>/api</c>, qui reste aux
/// routes de l'API. Seule la fenêtre hôte les fournit : lancée seule, l'API ne sert aucun fichier.
/// </summary>
public sealed class FichiersDeLInterface
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
            http => !http.Request.Path.StartsWithSegments(CheminsDeLApi),
            branche => branche
                .UseDefaultFiles(new DefaultFilesOptions { FileProvider = fichiers, DefaultFileNames = ["index.html"] })
                .UseStaticFiles(new StaticFileOptions { FileProvider = fichiers }));
    }
}
