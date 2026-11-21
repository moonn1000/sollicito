using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Une base telle que #12 l'a laissée, avant la migration de #25 : ses migrations, puis deux candidatures. La première
/// a un passage corrigé et une étape ; la seconde, un passage. Toutes les données sont fictives.
/// </summary>
public static class BaseDe12
{
    /// <summary>La dernière migration de #12.</summary>
    public const string Migration = "20261120135647_HistoriqueDesCandidatures";

    /// <summary>La migration qui la suit, celle de #25.</summary>
    public const string MigrationSuivante = "20261120164027_CorrectionsDesEtapes";

    /// <summary>Crée et peuple la base dans ce dossier, arrêtée à la migration de #12.</summary>
    public static async Task PeuplerAsync(DossierDeLaBase dossierDeLaBase)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(dossierDeLaBase.ChaineDeConnexion()).Options;
        await using var baseDe12 = new BaseDeSollicito(options);
        await baseDe12.GetService<IMigrator>().MigrateAsync(Migration, TestContext.Current.CancellationToken);
        Assert.Equal(Migration, (await baseDe12.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Last());
        await baseDe12.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Entreprises (Id, Nom, Cle) VALUES (1, 'Lumière Verte SA', 'lumiere verte'), (2, 'Brasserie Lune Rousse', 'brasserie lune rousse');
            INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES
                (1, 1, 'Comptable', '2026-11-02 09:00:00', 'Envoyee'),
                (2, 2, 'Brasseuse', '2026-11-03 09:00:00', 'Refusee');
            INSERT INTO EvenementsDeCandidature (Id, CandidatureId, SurvenuLe, Sorte, Ancien, Nouveau, CorrigeId, Type, Note) VALUES
                (1, 1, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Envoyee', 'Entretien', NULL, NULL, NULL),
                (2, 1, '2026-11-18 14:30:00', 'ChangementDeStatut', 'Entretien', 'Envoyee', 1, NULL, NULL),
                (3, 1, '2026-11-10 09:00:00', 'Etape', NULL, NULL, NULL, 'Appel', 'Rappel de la responsable.'),
                (4, 2, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Envoyee', 'Refusee', NULL, NULL, NULL);
            """,
            TestContext.Current.CancellationToken);
    }
}
