namespace Sollicito.Fenetre;

/// <summary>Ce qui règle un lancement de la fenêtre : la version publiée, ou la version de test.</summary>
/// <param name="DossierDeDonnees">Le dossier de données : base, sauvegardes, profil du navigateur intégré.</param>
/// <param name="ArgumentsDuNavigateur">Arguments ajoutés au navigateur intégré ; aucun dans la version publiée.</param>
/// <param name="OuvreurDeLiens">Ce qui ouvre un lien externe : le navigateur par défaut dans la version publiée.</param>
/// <param name="DossierDesTelechargements">Le dossier où tomberait un téléchargement ; celui du profil dans la version publiée.</param>
internal sealed record Lancement(
    string DossierDeDonnees,
    string? ArgumentsDuNavigateur,
    IOuvreurDeLiens OuvreurDeLiens,
    string? DossierDesTelechargements)
{
    /// <summary>Le lancement de cet exe.</summary>
    public static Lancement Lire()
    {
#if VERSION_DE_TEST
        return VersionDeTest.Lancement();
#else
        return new Lancement(
            Fenetre.DossierDeDonnees.DeLUtilisateur(),
            ArgumentsDuNavigateur: null,
            new OuvreurParLeShell(),
            DossierDesTelechargements: null);
#endif
    }
}
