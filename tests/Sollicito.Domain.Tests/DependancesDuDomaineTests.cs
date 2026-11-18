using System.Reflection;
using Sollicito.Tests.Commun;

namespace Sollicito.Domain.Tests;

/// <summary>
/// Le domaine ne dépend ni de l'infrastructure, ni de la base, ni du réseau (ADR-0001, D4).
/// </summary>
public sealed class DependancesDuDomaineTests
{
    private const string Domaine = "Sollicito.Domain";

    // Assemblées que le code du domaine ne doit jamais appeler.
    private static readonly string[] PrefixesInterdits =
    [
        "System.Net",   // réseau : HTTP, sockets, résolution de noms, messagerie
        "System.Data",  // bases de données (ADO.NET)
        "Microsoft.",   // tout ce qui dépasse la bibliothèque de base : EF Core, SQLite, ASP.NET Core…
        "Sollicito.",   // les autres couches de la solution
    ];

    [Fact]
    public void Le_domaine_ne_declare_aucune_dependance()
    {
        Assert.Empty(GrapheDesDependances.DependancesDe(Domaine));
    }

    [Fact]
    public void Le_code_du_domaine_n_utilise_ni_le_reseau_ni_une_base_de_donnees()
    {
        var references = Assembly.Load(Domaine).GetReferencedAssemblies().Select(reference => reference.Name ?? "");

        var interdites = references.Where(nom => PrefixesInterdits.Any(prefixe => nom.StartsWith(prefixe, StringComparison.Ordinal)));

        Assert.Empty(interdites);
    }
}
