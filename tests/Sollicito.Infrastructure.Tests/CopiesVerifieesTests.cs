using Microsoft.Data.Sqlite;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// Une copie vérifiée (#26, ADR-0008) : elle ne prend son nom qu'une fois relue saine. VACUUM INTO reconstruit tables et
/// index, si bien qu'une copie défaillante ne se fabrique pas en SQL : le contrôle sert contre une écriture abîmée sur le
/// support, que ces tests imitent en abîmant le fichier, ou par un contrôle qui la refuse. Données fictives.
/// </summary>
public sealed class CopiesVerifieesTests : IDisposable
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private string Source => Path.Combine(_dossier, "source.db");

    public CopiesVerifieesTests()
    {
        using var connexion = Ouvrir(Source, SqliteOpenMode.ReadWriteCreate);
        Executer(connexion, "PRAGMA journal_mode = WAL");
        Executer(connexion, "CREATE TABLE Candidatures (Id INTEGER PRIMARY KEY, Poste TEXT NOT NULL)");
        Executer(connexion, "CREATE INDEX IX_Poste ON Candidatures (Poste)");
        for (var numero = 1; numero <= 400; numero++)
        {
            Executer(connexion, $"INSERT INTO Candidatures (Poste) VALUES ('Poste fictif numéro {numero}, avec un texte assez long pour remplir plusieurs pages')");
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dossier, recursive: true);
        }
        catch (IOException)
        {
            // Le dossier temporaire reste : le système le videra.
        }
    }

    [Fact]
    public async Task Une_copie_saine_se_relit_saine()
    {
        var copie = Path.Combine(_dossier, "copie.db");

        await CopiesVerifiees.EcrireAsync(Source, copie, TestContext.Current.CancellationToken);

        Assert.True(await CopieDeLaBase.EstSaineAsync(copie, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Une_copie_abimee_ou_qui_n_est_pas_une_base_ne_se_relit_pas_saine()
    {
        var copie = Path.Combine(_dossier, "copie.db");
        await CopiesVerifiees.EcrireAsync(Source, copie, TestContext.Current.CancellationToken);
        // Des octets abîmés au milieu du fichier, sur les pages des données, comme une écriture ratée sur une clé.
        var octets = await File.ReadAllBytesAsync(copie, TestContext.Current.CancellationToken);
        for (var position = octets.Length / 2; position < (octets.Length / 2) + 2048; position++)
        {
            octets[position] = 0x5A;
        }

        await File.WriteAllBytesAsync(copie, octets, TestContext.Current.CancellationToken);
        var texte = Path.Combine(_dossier, "notes.db");
        await File.WriteAllTextAsync(texte, "pas une base", TestContext.Current.CancellationToken);

        Assert.False(await CopieDeLaBase.EstSaineAsync(copie, TestContext.Current.CancellationToken));
        Assert.False(await CopieDeLaBase.EstSaineAsync(texte, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Une_copie_dont_un_index_ne_suit_plus_sa_table_ne_se_relit_pas_saine()
    {
        using (var connexion = Ouvrir(Source, SqliteOpenMode.ReadWrite))
        {
            Executer(connexion, "CREATE TABLE Entreprises (Id INTEGER PRIMARY KEY, Nom TEXT NOT NULL)");
            Executer(connexion, "CREATE INDEX IX_Nom ON Entreprises (Nom)");
            Executer(connexion, "INSERT INTO Entreprises (Nom) VALUES ('Lumiere Verte'), ('Lune Rousse')");
        }

        var copie = Path.Combine(_dossier, "copie.db");
        await CopiesVerifiees.EcrireAsync(Source, copie, TestContext.Current.CancellationToken);
        long page;
        long taille;
        using (var connexion = Ouvrir(copie, SqliteOpenMode.ReadOnly))
        {
            page = Lire(connexion, "SELECT rootpage FROM sqlite_master WHERE name = 'IX_Nom'");
            taille = Lire(connexion, "PRAGMA page_size");
        }

        // Une lettre changée dans la seule page de l'index : l'index ne désigne plus la ligne de sa table. Le fichier reste
        // lisible, et integrity_check répond l'erreur au lieu de lever une exception.
        var octets = await File.ReadAllBytesAsync(copie, TestContext.Current.CancellationToken);
        var debut = (int)((page - 1) * taille);
        var position = octets.AsSpan(debut, (int)taille).IndexOf("Lune Rousse"u8);
        Assert.True(position >= 0);
        octets[debut + position + 5] = (byte)'P';
        await File.WriteAllBytesAsync(copie, octets, TestContext.Current.CancellationToken);

        Assert.False(await CopieDeLaBase.EstSaineAsync(copie, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Une_copie_qui_ne_se_verifie_pas_ne_prend_pas_son_nom_et_ne_laisse_rien()
    {
        // La copie d'hier, qui porte déjà le nom : elle doit rester telle quelle.
        var destination = Path.Combine(_dossier, "copie.db");
        await File.WriteAllTextAsync(destination, "copie d'hier", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CopiesVerifiees.EcrireAsync(Source, destination, TestContext.Current.CancellationToken, verifier: (_, _) => Task.FromResult(false)));

        Assert.Equal("copie d'hier", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal(["copie.db", "source.db"], Directory.EnumerateFiles(_dossier).Select(Path.GetFileName).Where(nom => !nom!.StartsWith("source.db-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    private static SqliteConnection Ouvrir(string fichier, SqliteOpenMode mode)
    {
        var connexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fichier, Mode = mode, Pooling = false }.ToString());
        connexion.Open();
        return connexion;
    }

    private static long Lire(SqliteConnection connexion, string requete)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        return (long)commande.ExecuteScalar()!;
    }

    private static void Executer(SqliteConnection connexion, string instruction)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = instruction;
        commande.ExecuteNonQuery();
    }
}
