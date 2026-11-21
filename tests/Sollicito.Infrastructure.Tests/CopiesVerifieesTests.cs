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
            // l'examine : le renommage échoue, et se reprend. La copie et sa vérification ne rendent pas la main : quand
            // l'écriture la rend, son premier renommage a échoué, et elle attend sa reprise.
            var ecriture = CopiesVerifiees.EcrireAsync(Source, destination, annulation.Token, verifier: (provisoire, _) =>
            {
                tenu = new FileStream(provisoire, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(true);
            });
            Assert.NotNull(tenu);
            Assert.False(ecriture.IsCompleted);

            // L'annulation tombe pendant les reprises ; le fichier se libère aussitôt après, sans attente, pour que
            // l'effacement du provisoire aboutisse dans ses propres reprises.
            annulation.Cancel();
            tenu.Dispose();

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

        // Trois relectures complètes d'abord : la plus courte donne leur durée sur ce poste, même sous la charge.
        var complete = TimeSpan.MaxValue;
        for (var fois = 0; fois < 3; fois++)
        {
            var debutDeLaMesure = Stopwatch.GetTimestamp();
            Assert.True(await CopieDeLaBase.EstSaineAsync(lestee, TestContext.Current.CancellationToken));
            var duree = Stopwatch.GetElapsedTime(debutDeLaMesure);
            complete = duree < complete ? duree : complete;
        }

        using var annulation = new CancellationTokenSource();
        var annuleeLe = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var inscription = annulation.Token.Register(() => annuleeLe.TrySetResult(Stopwatch.GetTimestamp()));
        long commenceeLe = 0;
        long finieLe = 0;
        // Les appels de SQLite sont synchrones : la vérification tourne sur un autre fil, et l'annulation tombe pendant.
        // Ses instants se prennent sur ce fil, pour qu'un retard du test ne compte pas.
        var verification = Task.Run(
            async () =>
            {
                commenceeLe = Stopwatch.GetTimestamp();
                try
                {
                    return await CopieDeLaBase.EstSaineAsync(lestee, annulation.Token);
                }
                finally
                {
                    finieLe = Stopwatch.GetTimestamp();
                }
            },
            TestContext.Current.CancellationToken);
        annulation.CancelAfter(complete / 4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verification);
        // Interrompue, elle rend la main peu après l'annulation, ou après son propre départ s'il est venu plus tard, au
        // lieu de finir la relecture.
        var depuisLAnnulation = Stopwatch.GetElapsedTime(Math.Max(await annuleeLe.Task, commenceeLe), finieLe);
        Assert.True(depuisLAnnulation < complete / 2, $"Relecture annulée rendue {depuisLAnnulation.TotalMilliseconds:F0} ms après l'annulation ; relecture complète en {complete.TotalMilliseconds:F0} ms.");
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
