namespace Sollicito.Fenetre;

/// <summary>
/// Le dossier de données de Sollicito (ADR-0001, D10) : <c>%LOCALAPPDATA%\Sollicito</c>, sous le profil de
/// l'utilisateur, hors du dossier de l'exe et hors du dépôt. Il garde la base, les sauvegardes et le profil du
/// navigateur intégré.
/// </summary>
internal static class DossierDeDonnees
{
    /// <summary>Le nom du dossier, sous <c>%LOCALAPPDATA%</c>.</summary>
    public const string Nom = "Sollicito";

    /// <summary>Le sous-dossier du profil du navigateur intégré (ADR-0003, « Dossier de profil »).</summary>
    public const string NomDuProfil = "WebView2";

    /// <summary>Le dossier de données de l'utilisateur courant.</summary>
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

    /// <summary>Le profil du navigateur intégré, dans le dossier de données.</summary>
    public static string ProfilDuNavigateur(string dossierDeDonnees) => Path.Combine(dossierDeDonnees, NomDuProfil);
}
