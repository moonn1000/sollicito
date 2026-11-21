using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La migration de #14, <c>RelancesEtContacts</c>, sur une base peuplée avant elle : la base est arrêtée à la migration
/// de #25, des candidatures y sont écrites, une étape corrigée comprise, puis l'API se lance sur elle, applique la
/// migration sur une copie (#26), relit chaque fiche et l'écrit, relances et contacts compris. Toutes les données sont
/// fictives.
/// </summary>
public sealed class MigrationDesRelancesTests
{
    private const string MigrationDe25 = "20261120164027_CorrectionsDesEtapes";

    [Fact]
    public async Task Une_base_peuplee_avant_14_passe_la_migration_sans_rien_perdre_puis_recoit_relances_et_contacts()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            var dossierDeLaBase = new DossierDeLaBase(dossier);
            await PeuplerA25Async(dossierDeLaBase);

            await using var api = await ApiLancee.LancerAsync(builder =>
            {
                new HorlogeDeTest().Utiliser(builder);
                builder.ConfigureTestServices(services => services.AddSingleton(dossierDeLaBase));
            });

            await using (var portee = api.Services.CreateAsyncScope())
            {
                var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
                Assert.Equal(baseDeSollicito.Database.GetMigrations(), await baseDeSollicito.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
            }

            Assert.Contains(Fichiers(dossierDeLaBase), nom => nom == NomsDesCopiesAvant());
            var fiche = await api.FicheAsync(1);
            Assert.Equal("2026-11-02T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
            Assert.Equal([3, 5, 1, 2], fiche["historique"]!.AsArray().Select(evenement => evenement!["id"]!.GetValue<int>()));
            Assert.Equal(3, fiche["historique"]![1]!["corrige"]!.GetValue<int>());
            Assert.True(fiche["historique"]![0]!["estCorrige"]!.GetValue<bool>());
            Assert.Empty(fiche["relances"]!.AsArray());
            Assert.Equal("Refusee", (await api.FicheAsync(2))["statut"]!.GetValue<string>());

            // La base migrée reçoit un contact et une relance, que la clôture fait entrer dans l'historique.
            var contact = await api.AjouterUnContactAvecSuccesAsync(1, "Jeanne Martin", "jeanne@exemple.invalid");
            var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(1, destinataire: Id(contact))));
            using var faite = await api.MarquerFaiteAsync(1, relance);
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
            Assert.Equal(5, (await api.FicheAsync(1))["historique"]!.AsArray().Count);
            using var refusee = await api.PlanifierAsync(2);
            Assert.Equal(HttpStatusCode.Conflict, refusee.StatusCode);
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    // La copie d'avant la mise à jour, nommée d'après la dernière migration qui attend : celle de #14.
    private static string NomsDesCopiesAvant() => Application.Sauvegardes.NomsDesCopies.AvantMiseAJour(BaseDe12.DerniereMigration);

    private static string[] Fichiers(DossierDeLaBase dossierDeLaBase) => AppelsDesSauvegardes.Fichiers(dossierDeLaBase.DossierDesSauvegardes);

    // Une base telle que #25 l'a laissée : ses migrations, puis deux candidatures, dont une étape corrigée.
    private static async Task PeuplerA25Async(DossierDeLaBase dossierDeLaBase)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(dossierDeLaBase.ChaineDeConnexion()).Options;
        await using var baseDe25 = new BaseDeSollicito(options);
        await baseDe25.GetService<IMigrator>().MigrateAsync(MigrationDe25, TestContext.Current.CancellationToken);
        Assert.Equal(MigrationDe25, (await baseDe25.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Last());
        await baseDe25.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Entreprises (Id, Nom, Cle) VALUES (1, 'Lumière Verte SA', 'lumiere verte'), (2, 'Brasserie Lune Rousse', 'brasserie lune rousse');
            INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES
                (1, 1, 'Comptable', '2026-11-02 09:00:00', 'Envoyee'),
                (2, 2, 'Brasseuse', '2026-11-03 09:00:00', 'Refusee');
            INSERT INTO EvenementsDeCandidature (Id, CandidatureId, SurvenuLe, Sorte, Ancien, Nouveau, CorrigeId, Type, Note, EtapeCorrigeeId) VALUES
                (1, 1, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Envoyee', 'Entretien', NULL, NULL, NULL, NULL),
                (2, 1, '2026-11-18 14:30:00', 'ChangementDeStatut', 'Entretien', 'Envoyee', 1, NULL, NULL, NULL),
                (3, 1, '2026-11-10 09:00:00', 'Etape', NULL, NULL, NULL, 'Appel', 'Rappel de la responsable.', NULL),
                (4, 2, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Envoyee', 'Refusee', NULL, NULL, NULL, NULL),
                (5, 1, '2026-11-11 10:00:00', 'Etape', NULL, NULL, NULL, 'Entretien', NULL, 3);
            """,
            TestContext.Current.CancellationToken);
    }
}
