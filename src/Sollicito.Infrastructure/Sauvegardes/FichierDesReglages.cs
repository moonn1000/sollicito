using System.Text;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// Le fichier des réglages, <c>reglages.json</c>, dans le dossier de données, hors de la base (ADR-0008). Il s'écrit d'un
/// bloc : un fichier provisoire, puis un renommage, pour qu'un arrêt en pleine écriture ne laisse jamais un fichier à
/// moitié écrit.
/// </summary>
public sealed class FichierDesReglages(DossierDeLaBase dossier)
{
    /// <summary>Lit les réglages ; un fichier absent rend ceux par défaut, un fichier illisible aussi, en le disant.</summary>
    public LectureDesReglages Lire()
    {
        var texte = LireLeTexte(out var present);
        return present && texte is null
            ? new LectureDesReglages(ReglagesDeLApplication.ParDefaut, Lisible: false)
            : ReglagesDeLApplication.Lire(texte);
    }

    /// <summary>
    /// Écrit les réglages, en gardant ceux que le fichier lisible d'avant portait en plus. Le renommage se reprend tant
    /// qu'un autre programme, comme un antivirus, tient le fichier un instant.
    /// </summary>
    public async Task EcrireAsync(ReglagesDeLApplication reglages)
    {
        ArgumentNullException.ThrowIfNull(reglages);

        var texte = reglages.Ecrire(LireLeTexte(out _));
        var provisoire = $"{dossier.FichierDesReglages}.{Guid.NewGuid():N}.provisoire";
        try
        {
            File.WriteAllText(provisoire, texte, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await CopiesVerifiees.ReessayerAsync(() => File.Move(provisoire, dossier.FichierDesReglages, overwrite: true));
        }
        finally
        {
            File.Delete(provisoire);
        }
    }

    // Le texte du fichier, ou null s'il est absent, trop gros ou illisible (présent dit alors s'il existe).
    private string? LireLeTexte(out bool present)
    {
        var fichier = new FileInfo(dossier.FichierDesReglages);
        present = fichier.Exists;
        if (!present)
        {
            return null;
        }

        try
        {
            if (fichier.Length > ReglagesDeLApplication.TailleMaximale)
            {
                return null;
            }

            return File.ReadAllText(fichier.FullName, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }
}
