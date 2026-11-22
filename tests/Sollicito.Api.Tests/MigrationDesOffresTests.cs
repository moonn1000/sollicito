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
/// La migration de #36, <c>Offres</c>, sur une base peuplée avant elle : la base est arrêtée à la migration de #14, des
/// candidatures, une étape corrigée, un contact, une relance faite et une relance à faire y sont écrits, puis l'API se
/// lance sur elle, applique la migration sur une copie (#26), relit tout, et reçoit une offre. Toutes les données sont
/// fictives.
/// </summary>
public sealed class MigrationDesOffresTests
{
    private const string MigrationDe14 = "20261121140747_RelancesEtContacts";

    [Fact]
    public async Task Une_base_peuplee_avant_36_passe_la_migration_sans_rien_perdre_puis_recoit_une_offre()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            var dossierDeLaBase = new DossierDeLaBase(dossier);
            await PeuplerA14Async(dossierDeLaBase);

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

            Assert.Contains(AppelsDesSauvegardes.Fichiers(dossierDeLaBase.DossierDesSauvegardes), nom => nom == Application.Sauvegardes.NomsDesCopies.AvantMiseAJour(BaseDe12.DerniereMigration));
            var fiche = await api.FicheAsync(1);
            Assert.Equal("2026-11-02T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
            Assert.Equal([3, 5, 1, 2, 6], fiche["historique"]!.AsArray().Select(evenement => evenement!["id"]!.GetValue<int>()));
            Assert.Equal(2, fiche["relances"]!.AsArray().Count);
            Assert.Equal("Jeanne Martin", Assert.Single(await api.ContactsAsync(1))!["nom"]!.GetValue<string>());
            Assert.Equal("Refusee", (await api.FicheAsync(2))["statut"]!.GetValue<string>());

            // La base migrée reçoit une offre, qui reprend l'entreprise enregistrée par sa clé.
            var offre = await api.CollerAvecSuccesAsync("Offre fictive après la migration.", "Chauffeur", entreprise: "LUMIERE VERTE S.A.");
            Assert.Equal(1, Id(offre["entreprise"]));
            Assert.Equal("Lumière Verte SA", offre["entreprise"]!["nom"]!.GetValue<string>());
            Assert.Equal(2, (await api.ListerLesEntreprisesAsync()).Count);
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    // Une base telle que #14 l'a laissée : ses migrations, puis deux candidatures, une étape corrigée, un contact, une
    // relance faite, avec sa clôture, et une relance à faire.
    private static async Task PeuplerA14Async(DossierDeLaBase dossierDeLaBase)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(dossierDeLaBase.ChaineDeConnexion()).Options;
        await using var baseDe14 = new BaseDeSollicito(options);
        await baseDe14.GetService<IMigrator>().MigrateAsync(MigrationDe14, TestContext.Current.CancellationToken);
        Assert.Equal(MigrationDe14, (await baseDe14.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Last());
        await baseDe14.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Entreprises (Id, Nom, Cle) VALUES (1, 'Lumière Verte SA', 'lumiere verte'), (2, 'Brasserie Lune Rousse', 'brasserie lune rousse');
            INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES
                (1, 1, 'Comptable', '2026-11-02 09:00:00', 'Envoyee'),
                (2, 2, 'Brasseuse', '2026-11-03 09:00:00', 'Refusee');
            INSERT INTO Contacts (Id, EntrepriseId, Nom, Email) VALUES (1, 1, 'Jeanne Martin', 'jeanne@exemple.invalid');
            INSERT INTO Relances (Id, CandidatureId, Echeance, Etat, DestinataireId) VALUES
                (1, 1, '2026-11-09', 'Faite', 1),
                (2, 1, '2026-11-16', 'AFaire', 1);
            INSERT INTO EvenementsDeCandidature (Id, CandidatureId, SurvenuLe, Sorte, Ancien, Nouveau, CorrigeId, Type, Note, EtapeCorrigeeId, RelanceId) VALUES
                (1, 1, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Envoyee', 'Entretien', NULL, NULL, NULL, NULL, NULL),
                (2, 1, '2026-11-18 14:30:00', 'ChangementDeStatut', 'Entretien', 'Envoyee', 1, NULL, NULL, NULL, NULL),
                (3, 1, '2026-11-10 09:00:00', 'Etape', NULL, NULL, NULL, 'Appel', 'Rappel de la responsable.', NULL, NULL),
                (4, 2, '2026-11-18 13:30:00', 'ChangementDeStatut', 'Envoyee', 'Refusee', NULL, NULL, NULL, NULL, NULL),
                (5, 1, '2026-11-11 10:00:00', 'Etape', NULL, NULL, NULL, 'Entretien', NULL, 3, NULL),
                (6, 1, '2026-11-18 15:00:00', 'ClotureDeRelance', NULL, NULL, NULL, NULL, NULL, NULL, 1);
            """,
            TestContext.Current.CancellationToken);
    }
}
