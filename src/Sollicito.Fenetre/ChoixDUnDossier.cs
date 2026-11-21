namespace Sollicito.Fenetre;

/// <summary>Ce qui fait choisir un dossier à l'utilisateur : le dialogue de Windows dans la version publiée.</summary>
internal interface IChoixDUnDossier
{
    /// <summary>Le dossier choisi, en chemin absolu, ou <c>null</c> si l'utilisateur a annulé.</summary>
    string? Choisir(IWin32Window proprietaire, string dossierActuel);
}

/// <summary>
/// Le dialogue de dossier de Windows (#26, ADR-0008) : le seul endroit d'où vient le dossier des sauvegardes. Il ne rend
/// qu'un dossier du système de fichiers, qui existe ; un dossier en réseau ou synchronisé reste le choix de
/// l'utilisateur.
/// </summary>
internal sealed class ChoixParLeDialogue : IChoixDUnDossier
{
    public string? Choisir(IWin32Window proprietaire, string dossierActuel)
    {
        using var dialogue = new FolderBrowserDialog
        {
            Description = "Choisissez le dossier des sauvegardes de Sollicito. Les copies de votre base n'y sont pas chiffrées : "
                + "toute personne qui accède à ce dossier peut les lire.",
            UseDescriptionForTitle = false,
            ShowNewFolderButton = true,
            InitialDirectory = dossierActuel,
            AutoUpgradeEnabled = true,
        };
        return dialogue.ShowDialog(proprietaire) == DialogResult.OK && dialogue.SelectedPath.Length > 0 ? dialogue.SelectedPath : null;
    }
}
