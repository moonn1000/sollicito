using Microsoft.Data.Sqlite;

namespace Prototype9;

record Candidature(long Id, string Entreprise, string Poste);

record Lecture(string Version, IReadOnlyList<Candidature> Candidatures);

/// <summary>Base SQLite du prototype, dans un dossier temporaire. Données fictives.</summary>
static class Base
{
    public static string Creer(string dossier)
    {
        var chemin = Path.Combine(dossier, "prototype.sqlite3");
        using var connexion = Ouvrir(chemin, SqliteOpenMode.ReadWriteCreate);
        using var commande = connexion.CreateCommand();
        commande.CommandText = """
            CREATE TABLE candidature (id INTEGER PRIMARY KEY, entreprise TEXT NOT NULL, poste TEXT NOT NULL);
            INSERT INTO candidature (entreprise, poste) VALUES
                ('Entreprise fictive A', 'Développeur .NET'),
                ('Entreprise fictive B', 'Architecte logiciel');
            """;
        commande.ExecuteNonQuery();
        return chemin;
    }

    public static Lecture Lire(string chemin)
    {
        using var connexion = Ouvrir(chemin, SqliteOpenMode.ReadOnly);
        using var version = connexion.CreateCommand();
        version.CommandText = "SELECT sqlite_version()";
        var texteVersion = (string)version.ExecuteScalar()!;

        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT id, entreprise, poste FROM candidature ORDER BY id";
        using var lecteur = commande.ExecuteReader();
        var candidatures = new List<Candidature>();
        while (lecteur.Read())
        {
            candidatures.Add(new Candidature(lecteur.GetInt64(0), lecteur.GetString(1), lecteur.GetString(2)));
        }
        return new Lecture(texteVersion, candidatures);
    }

    // Sans pool : fermer la connexion ferme vraiment le fichier.
    static SqliteConnection Ouvrir(string chemin, SqliteOpenMode mode)
    {
        var connexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = chemin,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connexion.Open();
        return connexion;
    }
}
