using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Met à jour une base existante sans jamais la laisser à moitié migrée (#26, qui absorbe #27 ; ADR-0008) :
/// <list type="number">
/// <item>une copie d'avant la mise à jour, vérifiée, dans le dossier des sauvegardes par défaut, sur le disque de la
/// base : une mise à jour n'attend jamais un support amovible ;</item>
/// <item>les migrations s'appliquent à une seconde copie, dans le dossier de données, qui se vérifie à son tour ;</item>
/// <item>la copie migrée remplace le contenu de la base par l'API de sauvegarde de SQLite, en une transaction de la
/// base, qui garde son mode WAL.</item>
/// </list>
/// Avant la dernière étape, la base n'est jamais écrite : une migration qui échoue, ou un arrêt brutal, la laisse telle
/// qu'avant, et la version précédente de Sollicito l'ouvre toujours. Un échec lève <see cref="MiseAJourDeLaBaseEchouee"/>,
/// dont le message ne cite aucun chemin, et qui ne garde pas l'erreur d'origine : son message en citerait un.
/// </summary>
public static class MiseAJourDeLaBase
{
    /// <summary>
    /// Applique à la base les migrations qui attendent, jusqu'à <paramref name="migrationCible"/>, la dernière d'entre
    /// elles : elle nomme la copie d'avant, qu'un nouvel essai de la même mise à jour remplace.
    /// </summary>
    public static async Task AppliquerAsync(DossierDeLaBase dossier, string migrationCible, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        var copieDAvant = Path.Combine(dossier.DossierDesSauvegardes, NomsDesCopies.AvantMiseAJour(migrationCible));
        try
        {
            Directory.CreateDirectory(dossier.DossierDesSauvegardes);
            // Les restes d'une copie d'avant interrompue, qui ne désignent aucune copie.
            await CopiesVerifiees.RetirerLesProvisoiresAsync(dossier.DossierDesSauvegardes, nom => NomsDesCopies.EstUnProvisoireDe(nom, installation: null));
            await CopiesVerifiees.EcrireAsync(dossier.Fichier, copieDAvant, annulation);
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            throw new MiseAJourDeLaBaseEchouee(RaisonDeLEchec.CopieDAvant, erreur.GetType().Name);
        }

        var copieDeTravail = Path.Combine(dossier.Chemin, RestesDUnArret.NomDUneCopieDeTravail());
        try
        {
            File.Copy(copieDAvant, copieDeTravail);
            var options = new DbContextOptionsBuilder<BaseDeSollicito>()
                .UseSqlite(CopieDeLaBase.ChaineDeConnexion(copieDeTravail, SqliteOpenMode.ReadWrite))
                .Options;
            await using (var copie = new BaseDeSollicito(options))
            {
                await copie.Database.MigrateAsync(annulation);
            }

            if (!await CopieDeLaBase.EstSaineAsync(copieDeTravail, annulation))
            {
                throw new InvalidDataException("La copie migrée ne se relit pas comme une base saine.");
            }

            await CopieDeLaBase.RemplacerAsync(copieDeTravail, dossier.Fichier, annulation);
            if (!string.Equals(await CopieDeLaBase.ModeDuJournalAsync(dossier.Fichier, annulation), "wal", StringComparison.OrdinalIgnoreCase))
            {
                await CopieDeLaBase.MettreEnModeWalAsync(dossier.Fichier, annulation);
            }
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            throw new MiseAJourDeLaBaseEchouee(RaisonDeLEchec.Migration, erreur.GetType().Name);
        }
        finally
        {
            await CopiesVerifiees.EffacerAsync(copieDeTravail);
        }
    }
}

/// <summary>Ce qui a fait échouer une mise à jour de la base.</summary>
public enum RaisonDeLEchec
{
    /// <summary>La copie d'avant n'a pas pu s'écrire ni se vérifier : rien n'a été tenté.</summary>
    CopieDAvant,

    /// <summary>La copie d'avant est faite, mais les migrations, ou la mise en place, ont échoué.</summary>
    Migration,
}

/// <summary>
/// Une mise à jour de la base qui a échoué : la base est restée telle qu'avant. Son message s'adresse à l'utilisateur,
/// sans aucun chemin du poste ; elle ne garde pas l'erreur d'origine, dont seul le type est retenu.
/// </summary>
public sealed class MiseAJourDeLaBaseEchouee : InvalidOperationException
{
    /// <summary>Le message d'une migration qui a échoué, une fois la copie d'avant faite.</summary>
    public const string MessageDeLaMigration =
        "La mise à jour de votre base a échoué : elle est restée telle qu'avant. Une copie en a été faite avant la "
        + @"tentative, dans %LOCALAPPDATA%\Sollicito\Sauvegardes. Vous pouvez relancer la version précédente de Sollicito.";

    /// <summary>Le message d'une copie d'avant qui n'a pas pu se faire.</summary>
    public const string MessageDeLaCopieDAvant =
        "Sollicito n'a pas pu copier votre base avant de la mettre à jour : il ne l'a pas touchée. Vérifiez la place "
        + "libre sur le disque, puis relancez-le.";

    /// <summary>Un échec, par sa raison et le type de l'erreur d'origine.</summary>
    public MiseAJourDeLaBaseEchouee(RaisonDeLEchec raison, string typeDeLaCause)
        : base(raison == RaisonDeLEchec.CopieDAvant ? MessageDeLaCopieDAvant : MessageDeLaMigration)
    {
        Raison = raison;
        TypeDeLaCause = typeDeLaCause;
    }

    /// <summary>Ce qui a échoué.</summary>
    public RaisonDeLEchec Raison { get; }

    /// <summary>Le type de l'erreur d'origine, seul retenu pour le journal.</summary>
    public string TypeDeLaCause { get; }
}
