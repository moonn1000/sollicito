using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sollicito.Application.Export;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// La base lue pour l'export (#49, ADR-0020) : toutes les colonnes de toutes les tables de l'utilisateur, dans un seul
/// instantané, en mode WAL, et les instants relus tels qu'Entity Framework Core les écrit. Tout se passe dans un dossier
/// temporaire ; données fictives.
/// </summary>
public sealed class SourceDeLExportTests : IDisposable
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private DossierDeLaBase Dossier => new(_dossier);

    private static CancellationToken Annulation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public async Task L_export_lit_chaque_colonne_de_chaque_table_que_les_migrations_construisent()
    {
        await CreerLaBaseAsync();

        // Les tables de l'utilisateur : toutes, hors de celles que SQLite et EF Core tiennent pour eux.
        var tables = Lire("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\'");
        Assert.Equal(tables.Order(StringComparer.Ordinal), SourceDeLExport.ColonnesLues.Select(table => table.Table).Order(StringComparer.Ordinal));
        foreach (var (table, colonnes) in SourceDeLExport.ColonnesLues)
        {
            Assert.Equal(Lire($"SELECT name FROM pragma_table_info('{table}')").Order(StringComparer.Ordinal), colonnes.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task Une_ecriture_validee_pendant_la_lecture_n_entre_pas_dans_l_instantane()
    {
        await CreerLaBaseAsync();
        Executer("""
            INSERT INTO "Entreprises" ("Id", "Nom", "Cle") VALUES (1, 'Atelier fictif', 'atelier fictif');
            INSERT INTO "Candidatures" ("Id", "EntrepriseId", "Poste", "EnvoyeeLe", "Statut") VALUES (1, 1, 'Comptable', '2026-11-02 08:15:00', 'Envoyee');
            """);
        var source = new SourceDeLExport(Dossier);

        await using (var instantane = await source.OuvrirAsync(Annulation))
        {
            Assert.Single(await LireToutAsync(instantane.Entreprises(Annulation)));
            // Les entreprises lues, une autre connexion écrit et valide une candidature, et son entreprise.
            Executer("""
                INSERT INTO "Entreprises" ("Id", "Nom", "Cle") VALUES (2, 'Brasserie fictive', 'brasserie fictive');
                INSERT INTO "Candidatures" ("Id", "EntrepriseId", "Poste", "EnvoyeeLe", "Statut") VALUES (2, 2, 'Brasseuse', '2026-11-03 08:15:00', 'Envoyee');
                """);
            Assert.Equal([1], (await LireToutAsync(instantane.Candidatures(Annulation))).Select(candidature => candidature.Id));
        }

        // Un nouvel instantané la voit.
        await using var suivant = await source.OuvrirAsync(Annulation);
        Assert.Equal([1, 2], (await LireToutAsync(suivant.Candidatures(Annulation))).Select(candidature => candidature.Id));
    }

    [Fact]
    public async Task Les_instants_les_dates_et_les_valeurs_absentes_se_relisent_tels_qu_EF_Core_les_ecrit()
    {
        await CreerLaBaseAsync();
        Executer("""
            INSERT INTO "Entreprises" ("Id", "Nom", "Cle") VALUES (1, 'Atelier fictif', 'atelier fictif');
            INSERT INTO "Candidatures" ("Id", "EntrepriseId", "Poste", "EnvoyeeLe", "Statut") VALUES (1, 1, 'Comptable', '2026-11-02 08:15:00', 'Envoyee');
            INSERT INTO "Relances" ("Id", "CandidatureId", "DestinataireId", "Echeance", "Etat") VALUES (1, 1, NULL, '2026-11-09', 'AFaire');
            INSERT INTO "EvenementsDeCandidature" ("Id", "CandidatureId", "Sorte", "SurvenuLe", "Ancien", "Nouveau")
                VALUES (1, 1, 'ChangementDeStatut', '2026-11-18 13:31:00.1234567', 'Envoyee', 'Entretien');
            """);
        await using var instantane = await new SourceDeLExport(Dossier).OuvrirAsync(Annulation);

        var candidature = Assert.Single(await LireToutAsync(instantane.Candidatures(Annulation)));
        Assert.Equal(new DateTime(2026, 11, 2, 8, 15, 0, DateTimeKind.Utc), candidature.EnvoyeeLe);
        Assert.Equal(DateTimeKind.Utc, candidature.EnvoyeeLe.Kind);
        Assert.Null(candidature.OffreId);
        var relance = Assert.Single(await LireToutAsync(instantane.Relances(Annulation)));
        Assert.Equal(new DateOnly(2026, 11, 9), relance.Echeance);
        Assert.Null(relance.DestinataireId);
        var evenement = Assert.Single(await LireToutAsync(instantane.Evenements(Annulation)));
        Assert.Equal(new DateTime(2026, 11, 18, 13, 31, 0, DateTimeKind.Utc).AddTicks(1_234_567), evenement.SurvenuLe);
        Assert.Equal(DateTimeKind.Utc, evenement.SurvenuLe.Kind);
        Assert.Equal(("Envoyee", "Entretien", null, null, null, null, null), (evenement.Ancien, evenement.Nouveau, evenement.CorrigeId, evenement.Type, evenement.Note, evenement.EtapeCorrigeeId, evenement.RelanceId));
    }

    [Theory]
    [InlineData("hier")]
    [InlineData("2026-11-02T08:15:00Z")]
    [InlineData("02/11/2026 08:15:00")]
    public async Task Un_instant_d_une_autre_ecriture_fait_echouer_la_lecture_sans_citer_sa_valeur(string ecriture)
    {
        await CreerLaBaseAsync();
        Executer($"""
            INSERT INTO "Entreprises" ("Id", "Nom", "Cle") VALUES (1, 'Atelier fictif', 'atelier fictif');
            INSERT INTO "Candidatures" ("Id", "EntrepriseId", "Poste", "EnvoyeeLe", "Statut") VALUES (1, 1, 'Comptable', '{ecriture}', 'Envoyee');
            """);
        await using var instantane = await new SourceDeLExport(Dossier).OuvrirAsync(Annulation);

        var erreur = await Assert.ThrowsAsync<InvalidDataException>(() => LireToutAsync(instantane.Candidatures(Annulation)));
        Assert.DoesNotContain(ecriture, erreur.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task L_instantane_ne_prend_aucun_verrou_d_ecriture_et_une_autre_connexion_ecrit_sans_l_attendre()
    {
        await CreerLaBaseAsync();
        await using var instantane = await new SourceDeLExport(Dossier).OuvrirAsync(Annulation);
        Assert.Empty(await LireToutAsync(instantane.Entreprises(Annulation)));

        // Une transaction d'écriture immédiate passe tout de suite, l'instantané ouvert : il ne tient qu'une lecture.
        using var connexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Dossier.Fichier, Pooling = false, DefaultTimeout = 1 }.ToString());
        connexion.Open();
        using (var transaction = connexion.BeginTransaction(deferred: false))
        {
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = """INSERT INTO "Entreprises" ("Id", "Nom", "Cle") VALUES (1, 'Atelier fictif', 'atelier fictif');""";
            commande.ExecuteNonQuery();
            transaction.Commit();
        }

        Assert.Equal(["1"], Lire("SELECT COUNT(*) FROM \"Entreprises\""));
    }

    private async Task CreerLaBaseAsync()
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>()
            .UseSqlite(Dossier.ChaineDeConnexion())
            .Options;
        await using (var baseDeSollicito = new BaseDeSollicito(options))
        {
            await baseDeSollicito.GetService<IMigrator>().MigrateAsync(cancellationToken: Annulation);
        }

        Assert.Equal("wal", await CopieDeLaBase.ModeDuJournalAsync(Dossier.Fichier, Annulation));
    }

    private static async Task<List<T>> LireToutAsync<T>(IAsyncEnumerable<T> lignes)
    {
        var toutes = new List<T>();
        await foreach (var ligne in lignes)
        {
            toutes.Add(ligne);
        }

        return toutes;
    }

    private void Executer(string instructions)
    {
        using var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(Dossier.Fichier, SqliteOpenMode.ReadWrite));
        connexion.Open();
        using var commande = connexion.CreateCommand();
        commande.CommandText = instructions;
        commande.ExecuteNonQuery();
    }

    private string[] Lire(string requete)
    {
        using var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(Dossier.Fichier, SqliteOpenMode.ReadOnly));
        connexion.Open();
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        using var lecture = commande.ExecuteReader();
        var valeurs = new List<string>();
        while (lecture.Read())
        {
            valeurs.Add(Convert.ToString(lecture.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return [.. valeurs];
    }
}
