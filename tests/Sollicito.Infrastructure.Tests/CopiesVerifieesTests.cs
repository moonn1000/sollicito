using System.Diagnostics;
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

    [Fact]
    public async Task Une_copie_annulee_pendant_sa_verification_ne_prend_pas_son_nom_meme_saine_et_ne_laisse_rien()
    {
        var destination = Path.Combine(_dossier, "copie.db");
        await File.WriteAllTextAsync(destination, "copie d'hier", TestContext.Current.CancellationToken);
        using var annulation = new CancellationTokenSource();

        // L'annulation tombe pendant la vérification, qui répond pourtant que la copie est saine.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CopiesVerifiees.EcrireAsync(Source, destination, annulation.Token, verifier: async (_, _) =>
            {
                await annulation.CancelAsync();
                return true;
            }));

        Assert.Equal("copie d'hier", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal(["copie.db", "source.db"], Directory.EnumerateFiles(_dossier).Select(Path.GetFileName).Where(nom => !nom!.StartsWith("source.db-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Une_copie_annulee_pendant_que_son_renommage_se_reprend_ne_remplace_pas_celle_d_avant()
    {
        var destination = Path.Combine(_dossier, "copie.db");
        await File.WriteAllTextAsync(destination, "copie d'hier", TestContext.Current.CancellationToken);
        using var annulation = new CancellationTokenSource();
        FileStream? tenu = null;
        try
        {
            // Un autre programme tient le fichier provisoire sans en partager la suppression, comme un antivirus qui
            // l'examine : le renommage échoue, et se reprend.
            var ecriture = CopiesVerifiees.EcrireAsync(Source, destination, annulation.Token, verifier: (provisoire, _) =>
            {
                tenu = new FileStream(provisoire, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(true);
            });
            await Task.Delay(120, TestContext.Current.CancellationToken);

            // L'annulation tombe pendant les reprises, puis le fichier se libère.
            await annulation.CancelAsync();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            tenu?.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ecriture);
        }
        finally
        {
            tenu?.Dispose();
        }

        Assert.Equal("copie d'hier", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal(["copie.db", "source.db"], Directory.EnumerateFiles(_dossier).Select(Path.GetFileName).Where(nom => !nom!.StartsWith("source.db-", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Une_verification_annulee_en_cours_s_interrompt_au_lieu_d_aller_au_bout()
    {
        // Cent mille lignes sous trois index : integrity_check met plusieurs centaines de millisecondes à les relire
        // (relevé le 2026-11-21 : 1,4 s pour deux cent mille lignes).
        var lestee = Path.Combine(_dossier, "lestee.db");
        using (var connexion = Ouvrir(lestee, SqliteOpenMode.ReadWriteCreate))
        {
            Executer(connexion, "CREATE TABLE Lest (A INTEGER, B TEXT, C TEXT)");
            Executer(connexion, "CREATE INDEX IX_A ON Lest (A)");
            Executer(connexion, "CREATE INDEX IX_B ON Lest (B)");
            Executer(connexion, "CREATE INDEX IX_C ON Lest (C)");
            Executer(connexion, "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 100000) INSERT INTO Lest SELECT i, hex(randomblob(8)), hex(randomblob(8)) FROM n");
        }

        // Une relecture complète d'abord, pour mesurer sa durée sur ce poste.
        var montre = Stopwatch.StartNew();
        Assert.True(await CopieDeLaBase.EstSaineAsync(lestee, TestContext.Current.CancellationToken));
        var complete = montre.Elapsed;

        using var annulation = new CancellationTokenSource();
        montre.Restart();
        // Les appels de SQLite sont synchrones : la vérification tourne sur un autre fil, et l'annulation tombe pendant.
        var verification = Task.Run(() => CopieDeLaBase.EstSaineAsync(lestee, annulation.Token), TestContext.Current.CancellationToken);
        annulation.CancelAfter(complete / 4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verification);
        // Interrompue, elle rend la main bien avant la fin d'une relecture complète, et non une fois celle-ci finie.
        Assert.True(montre.Elapsed < complete * 0.75, $"Relecture annulée rendue en {montre.Elapsed.TotalMilliseconds:F0} ms ; relecture complète en {complete.TotalMilliseconds:F0} ms.");
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
