using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests;

/// <summary>
/// La migration de #25, <c>CorrectionsDesEtapes</c>, sur une base peuplée avant elle (#82, point 1) : la base est
/// arrêtée à la migration de #12, des candidatures y sont écrites, puis l'API se lance sur elle, applique la migration,
/// relit chaque fiche et l'écrit. Toutes les données sont fictives.
/// </summary>
public sealed class MigrationDesCorrectionsDEtapeTests
{
    private const string MigrationDe12 = "20261120135647_HistoriqueDesCandidatures";

    private readonly HorlogeDeTest _horloge = new();

    [Fact]
    public async Task Une_base_peuplee_avant_25_passe_la_migration_sans_rien_perdre_puis_s_ecrit()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            var dossierDeLaBase = new DossierDeLaBase(dossier);
            await PeuplerUneBaseDe12Async(dossierDeLaBase);

            await using var api = await ApiLancee.LancerAsync(builder =>
            {
                _horloge.Utiliser(builder);
                builder.ConfigureTestServices(services => services.AddSingleton(dossierDeLaBase));
            });

            // Toutes les migrations sont passées, et l'historique de #12 est intact : correction et étape comprises.
            await using (var portee = api.Services.CreateAsyncScope())
            {
                var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
                Assert.Equal(baseDeSollicito.Database.GetMigrations(), await baseDeSollicito.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
            }

            var fiche = await api.FicheAsync(1);
            Assert.Equal(("Lumière Verte SA", "Comptable", "Envoyee"), (fiche["entreprise"]!["nom"]!.GetValue<string>(), fiche["poste"]!.GetValue<string>(), fiche["statut"]!.GetValue<string>()));
            var historique = fiche["historique"]!.AsArray();
            Assert.Equal([3, 1, 2], historique.Select(evenement => evenement!["id"]!.GetValue<int>()));
            Assert.Equal(["etape", "changementDeStatut", "changementDeStatut"], historique.Select(evenement => evenement!["sorte"]!.GetValue<string>()));
            Assert.Equal([false, true, false], historique.Select(evenement => evenement!["estCorrige"]!.GetValue<bool>()));
            Assert.Equal(1, historique[2]!["corrige"]!.GetValue<int>());
            Assert.Null(historique[0]!["corrige"]);
            Assert.Equal("Rappel de la responsable.", historique[0]!["note"]!.GetValue<string>());
            Assert.Single((await api.FicheAsync(2))["historique"]!.AsArray());

            // La base migrée s'écrit : une étape se corrige, une candidature se modifie, puis se supprime avec son historique.
            Assert.Equal(4, (await api.CorrigerUneEtapeAvecSuccesAsync(1, 3, "Appel", "2026-11-11T10:00", null))["historique"]!.AsArray().Count);
            Assert.Equal("Comptable senior", (await api.ModifierAvecSuccesAsync(1, "Lumière Verte SA", "Comptable senior", "2026-11-02T10:00"))["poste"]!.GetValue<string>());
            using var suppression = await api.SupprimerAsync(1);
            Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
            Assert.Single((await api.FicheAsync(2))["historique"]!.AsArray());
        }
        finally
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    // La base telle que #12 l'a laissée : ses migrations, puis deux candidatures. La première a un passage corrigé et
    // une étape ; la seconde, un passage.
    private static async Task PeuplerUneBaseDe12Async(DossierDeLaBase dossierDeLaBase)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(dossierDeLaBase.ChaineDeConnexion()).Options;
        await using var baseDe12 = new BaseDeSollicito(options);
        await baseDe12.GetService<IMigrator>().MigrateAsync(MigrationDe12, TestContext.Current.CancellationToken);
        Assert.Equal(MigrationDe12, (await baseDe12.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Last());
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
