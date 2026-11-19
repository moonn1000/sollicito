namespace Sollicito.Fenetre;

/// <summary>
/// Le dossier de données de Sollicito (ADR-0001, D10) : <c>%LOCALAPPDATA%\Sollicito</c>, sous le profil de
/// l'utilisateur, hors du dossier de l'exe et hors du dépôt. Il garde la base, les sauvegardes et le profil du
/// navigateur intégré. Son emplacement est celui de l'API (<see cref="Api.Donnees.DossierDeDonnees"/>), que la
/// fenêtre lui redonne en la construisant.
/// </summary>
internal static class DossierDeDonnees
{
    /// <summary>Le nom du dossier, sous <c>%LOCALAPPDATA%</c>.</summary>
    public const string Nom = Api.Donnees.DossierDeDonnees.Nom;

    /// <summary>Le sous-dossier du profil du navigateur intégré (ADR-0003, « Dossier de profil »).</summary>
    public const string NomDuProfil = "WebView2";

    /// <summary>Le dossier de données de l'utilisateur courant.</summary>
    public static string DeLUtilisateur() => Api.Donnees.DossierDeDonnees.DeLUtilisateur();

    /// <summary>Le dossier de données sous un dossier <c>%LOCALAPPDATA%</c> donné.</summary>
    public static string Sous(string dossierLocalDesApplications) => Api.Donnees.DossierDeDonnees.Sous(dossierLocalDesApplications);

    /// <summary>Le profil du navigateur intégré, dans le dossier de données.</summary>
    public static string ProfilDuNavigateur(string dossierDeDonnees) => Path.Combine(dossierDeDonnees, NomDuProfil);
}
