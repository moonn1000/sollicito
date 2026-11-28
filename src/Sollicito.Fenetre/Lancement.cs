namespace Sollicito.Fenetre;

/// <summary>Ce qui règle un lancement de la fenêtre : la version publiée, ou la version de test.</summary>
/// <param name="DossierDeDonnees">Le dossier de données : base, sauvegardes, profil du navigateur intégré.</param>
/// <param name="ArgumentsDuNavigateur">Arguments ajoutés au navigateur intégré ; aucun dans la version publiée.</param>
/// <param name="OuvreurDeLiens">Ce qui ouvre un lien externe : le navigateur par défaut dans la version publiée.</param>
/// <param name="DossierDesTelechargements">Le dossier où tomberait un téléchargement ; celui du profil dans la version publiée.</param>
/// <param name="NoterUneNavigationAnnulee">Ce qui note chaque navigation annulée ; rien dans la version publiée.</param>
/// <param name="ChoixDUnDossier">Ce qui fait choisir le dossier des sauvegardes : le dialogue de Windows dans la version publiée.</param>
/// <param name="OuvreurDeBrouillons">Ce qui ouvre le brouillon d'une relance : la messagerie par défaut dans la version publiée.</param>
/// <param name="ConfirmationDeLaRestauration">Ce qui demande le oui d'une restauration : le dialogue de Windows dans la version publiée (#28).</param>
/// <param name="EtapesDeLaRestauration">Ce qui voit passer les étapes d'une restauration : rien dans la version publiée.</param>
/// <param name="FaussesReponsesDeLIA">Ce qui répond à la place des fournisseurs d'IA en ligne : rien dans la version publiée, qui les appelle (#48).</param>
/// <param name="ChoixDUnFichierDExport">Ce qui fait choisir le fichier d'un export : le dialogue « Enregistrer sous » de Windows dans la version publiée (#49).</param>
/// <param name="EtapesDeLExport">Ce qui voit passer les étapes d'un export : rien dans la version publiée (#118).</param>
internal sealed record Lancement(
    string DossierDeDonnees,
    string? ArgumentsDuNavigateur,
    IOuvreurDeLiens OuvreurDeLiens,
    string? DossierDesTelechargements,
    Action<string>? NoterUneNavigationAnnulee,
    IChoixDUnDossier ChoixDUnDossier,
    IOuvreurDeBrouillons OuvreurDeBrouillons,
    IConfirmationDeLaRestauration ConfirmationDeLaRestauration,
    Api.Sauvegardes.IEtapesDeLaRestauration? EtapesDeLaRestauration,
    Func<HttpMessageHandler>? FaussesReponsesDeLIA,
    IChoixDUnFichierDExport ChoixDUnFichierDExport,
    Api.Export.IEtapesDeLExport? EtapesDeLExport)
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
            DossierDesTelechargements: null,
            NoterUneNavigationAnnulee: null,
            new ChoixParLeDialogue(),
            new OuvreurDeBrouillonsParLeShell(),
            new ConfirmationParLeDialogue(),
            EtapesDeLaRestauration: null,
            FaussesReponsesDeLIA: null,
            new ChoixDUnFichierParLeDialogue(),
            EtapesDeLExport: null);
#endif
    }
}
