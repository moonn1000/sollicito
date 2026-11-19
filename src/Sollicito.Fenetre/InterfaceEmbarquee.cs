using Microsoft.Extensions.FileProviders;

namespace Sollicito.Fenetre;

/// <summary>
/// Les fichiers de l'interface React construite (<c>interface/dist</c>), embarqués dans l'exe à sa construction
/// (Sollicito.Fenetre.csproj) : l'API les sert à la même origine qu'elle, et rien ne se lit à côté de l'exe.
/// </summary>
internal static class InterfaceEmbarquee
{
    private const string Racine = "interface";

    /// <summary>Les fichiers embarqués.</summary>
    public static IFileProvider Fichiers()
    {
        try
        {
            return new ManifestEmbeddedFileProvider(typeof(InterfaceEmbarquee).Assembly, Racine);
        }
        catch (InvalidOperationException)
        {
            // Aucun fichier embarqué : l'interface n'était pas construite.
            return new NullFileProvider();
        }
    }

    /// <summary>Vrai si l'interface est là : sa page d'entrée au moins.</summary>
    public static bool EstPresente(IFileProvider fichiers) => fichiers.GetFileInfo("index.html").Exists;
}
