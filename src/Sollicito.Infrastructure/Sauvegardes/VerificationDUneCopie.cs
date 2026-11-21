using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Sauvegardes;

/// <summary>
/// La vérification d'une copie à restaurer, sur sa copie de travail (#28, ADR-0012). La copie peut venir d'un dossier
/// partagé : chaque connexion à elle est réglée pour un fichier qui n'est pas sûr
/// (<see cref="CopieDeLaBase.ReglerPourUnFichierNonSur"/>) avant sa première instruction. Dans l'ordre :
/// <list type="number">
/// <item>ni vue, ni déclencheur, ni table virtuelle : <c>integrity_check</c> évalue les expressions du schéma (index sur
/// expression, index partiels), et appellerait la vérification d'une table virtuelle ;</item>
/// <item><c>PRAGMA integrity_check</c>, puis une taille de page égale à celle de la base ;</item>
/// <item>l'historique des migrations : celui de l'exe, ou un début de celui-ci ; plus long, la copie est plus récente ;</item>
/// <item>aucune clé étrangère sans sa ligne (<c>foreign_key_check</c>) ;</item>
/// <item>le verrou des migrations de la copie de travail en est retiré, quel qu'il soit (EF Core le recrée), puis la copie
/// se migre si elle est plus ancienne, et se relit saine ;</item>
/// <item>son schéma est celui de l'exe, que ses migrations construisent dans une base en mémoire : texte des tables,
/// colonnes, clés étrangères, texte des index ; puis, de nouveau, aucune clé étrangère sans sa ligne.</item>
/// </list>
/// Seule la copie de travail s'écrit. Une erreur d'écriture ou de lecture du disque n'est pas un verdict : elle se lève.
/// </summary>
public static class VerificationDUneCopie
{
    /// <summary>Vérifie la copie de travail, et la met à jour si elle est plus ancienne que l'exe.</summary>
    public static async Task<VerdictDUneCopie> VerifierEtMettreAJourAsync(string copieDeTravail, string baseEnCours, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(copieDeTravail);
        ArgumentNullException.ThrowIfNull(baseEnCours);

        try
        {
            IReadOnlyList<string> appliquees;
            await using (var connexion = await OuvrirAsync(copieDeTravail, SqliteOpenMode.ReadOnly, annulation))
            {
                // Les instructions de SQLite ne regardent pas le jeton : la borne de la vérification et la fermeture les
                // interrompent (sqlite3_interrupt), integrity_check compris.
                using var interruption = CopieDeLaBase.InterrompreSurAnnulation(connexion, annulation);
                var objets = LireLesObjets(connexion, annulation);
                // Une table virtuelle n'a pas de page racine.
                if (objets.Any(objet => objet.Type is not ("table" or "index") || (objet.Type == "table" && objet.PageRacine == 0)))
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Etrangere, 0);
                }

                if (!LireLeControle(connexion, "PRAGMA integrity_check", annulation))
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Abimee, 0);
                }

                if (Valeur(connexion, "PRAGMA page_size", annulation) != await TailleDePageAsync(baseEnCours, annulation)
                    || !objets.Any(objet => objet is { Type: "table", Nom: "__EFMigrationsHistory" }))
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Etrangere, 0);
                }

                appliquees = LireLesLignes(connexion, "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId", annulation);
                if (LireLesLignes(connexion, "PRAGMA foreign_key_check", annulation).Count > 0)
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Abimee, 0);
                }
            }

            var connues = MigrationsDeLExe();
            var aAppliquer = connues.Count - appliquees.Count;
            if (appliquees.Count == 0 || !appliquees.SequenceEqual(connues.Take(appliquees.Count), StringComparer.Ordinal))
            {
                // Plus longue que celle de l'exe, et qui la prolonge : une version plus récente ; sinon, une autre histoire.
                return appliquees.Count > connues.Count && appliquees.Take(connues.Count).SequenceEqual(connues, StringComparer.Ordinal)
                    ? new VerdictDUneCopie(SorteDeVerdict.PlusRecente, 0)
                    : new VerdictDUneCopie(SorteDeVerdict.Etrangere, 0);
            }

            await using (var connexion = await OuvrirAsync(copieDeTravail, SqliteOpenMode.ReadWrite, annulation))
            {
                using var interruption = CopieDeLaBase.InterrompreSurAnnulation(connexion, annulation);
                // La copie de travail n'est qu'à nous : son verrou des migrations s'en retire, quel qu'il soit. Une ligne
                // restée là, ou une contrainte glissée dans la table, ferait attendre ses migrations sans fin (#79). EF Core
                // le recrée tel qu'il l'écrit, à la migration de la copie, ou au lancement suivant.
                Executer(connexion, $"DROP TABLE IF EXISTS \"{VerrouDesMigrations.Table}\"", annulation);

                if (aAppliquer > 0)
                {
                    var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(connexion).Options;
                    await using var copie = new BaseDeSollicito(options);
                    await copie.Database.MigrateAsync(annulation);
                }
            }

            await using (var connexion = await OuvrirAsync(copieDeTravail, SqliteOpenMode.ReadOnly, annulation))
            {
                using var interruption = CopieDeLaBase.InterrompreSurAnnulation(connexion, annulation);
                if (!LireLeControle(connexion, "PRAGMA integrity_check", annulation))
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Abimee, 0);
                }

                if (!LaStructure(connexion, annulation).SequenceEqual(await StructureDeLExeAsync(annulation), StringComparer.Ordinal))
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Etrangere, 0);
                }

                // Les clés étrangères, de nouveau : la migration a pu reconstruire des tables.
                if (LireLesLignes(connexion, "PRAGMA foreign_key_check", annulation).Count > 0)
                {
                    return new VerdictDUneCopie(SorteDeVerdict.Abimee, 0);
                }
            }

            return new VerdictDUneCopie(SorteDeVerdict.Saine, aAppliquer);
        }
        catch (SqliteException erreur) when (annulation.IsCancellationRequested)
        {
            throw new OperationCanceledException("La vérification de la copie a été interrompue.", erreur, annulation);
        }
        catch (SqliteException erreur) when ((erreur.SqliteErrorCode & 0xFF) is raw.SQLITE_CORRUPT or raw.SQLITE_NOTADB)
        {
            return new VerdictDUneCopie(SorteDeVerdict.Abimee, 0);
        }
        catch (SqliteException erreur) when (!EstUneErreurDuDisque(erreur))
        {
            // Une instruction que le fichier refuse : une vue ou une table qui manque, une migration qui ne passe pas.
            return new VerdictDUneCopie(SorteDeVerdict.Etrangere, 0);
        }
    }

    // Lecture, écriture, place, fichier introuvable ou en lecture seule : le disque, pas la copie.
    private static bool EstUneErreurDuDisque(SqliteException erreur) =>
        (erreur.SqliteErrorCode & 0xFF) is raw.SQLITE_IOERR or raw.SQLITE_FULL or raw.SQLITE_CANTOPEN or raw.SQLITE_READONLY or raw.SQLITE_BUSY or raw.SQLITE_LOCKED or raw.SQLITE_NOMEM;

    private static async Task<SqliteConnection> OuvrirAsync(string fichier, SqliteOpenMode mode, CancellationToken annulation)
    {
        var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(fichier, mode));
        try
        {
            await connexion.OpenAsync(annulation);
            CopieDeLaBase.ReglerPourUnFichierNonSur(connexion);
            return connexion;
        }
        catch
        {
            await connexion.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> TailleDePageAsync(string fichier, CancellationToken annulation)
    {
        await using var connexion = new SqliteConnection(CopieDeLaBase.ChaineDeConnexion(fichier, SqliteOpenMode.ReadOnly));
        await connexion.OpenAsync(annulation);
        return Valeur(connexion, "PRAGMA page_size", annulation);
    }

    // Les migrations du code, dans l'ordre : une base en mémoire, jamais ouverte, ne sert qu'à les lister.
    private static List<string> MigrationsDeLExe()
    {
        using var conception = new BaseDeSollicito(new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite("Data Source=:memory:").Options);
        return [.. conception.Database.GetMigrations().Order(StringComparer.Ordinal)];
    }

    // Le schéma que les migrations de l'exe construisent, dans une base en mémoire.
    private static async Task<List<string>> StructureDeLExeAsync(CancellationToken annulation)
    {
        await using var memoire = new SqliteConnection("Data Source=:memory:");
        await memoire.OpenAsync(annulation);
        var options = new DbContextOptionsBuilder<BaseDeSollicito>().UseSqlite(memoire).Options;
        await using (var reference = new BaseDeSollicito(options))
        {
            await reference.Database.MigrateAsync(annulation);
        }

        return LaStructure(memoire, annulation);
    }

    // Texte des tables, colonnes, clés étrangères, texte des index ; ni les tables que tient SQLite, ni le verrou des
    // migrations, retiré de la copie de travail. Les mêmes migrations, jouées dans le même ordre, écrivent le même texte,
    // qu'une copie soit migrée en une fois ou en plusieurs.
    private static List<string> LaStructure(SqliteConnection connexion, CancellationToken annulation)
    {
        var lignes = new List<string>();
        foreach (var objet in LireLesObjets(connexion, annulation).Where(objet => !EstInterne(objet)))
        {
            lignes.Add($"objet|{objet.Type}|{objet.Nom}|{objet.Table}");
            if (objet.Type == "index")
            {
                lignes.Add($"index|{objet.Nom}|{objet.Sql}");
            }
            else if (objet.Type == "table")
            {
                // Le texte de la table : une contrainte CHECK ou une collation ajoutée ne se voit pas dans ses colonnes.
                lignes.Add($"table|{objet.Nom}|{objet.Sql}");
                lignes.AddRange(LireLesLignes(connexion, "SELECT cid || '|' || name || '|' || type || '|' || \"notnull\" || '|' || quote(dflt_value) || '|' || pk || '|' || hidden FROM pragma_table_xinfo($nom) ORDER BY cid", annulation, objet.Nom)
                    .Select(colonne => $"colonne|{objet.Nom}|{colonne}"));
                lignes.AddRange(LireLesLignes(connexion, "SELECT id || '|' || seq || '|' || \"table\" || '|' || \"from\" || '|' || quote(\"to\") || '|' || on_update || '|' || on_delete || '|' || \"match\" FROM pragma_foreign_key_list($nom) ORDER BY id, seq", annulation, objet.Nom)
                    .Select(cle => $"cle|{objet.Nom}|{cle}"));
            }
        }

        lignes.Sort(StringComparer.Ordinal);
        return lignes;
    }

    // Seules s'écartent les tables que SQLite tient lui-même, les index sans texte qu'il crée pour une contrainte UNIQUE ou
    // PRIMARY KEY, déjà lue dans le texte de leur table, et la table du verrou des migrations avec ses objets. Un autre
    // objet nommé « sqlite_… », que seul writable_schema peut écrire, ou un index qui porte le nom du verrou sur une autre
    // table, se compare comme les autres.
    private static bool EstInterne(ObjetDuSchema objet) =>
        objet is { Type: "table", Nom: "sqlite_sequence" or "sqlite_stat1" or "sqlite_stat2" or "sqlite_stat3" or "sqlite_stat4" }
        || (objet is { Type: "index", Sql.Length: 0 } && objet.Nom.StartsWith("sqlite_autoindex_", StringComparison.Ordinal))
        || objet is { Type: "table", Nom: VerrouDesMigrations.Table }
        || objet.Table == VerrouDesMigrations.Table;

    // Chaque instruction regarde d'abord le jeton : sqlite3_interrupt n'arrête qu'une instruction en cours, et une
    // annulation tombée entre deux instructions serait perdue.
    private static List<ObjetDuSchema> LireLesObjets(SqliteConnection connexion, CancellationToken annulation)
    {
        annulation.ThrowIfCancellationRequested();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT type, name, tbl_name, ifnull(sql, ''), ifnull(rootpage, 0) FROM sqlite_master ORDER BY type, name";
        using var lecture = commande.ExecuteReader();
        var objets = new List<ObjetDuSchema>();
        while (lecture.Read())
        {
            objets.Add(new ObjetDuSchema(lecture.GetString(0), lecture.GetString(1), lecture.GetString(2), lecture.GetString(3), lecture.GetInt64(4)));
        }

        return objets;
    }

    private static bool LireLeControle(SqliteConnection connexion, string pragma, CancellationToken annulation) =>
        LireLesLignes(connexion, pragma, annulation) is ["ok"];

    private static List<string> LireLesLignes(SqliteConnection connexion, string requete, CancellationToken annulation, string? nom = null)
    {
        annulation.ThrowIfCancellationRequested();
        using var commande = connexion.CreateCommand();
        commande.CommandText = requete;
        if (nom is not null)
        {
            commande.Parameters.AddWithValue("$nom", nom);
        }

        using var lecture = commande.ExecuteReader();
        var lignes = new List<string>();
        while (lecture.Read())
        {
            lignes.Add(Convert.ToString(lecture.GetValue(0), CultureInfo.InvariantCulture) ?? "");
        }

        return lignes;
    }

    private static string Valeur(SqliteConnection connexion, string requete, CancellationToken annulation) =>
        LireLesLignes(connexion, requete, annulation).Single();

    private static void Executer(SqliteConnection connexion, string instruction, CancellationToken annulation)
    {
        annulation.ThrowIfCancellationRequested();
        using var commande = connexion.CreateCommand();
        commande.CommandText = instruction;
        commande.ExecuteNonQuery();
    }

    private sealed record ObjetDuSchema(string Type, string Nom, string Table, string Sql, long PageRacine);
}

/// <summary>Ce que la vérification dit d'une copie de travail.</summary>
/// <param name="Sorte">Saine, abîmée, étrangère ou plus récente.</param>
/// <param name="MigrationsAppliquees">Les migrations appliquées à la copie de travail, si elle était plus ancienne.</param>
public sealed record VerdictDUneCopie(SorteDeVerdict Sorte, int MigrationsAppliquees);

/// <summary>Les verdicts de la vérification d'une copie.</summary>
public enum SorteDeVerdict
{
    /// <summary>Saine, au schéma de l'exe : elle peut remplacer la base.</summary>
    Saine,

    /// <summary>Elle ne se relit pas saine.</summary>
    Abimee,

    /// <summary>Ce n'est pas une copie de Sollicito que cette version sait lire.</summary>
    Etrangere,

    /// <summary>Elle vient d'une version plus récente de Sollicito.</summary>
    PlusRecente,
}
