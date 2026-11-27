using Sollicito.Application.Export;

namespace Sollicito.Fenetre;

/// <summary>Le fichier où enregistrer un export, tel que l'utilisateur l'a choisi.</summary>
/// <param name="Chemin">Le chemin absolu du fichier.</param>
/// <param name="Remplacer">
/// Vrai si un fichier de ce nom existait, et que l'utilisateur a accepté de le remplacer : sans ce oui, aucun fichier ne se
/// remplace, même apparu entre-temps.
/// </param>
internal sealed record FichierChoisi(string Chemin, bool Remplacer);

/// <summary>
/// Ce qui fait choisir le fichier d'un export (#49, ADR-0020) : le dialogue « Enregistrer sous » de Windows dans la version
/// publiée. Il est le seul endroit d'où vient le chemin : la page n'en envoie aucun.
/// </summary>
internal interface IChoixDUnFichierDExport
{
    /// <summary>Le fichier choisi, ou <c>null</c> si l'utilisateur a annulé.</summary>
    FichierChoisi? Choisir(IWin32Window proprietaire, FormatDExport format, string nomPropose);
}

/// <summary>Les textes du dialogue de l'export, mot pour mot (#49).</summary>
internal static class TextesDeLExport
{
    /// <summary>Le titre du dialogue.</summary>
    public const string Titre = "Exporter vos données";

    /// <summary>Le filtre du dialogue, pour cette forme : un seul type de fichier.</summary>
    public static string Filtre(FormatDExport format) => format switch
    {
        FormatDExport.Json => "Export JSON (*.json)|*.json",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}

/// <summary>
/// Le dialogue « Enregistrer sous » de Windows. Il demande lui-même avant de remplacer un fichier existant
/// (<see cref="FileDialog"/>, <c>OverwritePrompt</c>) : un chemin qu'il rend, et qui existe, a donc reçu ce oui. Un dossier
/// en réseau ou synchronisé reste le choix de l'utilisateur.
/// </summary>
internal sealed class ChoixDUnFichierParLeDialogue : IChoixDUnFichierDExport
{
    public FichierChoisi? Choisir(IWin32Window proprietaire, FormatDExport format, string nomPropose)
    {
        using var dialogue = Construire(format, nomPropose);
        if (dialogue.ShowDialog(proprietaire) != DialogResult.OK || dialogue.FileName.Length == 0)
        {
            return null;
        }

        return new FichierChoisi(dialogue.FileName, Remplacer: File.Exists(dialogue.FileName));
    }

    /// <summary>Le dialogue de cette forme, réglé, sans l'ouvrir.</summary>
    public static SaveFileDialog Construire(FormatDExport format, string nomPropose) => new()
    {
        Title = TextesDeLExport.Titre,
        Filter = TextesDeLExport.Filtre(format),
        DefaultExt = FormatDeLExport.Extension(format).TrimStart('.'),
        AddExtension = true,
        FileName = nomPropose,
        OverwritePrompt = true,
        CreatePrompt = false,
        CheckPathExists = true,
        // Le dossier courant du processus ne suit pas le dossier choisi.
        RestoreDirectory = true,
        AutoUpgradeEnabled = true,
    };
}
