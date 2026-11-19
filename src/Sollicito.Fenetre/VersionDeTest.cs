#if VERSION_DE_TEST
using System.Globalization;

namespace Sollicito.Fenetre;

/// <summary>
/// La version de test (configuration Test), que pilote le test Playwright de la vraie fenêtre (ADR-0003, « Runtime
/// et tests ») et qui ne se publie jamais (Sollicito.Fenetre.csproj). Elle reprend les réglages de la version publiée
/// et s'en écarte en quatre points, tous lus dans des variables d'environnement posées par le test :
/// <list type="bullet">
/// <item>le protocole CDP ouvert sur le port que le test choisit, en 127.0.0.1 ;</item>
/// <item>un dossier de données donné par le test, sans lequel elle refuse de se lancer : elle ne touche jamais à
/// celui de l'utilisateur ;</item>
/// <item>les liens externes notés dans un fichier de ce dossier au lieu d'être ouverts dans le navigateur ;</item>
/// <item>une caméra et un micro simulés, pour que leur demande de permission ne dépende pas du poste.</item>
/// </list>
/// </summary>
internal static class VersionDeTest
{
    public const string VariableDuDossierDeDonnees = "SOLLICITO_TEST_DOSSIER_DONNEES";
    public const string VariableDuPortCdp = "SOLLICITO_TEST_PORT_CDP";
    public const string FichierDesLiensOuverts = "liens-ouverts.txt";
    public const string DossierDesTelechargements = "telechargements";

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

        return new Lancement(
            dossier,
            arguments,
            new JournalDesLiens(Path.Combine(dossier, FichierDesLiensOuverts)),
            Path.Combine(dossier, DossierDesTelechargements));
    }

    /// <summary>Note chaque lien que la version publiée ouvrirait dans le navigateur par défaut.</summary>
    private sealed class JournalDesLiens(string fichier) : IOuvreurDeLiens
    {
        public void Ouvrir(Uri lien) => File.AppendAllText(fichier, lien.AbsoluteUri + Environment.NewLine);
    }
}
#endif
