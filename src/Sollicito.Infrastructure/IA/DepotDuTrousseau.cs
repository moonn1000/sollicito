using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Sollicito.Infrastructure.IA;

/// <summary>
/// Le dépôt du trousseau de Data Protection (#43, ADR-0016) : un fichier XML par clé du trousseau, dans le dossier
/// <c>Trousseau</c> du dossier de données. Data Protection ne lui remet que des clés déjà chiffrées par DPAPI ; il ne voit
/// jamais une clé en clair. À la différence du dépôt en fichiers de Data Protection, un fichier illisible (abîmé, tenu,
/// trop gros) est ignoré, comme une clé d'un autre compte : le trousseau reste utilisable, et une nouvelle clé d'IA s'y
/// enregistre. Le dossier ne se crée qu'à l'écriture de la première clé du trousseau, et chaque clé s'y écrit d'un bloc.
/// </summary>
internal sealed class DepotDuTrousseau(string dossier) : IXmlRepository
{
    // Une clé du trousseau tient en quelques kilo-octets ; au-delà, le fichier n'est pas une des siennes.
    private const int TailleMaximale = 64 * 1024;

    private static readonly XmlReaderSettings Lecture = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Le dossier du trousseau.</summary>
    internal string Dossier => dossier;

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        string[] fichiers;
        try
        {
            fichiers = Directory.Exists(dossier) ? Directory.GetFiles(dossier, "*.xml") : [];
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var elements = new List<XElement>();
        foreach (var fichier in fichiers.Order(StringComparer.Ordinal))
        {
            if (LireUnElement(fichier) is { } element)
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        Directory.CreateDirectory(dossier);
        var final = Path.Combine(dossier, NomSur(friendlyName) + ".xml");
        var provisoire = Path.Combine(dossier, $"{Guid.NewGuid():N}.provisoire");
        try
        {
            using (var flux = new FileStream(provisoire, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                element.Save(flux);
                flux.Flush(flushToDisk: true);
            }

            File.Move(provisoire, final, overwrite: false);
        }
        finally
        {
            File.Delete(provisoire);
        }
    }

    // Une clé du trousseau, ou rien si son fichier ne se lit pas.
    private static XElement? LireUnElement(string fichier)
    {
        try
        {
            using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (flux.Length > TailleMaximale)
            {
                return null;
            }

            using var lecteur = XmlReader.Create(flux, Lecture);
            return XElement.Load(lecteur);
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    // Le nom que Data Protection propose (« key-<identifiant> »), s'il n'est fait que de lettres, de chiffres et de tirets.
    private static string NomSur(string? nom) =>
        nom is { Length: > 0 and <= 64 } && nom.All(caractere => char.IsAsciiLetterOrDigit(caractere) || caractere == '-')
            ? nom
            : $"key-{Guid.NewGuid():D}";
}
