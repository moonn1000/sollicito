using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests;

/// <summary>
/// La migration de #25, <c>CorrectionsDesEtapes</c>, sur une base peuplée avant elle (#82, point 1) : la base est
/// arrêtée à la migration de #12, des candidatures y sont écrites, puis l'API se lance sur elle, applique la migration,
/// relit chaque fiche et l'écrit. Depuis #26, la migration s'applique à une copie, qui remplace ensuite la base : les
/// dates relues prouvent que rien ne s'est déplacé en route (#85, point 3). Toutes les données sont fictives.
/// </summary>
public sealed class MigrationDesCorrectionsDEtapeTests
{
    private readonly HorlogeDeTest _horloge = new();

    [Fact]
    public async Task Une_base_peuplee_avant_25_passe_la_migration_sans_rien_perdre_puis_s_ecrit()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            var dossierDeLaBase = new DossierDeLaBase(dossier);
            await BaseDe12.PeuplerAsync(dossierDeLaBase);

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
            Assert.Equal("2026-11-02T09:00:00Z", fiche["envoyeeLe"]!.GetValue<string>());
            var historique = fiche["historique"]!.AsArray();
            Assert.Equal([3, 1, 2], historique.Select(evenement => evenement!["id"]!.GetValue<int>()));
            Assert.Equal(["etape", "changementDeStatut", "changementDeStatut"], historique.Select(evenement => evenement!["sorte"]!.GetValue<string>()));
            Assert.Equal(
                ["2026-11-10T09:00:00Z", "2026-11-18T13:30:00Z", "2026-11-18T14:30:00Z"],
                historique.Select(evenement => evenement!["survenuLe"]!.GetValue<string>()));
            Assert.Equal([false, true, false], historique.Select(evenement => evenement!["estCorrige"]!.GetValue<bool>()));
            Assert.Equal(1, historique[2]!["corrige"]!.GetValue<int>());
            Assert.Null(historique[0]!["corrige"]);
            Assert.Equal("Rappel de la responsable.", historique[0]!["note"]!.GetValue<string>());
            var seconde = await api.FicheAsync(2);
            Assert.Equal("2026-11-03T09:00:00Z", seconde["envoyeeLe"]!.GetValue<string>());
            Assert.Equal("2026-11-18T13:30:00Z", Assert.Single(seconde["historique"]!.AsArray())!["survenuLe"]!.GetValue<string>());

            // La base migrée s'écrit : une étape se corrige, une candidature se modifie, puis se supprime avec son historique.
            Assert.Equal(4, (await api.CorrigerUneEtapeAvecSuccesAsync(1, 3, "Appel", "2026-11-11T10:00", null))["historique"]!.AsArray().Count);
            Assert.Equal("Comptable senior", (await api.ModifierAvecSuccesAsync(1, "Lumière Verte SA", "Comptable senior", "2026-11-02T10:00"))["poste"]!.GetValue<string>());
            using var suppression = await api.SupprimerAsync(1);
            Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);
            Assert.Single((await api.FicheAsync(2))["historique"]!.AsArray());
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }
}
