#if VERSION_DE_TEST
using System.Globalization;

namespace Sollicito.Fenetre;

/// <summary>
/// La version de test (configuration Test), que pilote le test Playwright de la vraie fenêtre (ADR-0003, « Runtime
/// et tests ») et qui ne se publie jamais (Sollicito.Fenetre.csproj). Elle reprend les réglages de la version publiée
/// et ne s'en écarte que sur ces points, réglés par des variables d'environnement que pose le test :
/// <list type="bullet">
/// <item>le protocole CDP ouvert sur le port que le test choisit, en 127.0.0.1 ;</item>
/// <item>un dossier de données donné par le test, sans lequel elle refuse de se lancer : elle ne touche jamais à
/// celui de l'utilisateur ;</item>
/// <item>les liens externes notés dans un fichier de ce dossier au lieu d'être ouverts dans le navigateur, et les
/// navigations annulées notées dans un autre ;</item>
/// <item>le dossier de téléchargement placé dans ce dossier, où tomberait un téléchargement admis ;</item>
/// <item>une caméra et un micro simulés, pour que leur demande de permission ne dépende pas du poste ;</item>
/// <item>ses messages d'erreur écrits sur la sortie d'erreur, sans boîte de dialogue (<see cref="Signalement"/>) ;</item>
/// <item>le dialogue de dossier des sauvegardes remplacé par le dossier que le test écrit dans un fichier de ce
/// dossier, sans dialogue (#26) ; sans ce fichier, le choix est annulé.</item>
/// </list>
/// </summary>
internal static class VersionDeTest
{
    public const string VariableDuDossierDeDonnees = "SOLLICITO_TEST_DOSSIER_DONNEES";
    public const string VariableDuPortCdp = "SOLLICITO_TEST_PORT_CDP";
    public const string FichierDesLiensOuverts = "liens-ouverts.txt";
    public const string FichierDesNavigationsAnnulees = "navigations-annulees.txt";
    public const string DossierDesTelechargements = "telechargements";
    public const string FichierDuDossierAChoisir = "dossier-a-choisir.txt";

    public static Lancement Lancement()
    {
        var dossier = Environment.GetEnvironmentVariable(VariableDuDossierDeDonnees);
        if (string.IsNullOrWhiteSpace(dossier) || !Path.IsPathFullyQualified(dossier))
        {
            throw new InvalidOperationException(
                $"La version de test exige un dossier de données absolu dans {VariableDuDossierDeDonnees}.");
        }

        var arguments = "--use-fake-device-for-media-stream";
        if (int.TryParse(Environment.GetEnvironmentVariable(VariableDuPortCdp), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535)
        {
            arguments += string.Create(CultureInfo.InvariantCulture, $" --remote-debugging-port={port}");
        }

        var navigationsAnnulees = Path.Combine(dossier, FichierDesNavigationsAnnulees);
        return new Lancement(
            dossier,
            arguments,
            new JournalDesLiens(Path.Combine(dossier, FichierDesLiensOuverts)),
            Path.Combine(dossier, DossierDesTelechargements),
            adresse => File.AppendAllText(navigationsAnnulees, adresse + Environment.NewLine),
            new ChoixLuDansUnFichier(Path.Combine(dossier, FichierDuDossierAChoisir)));
    }

    /// <summary>Note chaque lien que la version publiée ouvrirait dans le navigateur par défaut.</summary>
    private sealed class JournalDesLiens(string fichier) : IOuvreurDeLiens
    {
        public void Ouvrir(Uri lien) => File.AppendAllText(fichier, lien.AbsoluteUri + Environment.NewLine);
    }

    /// <summary>Le dossier que le test a écrit dans le fichier, au lieu du dialogue ; sans fichier, le choix est annulé.</summary>
    private sealed class ChoixLuDansUnFichier(string fichier) : IChoixDUnDossier
    {
        public string? Choisir(IWin32Window proprietaire, string dossierActuel) =>
            File.Exists(fichier) ? File.ReadAllText(fichier).Trim() : null;
    }
}
#endif
