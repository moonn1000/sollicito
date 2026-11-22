using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// Chaque migration de la base tient en une seule transaction (suites de #36, #98 ; #38) : sous SQLite, Entity Framework
/// Core ajoute une clé étrangère en reconstruisant la table, en plusieurs transactions, les clés étrangères coupées entre
/// deux, et un arrêt au milieu laisserait une table temporaire. Le script SQL de chaque migration, tel qu'EF Core
/// l'exécute, ne porte donc qu'une transaction, aucune table temporaire, et ne coupe jamais les clés étrangères. Une base
/// en mémoire, jamais ouverte : seul le code des migrations se lit.
/// </summary>
public sealed class MigrationsEnUneTransactionTests
{
    public static TheoryData<string, string> Migrations()
    {
        using var conception = Conception();
        var donnees = new TheoryData<string, string>();
        var precedente = Migration.InitialDatabase;
        foreach (var migration in conception.Database.GetMigrations())
        {
            donnees.Add(precedente, migration);
            precedente = migration;
        }

        return donnees;
    }

    [Theory]
    [MemberData(nameof(Migrations))]
    public void Une_migration_tient_en_une_seule_transaction_sans_reconstruire_de_table(string precedente, string migration)
    {
        using var conception = Conception();

        var script = conception.GetService<IMigrator>().GenerateScript(precedente, migration);

        Assert.Equal(1, Compter(script, "BEGIN TRANSACTION;"));
        Assert.Equal(1, Compter(script, "COMMIT;"));
        // La transaction ouvre le travail de la migration et se valide après l'inscription de la migration à l'historique.
        Assert.True(script.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal) < script.IndexOf("INSERT INTO \"__EFMigrationsHistory\"", StringComparison.Ordinal));
        Assert.True(script.IndexOf("INSERT INTO \"__EFMigrationsHistory\"", StringComparison.Ordinal) < script.IndexOf("COMMIT;", StringComparison.Ordinal));
        Assert.DoesNotContain("PRAGMA foreign_keys", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ef_temp_", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void La_migration_de_38_ajoute_une_colonne_avec_sa_cle_en_une_instruction_et_son_index_unique()
    {
        using var conception = Conception();

        var script = conception.GetService<IMigrator>().GenerateScript("20261122092245_Offres", "20261122135852_CandidatureDUneOffre");

        // L'instruction tient sur une ligne : le texte de la table, que compare la restauration (#28), ne garde ainsi aucune
        // fin de ligne du fichier source, CRLF ou LF selon la machine qui construit.
        const string Instruction =
            "ALTER TABLE \"Candidatures\" ADD \"OffreId\" INTEGER NULL CONSTRAINT \"FK_Candidatures_Offres_OffreId\" REFERENCES \"Offres\" (\"Id\") ON DELETE SET NULL;";
        Assert.Contains(Instruction, script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Candidatures_OffreId\" ON \"Candidatures\" (\"OffreId\");", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
    }

    private static BaseDeSollicito Conception() =>
        new(new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite("Data Source=:memory:").Options);

    private static int Compter(string texte, string motif)
    {
        var nombre = 0;
        for (var position = texte.IndexOf(motif, StringComparison.Ordinal); position >= 0; position = texte.IndexOf(motif, position + motif.Length, StringComparison.Ordinal))
        {
            nombre++;
        }

        return nombre;
    }
}
