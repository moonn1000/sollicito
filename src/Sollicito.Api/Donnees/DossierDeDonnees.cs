namespace Sollicito.Api.Donnees;

/// <summary>
/// Le dossier de données de Sollicito (ADR-0001, D10) : <c>%LOCALAPPDATA%\Sollicito</c>, sous le profil de
/// l'utilisateur, hors du dossier de l'exe et hors du dépôt, développement compris. Il garde la base, les sauvegardes
/// et le profil du navigateur intégré. L'API lancée seule y range sa base ; la fenêtre hôte donne le sien à l'API
/// qu'elle construit.
/// </summary>
public static class DossierDeDonnees
{
    /// <summary>Le nom du dossier, sous <c>%LOCALAPPDATA%</c>.</summary>
    public const string Nom = "Sollicito";

    /// <summary>Le dossier de données de l'utilisateur courant. Rien n'est créé.</summary>
    public static string DeLUtilisateur() => Sous(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolderOption.DoNotVerify));

    /// <summary>Le dossier de données sous un dossier <c>%LOCALAPPDATA%</c> donné.</summary>
    public static string Sous(string dossierLocalDesApplications)
    {
        // Un dossier introuvable rendrait un chemin relatif, donc le dossier courant : on s'arrête plutôt.
        if (!Path.IsPathFullyQualified(dossierLocalDesApplications))
        {
            throw new InvalidOperationException("Le dossier %LOCALAPPDATA% de l'utilisateur est introuvable.");
        }

        return Path.Combine(dossierLocalDesApplications, Nom);
    }
}
