using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// La vérification d'une copie à restaurer, sur sa copie de travail (#28, ADR-0012) : une copie abîmée ou étrangère n'est
/// jamais restaurée, une copie plus ancienne se migre, une plus récente est refusée. La copie peut venir d'un dossier
/// partagé : ni vue, ni déclencheur, ni table virtuelle, ni fonction du schéma ne s'exécute pendant sa vérification. Tout
/// se passe dans un dossier temporaire ; données fictives.
/// </summary>
public sealed class VerificationDUneCopieTests : IDisposable
{
    // La migration de #12, deux avant la dernière du code.
    private const string MigrationDe12 = "20261120135647_HistoriqueDesCandidatures";

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private string Base => Path.Combine(_dossier, "sollicito.db");

    private string Copie => Path.Combine(_dossier, "copie-de-travail.db");

    public VerificationDUneCopieTests() => CreerUneBaseAsync(Base, migration: null).GetAwaiter().GetResult();

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
    public async Task Une_copie_au_schema_de_l_exe_est_saine_sans_migration_et_la_base_n_est_pas_touchee()
    {
        await CreerUneBaseAsync(Copie, migration: null);
        Executer(Copie, "INSERT INTO Entreprises (Nom, Cle) VALUES ('Atelier fictif', 'atelier fictif')");
        var empreinte = Empreinte(Base);

        var verdict = await VerifierAsync();

        Assert.Equal(new VerdictDUneCopie(SorteDeVerdict.Saine, 0), verdict);
        Assert.Equal(empreinte, Empreinte(Base));
        Assert.Equal(["Atelier fictif"], Lire(Copie, "SELECT Nom FROM Entreprises"));
    }

    [Fact]
    public async Task Une_copie_plus_ancienne_se_migre_sur_la_copie_de_travail_sans_rien_perdre()
    {
        await CreerUneBaseAsync(Copie, MigrationDe12);
        Executer(Copie, "INSERT INTO Entreprises (Id, Nom, Cle) VALUES (1, 'Lumière Verte SA', 'lumiere verte')");
        Executer(Copie, "INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES (1, 1, 'Comptable fictive', '2026-11-02 09:00:00', 'Envoyee')");

        var verdict = await VerifierAsync();

        Assert.Equal(SorteDeVerdict.Saine, verdict.Sorte);
        Assert.Equal(2, verdict.MigrationsAppliquees);
        Assert.Equal(Lire(Base, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY 1"), Lire(Copie, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY 1"));
        Assert.Equal(["Comptable fictive"], Lire(Copie, "SELECT Poste FROM Candidatures"));
    }

