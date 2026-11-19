namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Un seul lancement de Sollicito à la fois sur un dossier de données : le fichier <see cref="NomDuFichier"/>, ouvert
/// sans partage pendant toute la vie de l'API. Windows le referme, et l'efface, quand le processus s'arrête, même tué :
/// le verrou ne reste jamais pris après coup. Sous ce verrou, aucun autre lancement ne migre la base du dossier.
/// </summary>
public sealed class VerrouDeLInstance : IDisposable
{
    /// <summary>Le nom du fichier du verrou, dans le dossier de données.</summary>
    public const string NomDuFichier = "sollicito.verrou";

    // ERROR_SHARING_VIOLATION : le fichier est déjà ouvert sans partage, par un autre lancement.
    private const int PartageRefuse = unchecked((int)0x80070020);

    private readonly FileStream _fichier;

    private VerrouDeLInstance(FileStream fichier) => _fichier = fichier;

    /// <summary>Prend le verrou du dossier, ou lève <see cref="BaseDejaOuverte"/> si un autre lancement le tient.</summary>
    public static VerrouDeLInstance Prendre(DossierDeLaBase dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        try
        {
            return new VerrouDeLInstance(new FileStream(
                Path.Combine(dossier.Chemin, NomDuFichier),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose));
        }
        catch (IOException erreur) when (erreur.HResult == PartageRefuse)
        {
            // Sans l'erreur d'origine : son message cite le chemin du fichier, donc le nom du compte Windows, et l'hôte
            // écrit dans le journal toute la chaîne des exceptions d'un lancement qui échoue.
            throw new BaseDejaOuverte();
        }
    }

    /// <summary>Rend le verrou.</summary>
    public void Dispose() => _fichier.Dispose();
}

/// <summary>Un autre lancement de Sollicito utilise déjà ce dossier de données.</summary>
public sealed class BaseDejaOuverte : InvalidOperationException
{
    /// <summary>Le refus, sans le chemin du dossier.</summary>
    public BaseDejaOuverte()
        : base("Un autre lancement de Sollicito utilise déjà ce dossier de données.")
    {
    }
}
