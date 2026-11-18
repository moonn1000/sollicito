using System.Text.Json;

namespace Sollicito.Tests.Commun;

/// <summary>
/// Dépendances déclarées par les projets de la solution, références de projets et paquets
/// NuGet, lues dans le fichier <c>.deps.json</c> du projet de tests. Ce fichier les liste
/// toutes, y compris celles dont le code ne se sert pas, que le compilateur ne garde pas.
/// </summary>
internal static class GrapheDesDependances
{
    /// <summary>Toutes les dépendances du projet, directes et indirectes.</summary>
    public static IReadOnlySet<string> DependancesDe(string projet)
    {
        var graphe = LireGraphe();
        if (!graphe.TryGetValue(projet, out var directes))
        {
            throw new InvalidOperationException($"Le projet {projet} n'est pas dans le graphe des dépendances.");
        }

        var trouvees = new SortedSet<string>(StringComparer.Ordinal);
        var aVisiter = new Stack<string>(directes);
        while (aVisiter.TryPop(out var nom))
        {
            if (trouvees.Add(nom) && graphe.TryGetValue(nom, out var suivantes))
            {
                foreach (var suivante in suivantes)
                {
                    aVisiter.Push(suivante);
                }
            }
        }

        return trouvees;
    }

    private static Dictionary<string, string[]> LireGraphe()
    {
        var nomDuProjetDeTests = typeof(GrapheDesDependances).Assembly.GetName().Name;
        var fichier = Path.Combine(AppContext.BaseDirectory, $"{nomDuProjetDeTests}.deps.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fichier));

        var racine = document.RootElement;
        var cible = racine.GetProperty("runtimeTarget").GetProperty("name").GetString()
            ?? throw new InvalidOperationException($"Cible absente de {fichier}.");

        var graphe = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var bibliotheque in racine.GetProperty("targets").GetProperty(cible).EnumerateObject())
        {
            // Chaque entrée est nommée « nom/version ».
            var separateur = bibliotheque.Name.IndexOf('/', StringComparison.Ordinal);
            if (separateur < 0)
            {
                throw new InvalidOperationException($"Entrée « {bibliotheque.Name} » sans version dans {fichier}.");
            }

            var nom = bibliotheque.Name[..separateur];
            graphe[nom] = bibliotheque.Value.TryGetProperty("dependencies", out var dependances)
                ? [.. dependances.EnumerateObject().Select(dependance => dependance.Name)]
                : [];
        }

        return graphe;
    }
}
