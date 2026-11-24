using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests;

/// <summary>
/// La migration de #12, <c>HistoriqueDesCandidatures</c>, sur une base peuplée avant elle (#82, point 1) : la base est
/// arrêtée à la migration de #11, des candidatures y sont écrites, puis l'API se lance sur elle, applique les migrations,
/// relit chaque fiche et l'écrit. Chaque migration a ainsi son essai sur une base peuplée par la précédente : celle de
/// #25 a le sien (<see cref="MigrationDesCorrectionsDEtapeTests"/>), et les suivantes les leurs. Toutes les données sont
/// fictives.
/// </summary>
public sealed class MigrationDeLHistoriqueTests
{
    private readonly HorlogeDeTest _horloge = new();

    [Fact]
    public async Task Une_base_peuplee_avant_12_passe_les_migrations_sans_rien_perdre_puis_s_ecrit()
    {
        var dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;
        try
        {
            var dossierDeLaBase = new DossierDeLaBase(dossier);
            await BaseDe11.PeuplerAsync(dossierDeLaBase);

            await using var api = await ApiLancee.LancerAsync(builder =>
            {
                _horloge.Utiliser(builder);
                builder.ConfigureTestServices(services => services.AddSingleton(dossierDeLaBase));
            });

            // Toutes les migrations sont passées, celle de #12 comprise.
            await using (var portee = api.Services.CreateAsyncScope())
            {
                var baseDeSollicito = portee.ServiceProvider.GetRequiredService<BaseDeSollicito>();
                Assert.Equal(baseDeSollicito.Database.GetMigrations(), await baseDeSollicito.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
                Assert.Contains(BaseDe12.Migration, await baseDeSollicito.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
            }

            // Chaque fiche se lit : au statut Envoyée, ses dates intactes, un historique vide (aucun événement n'est inventé).
            var premiere = await api.FicheAsync(1);
            Assert.Equal(("Lumière Verte SA", "Comptable", "Envoyee"), (premiere["entreprise"]!["nom"]!.GetValue<string>(), premiere["poste"]!.GetValue<string>(), premiere["statut"]!.GetValue<string>()));
            Assert.Equal("2026-11-02T09:00:00Z", premiere["envoyeeLe"]!.GetValue<string>());
            Assert.Empty(premiere["historique"]!.AsArray());
            Assert.Null(premiere["correction"]);
            var seconde = await api.FicheAsync(2);
            Assert.Equal(("Brasserie Lune Rousse", "Brasseuse", "Envoyee"), (seconde["entreprise"]!["nom"]!.GetValue<string>(), seconde["poste"]!.GetValue<string>(), seconde["statut"]!.GetValue<string>()));
            Assert.Equal("2026-11-03T09:00:00Z", seconde["envoyeeLe"]!.GetValue<string>());
            Assert.Empty(seconde["historique"]!.AsArray());

            // Elle s'écrit : un passage, une étape, puis la correction du passage ; l'autre fiche n'a pas bougé.
            Assert.Equal("Entretien", (await api.ChangerLeStatutAvecSuccesAsync(1, "Entretien"))["statut"]!.GetValue<string>());
            Assert.Equal(2, (await api.NoterUneEtapeAvecSuccesAsync(1, "Appel", "2026-11-10T09:00", "Rappel de la responsable."))["historique"]!.AsArray().Count);
            var corrigee = await api.CorrigerAvecSuccesAsync(1);
            Assert.Equal("Envoyee", corrigee["statut"]!.GetValue<string>());
            Assert.Equal(3, corrigee["historique"]!.AsArray().Count);
            Assert.Empty((await api.FicheAsync(2))["historique"]!.AsArray());
        }
        finally
        {
            await AppelsDesSauvegardes.SupprimerAsync(dossier);
        }
    }
}
