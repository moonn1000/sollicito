using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Une base telle que #11 l'a laissée, avant la migration de #12 : sa seule migration, puis deux candidatures au statut
/// Envoyée, sans historique. Toutes les données sont fictives.
/// </summary>
public static class BaseDe11
{
    /// <summary>La seule migration de #11.</summary>
    public const string Migration = "20261119133602_CandidaturesEtEntreprises";

    /// <summary>Crée et peuple la base dans ce dossier, arrêtée à la migration de #11.</summary>
    public static async Task PeuplerAsync(DossierDeLaBase dossierDeLaBase)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(dossierDeLaBase.ChaineDeConnexion()).Options;
        await using var baseDe11 = new BaseDeSollicito(options);
        await baseDe11.GetService<IMigrator>().MigrateAsync(Migration, TestContext.Current.CancellationToken);
        Assert.Equal(Migration, (await baseDe11.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Last());
        await baseDe11.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Entreprises (Id, Nom, Cle) VALUES (1, 'Lumière Verte SA', 'lumiere verte'), (2, 'Brasserie Lune Rousse', 'brasserie lune rousse');
            INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES
                (1, 1, 'Comptable', '2026-11-02 09:00:00', 'Envoyee'),
                (2, 2, 'Brasseuse', '2026-11-03 09:00:00', 'Envoyee');
            """,
            TestContext.Current.CancellationToken);
    }
}
