using Microsoft.Data.Sqlite;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// Le remplacement du contenu d'une base par l'API de sauvegarde de SQLite, par tranches de pages (#28, ADR-0012) : une
/// seule transaction de la base, que SQLite ne valide qu'à la dernière tranche ; une erreur entre deux tranches la
/// défait. Il écrit aussi par-dessus une base abîmée, dont les fichiers se gardent d'abord tels quels. Dossier temporaire ;
/// données fictives.
/// </summary>
public sealed class RemplacementDeLaBaseTests : IDisposable
{
    private const int TailleDePage = 4096;
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private string Base => Path.Combine(_dossier, "sollicito.db");

    private string Source => Path.Combine(_dossier, "copie.db");

    public RemplacementDeLaBaseTests()
    {
        Creer(Base, lignes: 400, poste: "Poste fictif d'avant");
        Creer(Source, lignes: 300, poste: "Poste fictif restauré");
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
    public async Task Le_remplacement_avance_par_tranches_dans_une_transaction_que_seule_la_derniere_valide()
    {
        var tranches = 0;
        var vuesPendant = new List<string>();

        await CopieDeLaBase.RemplacerAsync(Source, Base, TestContext.Current.CancellationToken, () =>
        {
            tranches++;
            // Une autre connexion, entre deux tranches, lit encore la base d'avant : rien n'est validé.
            vuesPendant.Add(Compter(Base));
        });

        Assert.True(tranches > 1, $"{tranches} tranche(s)");
        Assert.All(vuesPendant, vue => Assert.Equal("400", vue));
        Assert.Equal("300", Compter(Base));
        Assert.Equal("wal", Lire(Base, "PRAGMA journal_mode"));
        Assert.Equal("ok", Lire(Base, "PRAGMA integrity_check"));
    }

    [Fact]
    public async Task Une_erreur_entre_deux_tranches_defait_le_remplacement_et_laisse_la_base_d_avant_entiere()
    {
        var erreur = new IOException("Disque plein (essai).");

        var levee = await Assert.ThrowsAsync<IOException>(() => CopieDeLaBase.RemplacerAsync(Source, Base, TestContext.Current.CancellationToken, () => throw erreur));

        Assert.Same(erreur, levee);
        Assert.Equal("400", Compter(Base));
        Assert.Equal("ok", Lire(Base, "PRAGMA integrity_check"));
        Assert.Equal("wal", Lire(Base, "PRAGMA journal_mode"));
    }

    [Theory]
    [InlineData("racine de la table", 2, 0x5A)]
    [InlineData("page du milieu", 0, 0x5A)]
    [InlineData("page du milieu, mise à zéro", 0, 0x00)]
    [InlineData("dernière page", -1, 0x5A)]
    public async Task Une_base_abimee_se_remplace_et_ses_fichiers_se_gardent_d_abord_tels_quels(string _, int page, byte valeur)
    {
        var pages = (int)(new FileInfo(Base).Length / TailleDePage);
        Abimer(Base, page switch { 0 => pages / 2, -1 => pages, _ => page }, valeur);
        var abimee = await File.ReadAllBytesAsync(Base, TestContext.Current.CancellationToken);
        Assert.False(await CopieDeLaBase.SeRelitSaineAsync(Base, TestContext.Current.CancellationToken));
        // VACUUM INTO ne la copie plus : seule une copie brute la garde.
        await Assert.ThrowsAsync<SqliteException>(() => CopiesVerifiees.EcrireAsync(Base, Path.Combine(_dossier, "avant.db"), TestContext.Current.CancellationToken));
        var brute = Path.Combine(_dossier, "avant-brute.db");

        await CopiesBrutes.EcrireUneCopieBruteAsync(Base, brute, TestContext.Current.CancellationToken);
        await CopieDeLaBase.RemplacerAsync(Source, Base, TestContext.Current.CancellationToken);

        Assert.Equal(abimee, await File.ReadAllBytesAsync(brute, TestContext.Current.CancellationToken));
        Assert.True(await CopieDeLaBase.SeRelitSaineAsync(Base, TestContext.Current.CancellationToken));
        Assert.Equal("300", Compter(Base));
        Assert.Equal("wal", Lire(Base, "PRAGMA journal_mode"));
        Assert.False(File.Exists(Path.Combine(_dossier, "avant.db")));
        Assert.DoesNotContain(Directory.EnumerateFiles(_dossier), fichier => fichier.EndsWith(".partielle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_copie_brute_garde_aussi_le_journal_et_ne_laisse_rien_de_provisoire_quand_elle_manque()
    {
        await File.WriteAllTextAsync(Base + "-wal", "journal fictif", TestContext.Current.CancellationToken);
        var brute = Path.Combine(_dossier, "avant-brute.db");

        await Assert.ThrowsAsync<IOException>(() => CopiesBrutes.EcrireUneCopieBruteAsync(Base, brute, TestContext.Current.CancellationToken, () => throw new IOException("Disque plein (essai).")));
        Assert.DoesNotContain(Directory.EnumerateFiles(_dossier), fichier => fichier.EndsWith(".partielle", StringComparison.Ordinal) || fichier.StartsWith(brute, StringComparison.Ordinal));

        await CopiesBrutes.EcrireUneCopieBruteAsync(Base, brute, TestContext.Current.CancellationToken);
        Assert.Equal(await File.ReadAllBytesAsync(Base, TestContext.Current.CancellationToken), await File.ReadAllBytesAsync(brute, TestContext.Current.CancellationToken));
        Assert.Equal("journal fictif", await File.ReadAllTextAsync(brute + "-wal", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Directory.EnumerateFiles(_dossier), fichier => fichier.EndsWith(".partielle", StringComparison.Ordinal));
    }

    private static void Creer(string fichier, int lignes, string poste)
    {
        using var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(fichier, SqliteOpenMode.ReadWriteCreate));
        connexion.Open();
        Executer(connexion, "PRAGMA journal_mode = WAL");
        Executer(connexion, "CREATE TABLE Candidatures (Id INTEGER PRIMARY KEY, Poste TEXT NOT NULL)");
        Executer(connexion, "CREATE INDEX IX_Poste ON Candidatures (Poste)");
        using var transaction = connexion.BeginTransaction();
        for (var numero = 1; numero <= lignes; numero++)
        {
            using var commande = connexion.CreateCommand();
            commande.CommandText = "INSERT INTO Candidatures (Poste) VALUES ($poste)";
            commande.Parameters.AddWithValue("$poste", $"{poste} numéro {numero:D4}, avec un texte assez long pour remplir plusieurs pages");
            commande.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Abimer(string fichier, int page, byte valeur)
    {
        using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Write, FileShare.None);
        flux.Position = (long)(page - 1) * TailleDePage;
        var octets = new byte[TailleDePage];
        Array.Fill(octets, valeur);
        flux.Write(octets);
    }

    private static string Compter(string fichier) => Lire(fichier, "SELECT COUNT(*) FROM Candidatures");

    private static string Lire(string fichier, string requete)
    {
        using var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
        connexion.Open();
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        return Convert.ToString(commande.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static void Executer(SqliteConnection connexion, string sql)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = sql;
        commande.ExecuteNonQuery();
    }
}
