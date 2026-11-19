using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// La base que construisent les outils d'Entity Framework Core (<c>dotnet ef migrations add</c>) pour écrire une
/// migration. Ils ne lancent pas l'API, et la base ne s'ouvre pas : aucun fichier ne se crée, ni dans le dossier de
/// données de l'utilisateur, ni dans le dépôt.
/// </summary>
internal sealed class FabriqueDeConception : IDesignTimeDbContextFactory<BaseDeSollicito>
{
    public BaseDeSollicito CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite("Data Source=:memory:").Options);
}
