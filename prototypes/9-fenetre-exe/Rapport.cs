using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Prototype9;

record Controle(string Nom, bool Ok, object? Detail);

/// <summary>
/// Rapport de l'autotest, écrit en JSON : contrôles, sections, étapes. Les chemins y sont masqués
/// (%TEMP%, %USERPROFILE%, %USERNAME%) pour qu'il puisse être publié tel quel.
/// </summary>
sealed class Rapport(string chemin)
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly Lock verrou = new();
    readonly Stopwatch chrono = Stopwatch.StartNew();
    readonly List<Controle> controles = [];
    readonly Dictionary<string, object?> sections = [];
    readonly List<object> etapes = [];
    readonly List<string> erreurs = [];
    bool ecrit;

    public void Etape(string texte)
    {
        lock (verrou)
        {
            etapes.Add(new { ms = chrono.ElapsedMilliseconds, texte = Masquer(texte) });
        }
    }

    public void Section(string nom, object? contenu)
    {
        lock (verrou)
        {
            sections[nom] = contenu;
        }
    }

    public void Controler(string nom, bool ok, object? detail = null)
    {
        lock (verrou)
        {
            controles.Add(new Controle(nom, ok, detail));
        }
    }

    public void Erreur(string ou, Exception ex)
    {
        lock (verrou)
        {
            erreurs.Add($"{ou} : {ex.GetType().FullName} : {Masquer(ex.Message)}");
        }
    }

    /// <summary>Écrit le rapport ; rend 0 si tous les contrôles attendus sont présents et au vert, 1 sinon.</summary>
    public int Ecrire(IReadOnlyCollection<string> attendus)
    {
        lock (verrou)
        {
            var manquants = attendus.Where(a => controles.All(c => c.Nom != a)).ToList();
            var reussi = erreurs.Count == 0 && manquants.Count == 0 && controles.All(c => c.Ok);
            EcrireSansVerrou(reussi, manquants);
            return reussi ? 0 : 1;
        }
    }

    /// <summary>Écrit ce qui est connu, puis arrête le processus (code 2). Appelable depuis n'importe quel fil.</summary>
    public void Abandonner(string raison)
    {
        lock (verrou)
        {
            erreurs.Add(Masquer(raison));
            EcrireSansVerrou(false, []);
        }
        Environment.Exit(2);
    }

    void EcrireSansVerrou(bool reussi, List<string> manquants)
    {
        if (ecrit)
        {
            return;
        }
        ecrit = true;
        var contenu = new
        {
            verdict = reussi ? "réussi" : "échoué",
            controles,
            manquants,
            erreurs,
            versions = Versions(),
            sections,
            etapes,
        };
        File.WriteAllText(chemin, JsonSerializer.Serialize(contenu, Options));
    }

    static object Versions() => new
    {
        dotnet = RuntimeInformation.FrameworkDescription,
        webView2Sdk = Informationnelle(typeof(Microsoft.Web.WebView2.Core.CoreWebView2Environment).Assembly),
        microsoftDataSqlite = Informationnelle(typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly),
        sqlitePclRaw = typeof(SQLitePCL.raw).Assembly.GetName().Version?.ToString(),
    };

    static string? Informationnelle(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString();

    public static string Masquer(string? texte)
    {
        if (string.IsNullOrEmpty(texte))
        {
            return texte ?? "";
        }
        var resultat = texte.Replace(Path.GetTempPath().TrimEnd('\\'), "%TEMP%", StringComparison.OrdinalIgnoreCase);
        resultat = resultat.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var utilisateur = Environment.UserName;
        if (utilisateur.Length > 0)
        {
            // Filet : le nom du compte comme segment de chemin, sous une autre forme que les deux précédentes.
            resultat = Regex.Replace(resultat, @"(?<=\\)" + Regex.Escape(utilisateur) + @"(?=\\|$)", "%USERNAME%", RegexOptions.IgnoreCase);
        }
        return resultat;
    }
}

/// <summary>Ce que l'exe charge réellement : preuve qu'il tourne seul, sans les runtimes .NET installés sur le poste.</summary>
static class Execution
{
    public static (object Description, bool Autonome) Decrire()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
        var installationDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet") + "\\";
        List<string> modules;
        using (var processus = Process.GetCurrentProcess())
        {
            modules = processus.Modules.Cast<ProcessModule>().Select(m => m.FileName).ToList();
        }
        var depuisInstallationDotnet = modules.Where(m => m.StartsWith(installationDotnet, StringComparison.OrdinalIgnoreCase)).ToList();
#pragma warning disable IL3000 // Voulu : en fichier unique, Location est vide pour les assemblies du bundle, et c'est ce qu'on vérifie.
        var emplacementCoreLib = typeof(object).Assembly.Location;
        var luesSurLeDisque = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => Rapport.Masquer(a.Location))
            .ToList();
#pragma warning restore IL3000

        var description = new
        {
            framework = RuntimeInformation.FrameworkDescription,
            rid = RuntimeInformation.RuntimeIdentifier,
            exe = Rapport.Masquer(Environment.ProcessPath),
            emplacementCoreLib = Rapport.Masquer(emplacementCoreLib),
            assembliesLuesSurLeDisque = luesSurLeDisque,
            modulesDepuisInstallationDotnet = depuisInstallationDotnet.Select(Rapport.Masquer).ToList(),
            modulesHorsWindows = modules.Where(m => !m.StartsWith(windows, StringComparison.OrdinalIgnoreCase)).Select(Rapport.Masquer).ToList(),
            extraction = emplacementCoreLib.Length == 0 ? DecrireExtraction(modules) : null,
        };
        var autonome = emplacementCoreLib.Length == 0 && luesSurLeDisque.Count == 0 && depuisInstallationDotnet.Count == 0;
        return (description, autonome);
    }

    // En fichier unique, .NET extrait les bibliothèques natives dans un même dossier : celui d'où e_sqlite3.dll est chargée.
    static object? DecrireExtraction(List<string> modules)
    {
        var sqlite = modules.FirstOrDefault(m => Path.GetFileName(m).Equals("e_sqlite3.dll", StringComparison.OrdinalIgnoreCase));
        var dossier = sqlite is null ? null : Path.GetDirectoryName(sqlite);
        if (dossier is null || !Directory.Exists(dossier))
        {
            return null;
        }
        var fichiers = Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
            .Select(f => new { nom = Path.GetRelativePath(dossier, f), octets = new FileInfo(f).Length })
            .OrderBy(f => f.nom, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new { dossier = Rapport.Masquer(dossier), fichiers, octets = fichiers.Sum(f => f.octets) };
    }
}
