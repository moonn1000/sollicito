#if VERSION_DE_TEST
using System.Globalization;
using Sollicito.Application.Export;
using Sollicito.Application.Sauvegardes;

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
/// dossier, sans dialogue (#26) ; sans ce fichier, le choix est annulé ;</item>
/// <item>le lien <c>mailto</c> du brouillon d'une relance noté dans un fichier de ce dossier au lieu d'être ouvert dans
/// la messagerie (#14) ;</item>
/// <item>le dialogue de la restauration remplacé par un fichier de ce dossier (#28) : il vaut le oui s'il contient
/// « restaurer » ; sans lui, la restauration est annulée. La question du dialogue est notée dans un autre fichier ;</item>
/// <item>un point d'arrêt de la restauration (#28) : à l'étape que donne une variable d'environnement, la restauration
/// écrit un fichier de ce dossier, puis attend que le test tue le processus, pour essayer un arrêt brutal ;</item>
/// <item>les fournisseurs d'IA en ligne remplacés par un faux (#48), qui ne touche jamais le réseau : il répond selon le
/// scénario qu'un fichier de ce dossier donne (« réseau absent » sans lui), et note chaque demande dans un autre, avec
/// l'empreinte SHA-256 de l'en-tête qui porte la clé, jamais sa valeur. Le scénario « texte » rend, comme texte de l'IA,
/// le contenu d'un troisième fichier de ce dossier (#46) ;</item>
/// <item>le dialogue « Enregistrer sous » d'un export remplacé par le fichier que le test écrit dans un fichier de ce
/// dossier, et le oui à remplacer un fichier existant par une seconde ligne, « remplacer » (#49) ; sans ce fichier, ou
/// devant un fichier existant sans ce oui, l'export est annulé, comme dans le vrai dialogue. Chaque demande est notée dans
/// un autre fichier, avec le format et le nom proposé.</item>
/// </list>
/// </summary>
internal static class VersionDeTest
{
    public const string VariableDuDossierDeDonnees = "SOLLICITO_TEST_DOSSIER_DONNEES";
    public const string VariableDuPortCdp = "SOLLICITO_TEST_PORT_CDP";
    public const string VariableDeLArretDeLaRestauration = "SOLLICITO_TEST_ARRET_RESTAURATION";
    public const string FichierDesLiensOuverts = "liens-ouverts.txt";
    public const string FichierDesNavigationsAnnulees = "navigations-annulees.txt";
    public const string DossierDesTelechargements = "telechargements";
    public const string FichierDuDossierAChoisir = "dossier-a-choisir.txt";
    public const string FichierDesBrouillonsOuverts = "brouillons-ouverts.txt";
    public const string FichierDeLaConfirmation = "restauration-a-confirmer.txt";
    public const string FichierDesRestaurationsDemandees = "restaurations-demandees.txt";
    public const string FichierDeLArret = "restauration-arretee.txt";
    public const string FichierDuScenarioDeLIA = "ia-scenario.txt";
    public const string FichierDuTexteDeLIA = "ia-texte.txt";
    public const string FichierDesDemandesALIA = "ia-demandes.txt";
    public const string FichierDeLExportAChoisir = "export-a-choisir.txt";
    public const string FichierDesExportsDemandes = "exports-demandes.txt";

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
            new ChoixLuDansUnFichier(Path.Combine(dossier, FichierDuDossierAChoisir)),
            new JournalDesBrouillons(Path.Combine(dossier, FichierDesBrouillonsOuverts)),
            new ConfirmationLueDansUnFichier(Path.Combine(dossier, FichierDeLaConfirmation), Path.Combine(dossier, FichierDesRestaurationsDemandees)),
            EtapesArreteesParLeTest.Lire(Environment.GetEnvironmentVariable(VariableDeLArretDeLaRestauration), Path.Combine(dossier, FichierDeLArret)),
            () => new FauxFournisseurDeLIA(
                Path.Combine(dossier, FichierDuScenarioDeLIA),
                Path.Combine(dossier, FichierDuTexteDeLIA),
                Path.Combine(dossier, FichierDesDemandesALIA)),
            new ExportLuDansUnFichier(Path.Combine(dossier, FichierDeLExportAChoisir), Path.Combine(dossier, FichierDesExportsDemandes)));
    }

    /// <summary>
    /// Le fichier d'un export, lu dans celui que le test écrit : le chemin sur la première ligne, et « remplacer » sur la
    /// seconde pour accepter de remplacer un fichier existant. Sans ce fichier, ou devant un fichier existant sans ce oui,
    /// le choix est annulé : le vrai dialogue ne rend jamais un fichier existant sans le oui de l'utilisateur. Chaque
    /// demande note le format et le nom proposé, pour que le test les vérifie.
    /// </summary>
    private sealed class ExportLuDansUnFichier(string choix, string demandes) : IChoixDUnFichierDExport
    {
        public FichierChoisi? Choisir(IWin32Window proprietaire, FormatDExport format, string nomPropose)
        {
            File.AppendAllText(demandes, format + " " + nomPropose + Environment.NewLine);
            if (!File.Exists(choix))
            {
                return null;
            }

            var lignes = File.ReadAllLines(choix);
            var chemin = lignes.Length > 0 ? lignes[0].Trim() : "";
            var oui = lignes.Length > 1 && lignes[1].Trim() == "remplacer";
            if (chemin.Length == 0 || (File.Exists(chemin) && !oui))
            {
                return null;
            }

            return new FichierChoisi(chemin, Remplacer: File.Exists(chemin));
        }
    }

    /// <summary>
    /// Le oui de la restauration, lu dans le fichier que le test écrit ; sans lui, la restauration est annulée. La question
    /// du dialogue est notée, une ligne pour le titre et une pour le texte, pour que le test vérifie la date et la perte.
    /// </summary>
    private sealed class ConfirmationLueDansUnFichier(string reponse, string questions) : IConfirmationDeLaRestauration
    {
        public bool Confirmer(IWin32Window proprietaire, QuestionDeRestauration question)
        {
            File.AppendAllText(questions, question.Titre + Environment.NewLine + question.Texte.ReplaceLineEndings(" ") + Environment.NewLine);
            return File.Exists(reponse) && File.ReadAllText(reponse).Trim() == "restaurer";
        }
    }

    /// <summary>
    /// Le point d'arrêt de la restauration : à l'étape donnée, écrit son nom dans un fichier, puis attend sans fin, sur le
    /// fil de la restauration ; le test tue alors le processus par son PID. Sans étape valide, rien ne s'arrête.
    /// </summary>
    private sealed class EtapesArreteesParLeTest(EtapeDeLaRestauration arret, string fichier) : Api.Sauvegardes.IEtapesDeLaRestauration
    {
        public static Api.Sauvegardes.IEtapesDeLaRestauration? Lire(string? etape, string fichier) =>
            Enum.TryParse<EtapeDeLaRestauration>(etape, ignoreCase: false, out var arret) && Enum.IsDefined(arret)
                ? new EtapesArreteesParLeTest(arret, fichier)
                : null;

        public void Atteinte(EtapeDeLaRestauration etape)
        {
            if (etape != arret)
            {
                return;
            }

            File.WriteAllText(fichier, etape.ToString());
            Thread.Sleep(Timeout.Infinite);
        }
    }

    /// <summary>Note chaque lien que la version publiée ouvrirait dans le navigateur par défaut.</summary>
    private sealed class JournalDesLiens(string fichier) : IOuvreurDeLiens
    {
        public void Ouvrir(Uri lien) => File.AppendAllText(fichier, lien.AbsoluteUri + Environment.NewLine);
    }

    /// <summary>Note chaque brouillon que la version publiée ouvrirait dans la messagerie : son lien mailto, sur une ligne.</summary>
    private sealed class JournalDesBrouillons(string fichier) : IOuvreurDeBrouillons
    {
        public bool Ouvrir(string lien)
        {
            File.AppendAllText(fichier, lien + Environment.NewLine);
            return true;
        }
    }

    /// <summary>Le dossier que le test a écrit dans le fichier, au lieu du dialogue ; sans fichier, le choix est annulé.</summary>
    private sealed class ChoixLuDansUnFichier(string fichier) : IChoixDUnDossier
    {
        public string? Choisir(IWin32Window proprietaire, string dossierActuel) =>
            File.Exists(fichier) ? File.ReadAllText(fichier).Trim() : null;
    }
}
#endif