    [Fact]
    public async Task Une_ligne_restee_dans_le_verrou_des_migrations_de_la_copie_ne_bloque_pas_sa_migration()
    {
        await CreerUneBaseAsync(Copie, MigrationDe12);
        Executer(Copie, $"INSERT INTO \"{VerrouDesMigrations.Table}\" (\"Id\", \"Timestamp\") VALUES (1, '2026-11-19 12:00:00+00:00')");

        // Sans le retrait de la ligne, MigrateAsync l'attendrait sans fin : ce délai fait échouer le test au lieu de le bloquer.
        var verdict = await Task.Run(VerifierAsync).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDeVerdict.Saine, verdict.Sorte);
        Assert.Equal(["0"], Lire(Copie, $"SELECT COUNT(*) FROM \"{VerrouDesMigrations.Table}\""));
    }

    [Fact]
    public async Task Une_copie_d_une_version_plus_recente_est_refusee()
    {
        await CreerUneBaseAsync(Copie, migration: null);
        Executer(Copie, "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29991231000000_UneVersionAVenir', '10.0.15')");

        Assert.Equal(SorteDeVerdict.PlusRecente, (await VerifierAsync()).Sorte);
    }

    [Theory]
    [InlineData("DROP TABLE __EFMigrationsHistory")]
    [InlineData("DELETE FROM __EFMigrationsHistory")]
    [InlineData("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20261120000000_InconnueAuMilieu', '10.0.15')")]
    [InlineData("DELETE FROM __EFMigrationsHistory WHERE MigrationId = '20261120135647_HistoriqueDesCandidatures'")]
    [InlineData("CREATE TABLE Etrangere (Id INTEGER PRIMARY KEY)")]
    [InlineData("ALTER TABLE Candidatures ADD COLUMN Etrangere TEXT")]
    [InlineData("DROP INDEX IX_Relances_UneAFaireParCandidature")]
    [InlineData("CREATE VIEW Vue AS SELECT Id FROM Candidatures")]
    [InlineData("CREATE TRIGGER Declencheur AFTER INSERT ON Candidatures BEGIN DELETE FROM Candidatures; END")]
    [InlineData("CREATE VIRTUAL TABLE Virtuelle USING fts5(Texte)")]
    public async Task Une_copie_qui_n_a_pas_le_schema_de_sollicito_est_etrangere(string retouche)
    {
        await CreerUneBaseAsync(Copie, migration: null);
        Executer(Copie, retouche);

        Assert.Equal(SorteDeVerdict.Etrangere, (await VerifierAsync()).Sorte);
    }

    [Fact]
    public async Task Une_copie_d_une_autre_taille_de_page_est_etrangere()
    {
        await CreerUneBaseAsync(Copie, migration: null);
        // La taille de page ne change qu'au VACUUM qui suit, sur la même connexion.
        Executer(Copie, "PRAGMA page_size = 8192; VACUUM;");
        Assert.Equal(["8192"], Lire(Copie, "PRAGMA page_size"));

        Assert.Equal(SorteDeVerdict.Etrangere, (await VerifierAsync()).Sorte);
    }

    [Fact]
    public async Task Une_fonction_inconnue_cachee_dans_un_index_du_schema_fait_refuser_la_copie()
    {
        await CreerUneBaseAsync(Copie, migration: null);
        using (var connexion = Ouvrir(Copie))
        {
            connexion.CreateFunction("piege", (long valeur) => valeur, isDeterministic: true);
            ExecuterSur(connexion, "CREATE INDEX IX_Piege ON Candidatures (piege(Id))");
        }

        // integrity_check évaluerait cet index sur expression : la copie est refusée, et la base n'a pas changé.
        Assert.NotEqual(SorteDeVerdict.Saine, (await VerifierAsync()).Sorte);
    }

    [Theory]
    [InlineData("contrainte CHECK ajoutée")]
    [InlineData("contrainte CHECK avec une fonction")]
    [InlineData("collation ajoutée à une colonne")]
    public async Task Une_table_dont_le_texte_est_retouche_est_etrangere(string retouche)
    {
        await CreerUneBaseAsync(Copie, migration: null);
        var texte = Assert.Single(Lire(Copie, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Entreprises'"));
        var retouchee = retouche switch
        {
            "contrainte CHECK ajoutée" => texte[..texte.LastIndexOf(')')] + ", CHECK (0))",
            "contrainte CHECK avec une fonction" => texte[..texte.LastIndexOf(')')] + ", CHECK (length(\"Nom\") > 1000))",
            _ => texte.Replace("\"Nom\" TEXT", "\"Nom\" TEXT COLLATE NOCASE", StringComparison.Ordinal),
        };
        Assert.NotEqual(texte, retouchee);
        // Le texte de la table se retouche dans sqlite_master, comme un outil le ferait : ses colonnes, ses clés et ses index
        // restent les mêmes.
        using (var connexion = Ouvrir(Copie))
        {
            using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA writable_schema = ON; UPDATE sqlite_master SET sql = $texte WHERE type = 'table' AND name = 'Entreprises'; PRAGMA writable_schema = OFF;";
            commande.Parameters.AddWithValue("$texte", retouchee);
            commande.ExecuteNonQuery();
        }

        Assert.Equal([retouchee], Lire(Copie, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Entreprises'"));
        Assert.Equal(new VerdictDUneCopie(SorteDeVerdict.Etrangere, 0), await VerifierAsync());
    }

    [Fact]
    public void Une_connexion_reglee_pour_un_fichier_non_sur_coupe_vues_declencheurs_schema_inscriptible_et_grosses_valeurs()
    {
        using (var connexion = Ouvrir(Copie, SqliteOpenMode.ReadWriteCreate))
        {
            ExecuterSur(connexion, "CREATE TABLE Notes (Texte TEXT); CREATE VIEW Vue AS SELECT Texte FROM Notes; CREATE TRIGGER Declencheur AFTER INSERT ON Notes BEGIN DELETE FROM Notes; END");
        }

        using var reglee = Ouvrir(Copie);
        CopieDeLaBase.ReglerPourUnFichierNonSur(reglee);

        Assert.Equal(["0"], LireSur(reglee, "PRAGMA trusted_schema"));
        Assert.Equal(["1"], LireSur(reglee, "PRAGMA cell_size_check"));
        Assert.Equal(["0"], LireSur(reglee, "PRAGMA mmap_size"));
        Assert.Contains("prohibited", Assert.Throws<SqliteException>(() => LireSur(reglee, "SELECT * FROM Vue")).Message, StringComparison.Ordinal);
        ExecuterSur(reglee, "INSERT INTO Notes VALUES ('le déclencheur ne s''exécute pas')");
        Assert.Equal(["1"], LireSur(reglee, "SELECT COUNT(*) FROM Notes"));
        // Mode défensif : le schéma ne devient pas inscriptible.
        ExecuterSur(reglee, "PRAGMA writable_schema = ON");
        Assert.Equal(["0"], LireSur(reglee, "PRAGMA writable_schema"));
        // Aucune valeur de plus de 10 Mo, aucune base attachée.
        Assert.Equal(18, Assert.Throws<SqliteException>(() => LireSur(reglee, "SELECT length(zeroblob(20000000))")).SqliteErrorCode & 0xFF);
        Assert.Throws<SqliteException>(() => ExecuterSur(reglee, $"ATTACH DATABASE '{Path.Combine(_dossier, "autre.db")}' AS autre"));
    }

    [Fact]
    public async Task Une_cle_etrangere_sans_sa_ligne_fait_une_copie_abimee()
    {
        await CreerUneBaseAsync(Copie, migration: null);
        using (var connexion = Ouvrir(Copie))
        {
            ExecuterSur(connexion, "PRAGMA foreign_keys = OFF");
            ExecuterSur(connexion, "INSERT INTO Candidatures (Id, EntrepriseId, Poste, EnvoyeeLe, Statut) VALUES (1, 42, 'Sans entreprise', '2026-11-02 09:00:00', 'Envoyee')");
        }

        Assert.Equal(SorteDeVerdict.Abimee, (await VerifierAsync()).Sorte);
    }

    [Fact]
    public async Task Une_copie_aux_octets_abimes_ou_qui_n_est_pas_une_base_est_abimee()
    {
        await CreerUneBaseAsync(Copie, migration: null);
        for (var numero = 1; numero <= 200; numero++)
        {
            Executer(Copie, $"INSERT INTO Entreprises (Nom, Cle) VALUES ('Entreprise fictive {numero} au nom assez long pour remplir des pages', 'cle {numero}')");
        }

        var octets = await File.ReadAllBytesAsync(Copie, TestContext.Current.CancellationToken);
        Array.Fill(octets, (byte)0x5A, octets.Length / 2, 4096);
        await File.WriteAllBytesAsync(Copie, octets, TestContext.Current.CancellationToken);
        Assert.Equal(SorteDeVerdict.Abimee, (await VerifierAsync()).Sorte);

        await File.WriteAllTextAsync(Copie, "pas une base", TestContext.Current.CancellationToken);
        Assert.Equal(SorteDeVerdict.Abimee, (await VerifierAsync()).Sorte);
    }

    private Task<VerdictDUneCopie> VerifierAsync() =>
        VerificationDUneCopie.VerifierEtMettreAJourAsync(Copie, Base, TestContext.Current.CancellationToken);

    // Une base de Sollicito, créée par ses migrations jusqu'à celle-ci, ou jusqu'à la dernière, en mode delete comme une copie.
    private static async Task CreerUneBaseAsync(string fichier, string? migration)
    {
        var options = new DbContextOptionsBuilder<BaseDeSollicito>()
            .UseSqlite(CopieDeLaBase.ChaineDeConnexion(fichier, SqliteOpenMode.ReadWriteCreate))
            .Options;
        await using (var baseDeSollicito = new BaseDeSollicito(options))
        {
            await baseDeSollicito.GetService<IMigrator>().MigrateAsync(migration, TestContext.Current.CancellationToken);
        }

        Executer(fichier, "PRAGMA journal_mode = DELETE");
    }

    private static SqliteConnection Ouvrir(string fichier, SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(fichier, mode));
        connexion.Open();
        return connexion;
    }

    private static string[] LireSur(SqliteConnection connexion, string requete)
    {
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

    private static void Executer(string fichier, string instruction)
    {
        using var connexion = Ouvrir(fichier);
        ExecuterSur(connexion, instruction);
    }

    private static void ExecuterSur(SqliteConnection connexion, string instruction)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = instruction;
        commande.ExecuteNonQuery();
    }

    private static string[] Lire(string fichier, string requete)
    {
        using var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
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

    private static string Empreinte(string fichier) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fichier)));
}
