using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDesOffres;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// La migration de #38, <c>CandidatureDUneOffre</c>, sur une base peuplée avant elle : la base est arrêtée à la migration
/// de #36, des candidatures, une étape corrigée, un contact, des relances et deux offres, l'une anonyme, y sont écrits,
/// puis l'API se lance sur elle, applique la migration sur une copie (#26), relit tout, et postule à une offre venue
/// d'avant. Aucune candidature d'avant ne désigne d'offre. Toutes les données sont fictives.
/// </summary>
public sealed class MigrationDuLienDesOffresTests
{
    private const string MigrationDe36 = "20261122092245_Offres";

    [Fact]
    public async Task Une_base_peuplee_avant_38_passe_la_migration_sans_rien_perdre_puis_postule_a_une_offre_d_avant()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            var dossierDeLaBase = new DossierDeLaBase(dossier);
            await PeuplerA36Async(dossierDeLaBase);

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

            // La copie d'avant la mise à jour, nommée d'après la dernière migration du code, celle qui s'applique.
            Assert.Contains(AppelsDesSauvegardes.Fichiers(dossierDeLaBase.DossierDesSauvegardes), nom => nom == Application.Sauvegardes.NomsDesCopies.AvantMiseAJour(BaseDe12.DerniereMigration));

            // Les candidatures, leur historique, leurs relances et les contacts, tels quels ; aucune ne désigne d'offre.
            var fiche = await api.FicheAsync(1);
            Assert.Equal("2026-11-02T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
            Assert.Equal([3, 5, 1, 2, 6], fiche["historique"]!.AsArray().Select(evenement => evenement!["id"]!.GetValue<int>()));
            Assert.Equal(2, fiche["relances"]!.AsArray().Count);
            Assert.Null(fiche["offre"]);
            Assert.Equal("Jeanne Martin", Assert.Single(await api.ContactsAsync(1))!["nom"]!.GetValue<string>());
            var refusee = await api.FicheAsync(2);
            Assert.Equal("Refusee", refusee["statut"]!.GetValue<string>());
            Assert.Null(refusee["offre"]);

            // Les offres, telles quelles, sans candidature ; l'entreprise que seule une offre désigne reste proposée.
            var offre = await api.LireLOffreAsync(1);
            Assert.Equal("Offre fictive d'avant #38.\nDeuxième ligne.", offre["texte"]!.GetValue<string>());
            Assert.Equal("Comptable", offre["poste"]!.GetValue<string>());
            Assert.Equal("Lumière Verte SA", offre["entreprise"]!["nom"]!.GetValue<string>());
            Assert.Null(offre["candidature"]);
            Assert.Null((await api.LireLOffreAsync(2))["entreprise"]);
            var offres = await api.ListerLesOffresAsync();
            Assert.Equal([2, 1, 3], Ids(offres));
            Assert.All(offres, listee => Assert.Null(listee!["candidature"]));
            Assert.Equal(
                ["Atelier d'Ondine", "Brasserie Lune Rousse", "Lumière Verte SA"],
                (await api.ListerLesEntreprisesAsync()).Select(entreprise => entreprise!["nom"]!.GetValue<string>()).Order(StringComparer.Ordinal));

            // La base migrée postule à une offre d'avant : la candidature reprend son entreprise, et la désigne.
            var candidature = await api.PostulerAvecSuccesAsync(1);
            Assert.Equal(1, Id(candidature["entreprise"]));
            Assert.Equal(3, Id(candidature));
            Assert.Equal(1, (await api.FicheAsync(3))["offre"]!.GetValue<int>());
            Assert.Equal(3, Id((await api.LireLOffreAsync(1))["candidature"]));
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }

    // Une base telle que #36 l'a laissée : ses migrations, puis deux candidatures, une étape corrigée, un contact, une
    // relance faite, avec sa clôture, une relance à faire, une offre d'une entreprise connue, une offre anonyme, et une
    // offre d'une troisième entreprise, que seule cette offre désigne.
    private static async Task PeuplerA36Async(DossierDeLaBase dossierDeLaBase)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(dossierDeLaBase.ChaineDeConnexion()).Options;
        await using var baseDe36 = new BaseDeSollicito(options);
        await baseDe36.GetService<IMigrator>().MigrateAsync(MigrationDe36, TestContext.Current.CancellationToken);
        Assert.Equal(MigrationDe36, (await baseDe36.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).Last());
        await baseDe36.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Entreprises (Id, Nom, Cle) VALUES
                (1, 'Lumière Verte SA', 'lumiere verte'),
                (2, 'Brasserie Lune Rousse', 'brasserie lune rousse'),
                (3, 'Atelier d''Ondine', 'atelier d ondine');
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
            INSERT INTO Offres (Id, Source, Empreinte, EntrepriseId, Poste, PosteNormalise, Lieu, PublieeLe, Adresse, Texte, AjouteeLe) VALUES
                (1, 'TexteColle', '1111111111111111111111111111111111111111111111111111111111111111', 1, 'Comptable', 'comptable', 'Liège', '2026-11-17', 'https://emploi.exemple.invalid/offres/1', 'Offre fictive d''avant #38.' || char(10) || 'Deuxième ligne.', '2026-11-18 10:00:00'),
                (2, 'TexteColle', '2222222222222222222222222222222222222222222222222222222222222222', NULL, 'Chauffeur', 'chauffeur', NULL, NULL, NULL, 'Offre fictive anonyme d''avant #38.', '2026-11-18 11:00:00'),
                (3, 'TexteColle', '3333333333333333333333333333333333333333333333333333333333333333', 3, 'Ébéniste', 'ebeniste', NULL, NULL, NULL, 'Offre fictive de l''atelier.', '2026-11-18 09:00:00');
            """,
            TestContext.Current.CancellationToken);
    }
}
