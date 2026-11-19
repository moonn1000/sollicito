using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Le verrou des migrations d'Entity Framework Core sous SQLite : une ligne de la table <see cref="Table"/>, insérée
/// et validée avant les migrations, retirée après. Un processus arrêté entre les deux la laisse, et chaque lancement
/// suivant l'attend sans fin (documentation d'EF Core, limites du fournisseur SQLite, « Handling abandoned locks »).
/// </summary>
public static class VerrouDesMigrations
{
    /// <summary>La table du verrou, que crée EF Core.</summary>
    public const string Table = "__EFMigrationsLock";

    // Des requêtes constantes, sans aucune valeur venue d'ailleurs.
    private const string TableExiste = "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = '" + Table + "'";
    private const string ViderLaTable = "DELETE FROM \"" + Table + "\"";

    /// <summary>
    /// Retire une ligne abandonnée du verrou des migrations, et rend le nombre de lignes retirées. À n'appeler que sous
    /// le <see cref="VerrouDeLInstance"/> du dossier : aucun autre lancement ne peut alors migrer la base, et une ligne
    /// présente ne peut venir que d'un processus arrêté pendant ses migrations.
    /// </summary>
    public static async Task<int> RetirerUneLigneAbandonneeAsync(DatabaseFacade baseDeDonnees, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(baseDeDonnees);
        await baseDeDonnees.OpenConnectionAsync(annulation);
        try
        {
            var tables = await baseDeDonnees.SqlQueryRaw<int>(TableExiste).SingleAsync(annulation);
            return tables == 0 ? 0 : await baseDeDonnees.ExecuteSqlRawAsync(ViderLaTable, annulation);
        }
        finally
        {
            await baseDeDonnees.CloseConnectionAsync();
        }
    }
}
