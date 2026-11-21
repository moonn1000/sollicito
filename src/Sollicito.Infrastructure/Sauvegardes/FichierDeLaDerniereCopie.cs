using System.Globalization;
using System.Text;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// L'instant de la dernière copie du jour réussie, gardé d'une séance à l'autre dans <c>sauvegardes.json</c>, dans le
/// dossier de données (ADR-0008) : relancé alors que le dossier choisi manque, Sollicito dit encore depuis quand aucune
/// copie n'a réussi. Le fichier ne porte qu'un instant, sans chemin ; illisible ou absent, il ne dit rien.
/// </summary>
public sealed class FichierDeLaDerniereCopie(DossierDeLaBase dossier)
{
    /// <summary>Le nom du fichier, dans le dossier de données.</summary>
    public const string NomDuFichier = "sauvegardes.json";

    private const string Propriete = "derniereCopie";

    private string Chemin => Path.Combine(dossier.Chemin, NomDuFichier);

    /// <summary>L'instant gardé, en UTC, ou <c>null</c> si le fichier est absent ou illisible.</summary>
    public DateTime? Lire()
    {
        try
        {
            var fichier = new FileInfo(Chemin);
            if (!fichier.Exists || fichier.Length > 4096)
            {
                return null;
            }

            var texte = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Chemin, Encoding.UTF8));
            return texte?[Propriete]?.GetValue<string>() is { } valeur
                && DateTime.TryParse(valeur, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant)
                ? DateTime.SpecifyKind(instant, DateTimeKind.Utc)
                : null;
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Garde l'instant, d'un bloc : un fichier provisoire, puis un renommage.</summary>
    public async Task EcrireAsync(DateTime instant)
    {
        var texte = new System.Text.Json.Nodes.JsonObject
        {
            [Propriete] = instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        }.ToJsonString();
        var provisoire = $"{Chemin}.{Guid.NewGuid():N}.provisoire";
        try
        {
            await File.WriteAllTextAsync(provisoire, texte, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await CopiesVerifiees.ReessayerAsync(() => File.Move(provisoire, Chemin, overwrite: true));
        }
        finally
        {
            File.Delete(provisoire);
        }
    }
}
