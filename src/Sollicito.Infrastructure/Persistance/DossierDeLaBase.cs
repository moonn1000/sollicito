using Microsoft.Data.Sqlite;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Le dossier où vit la base : le dossier de données de l'utilisateur (ADR-0001, D10), hors du dossier de l'exe et
/// hors du dépôt, ou un dossier temporaire dans les tests.
/// </summary>
/// <param name="Chemin">Le chemin absolu du dossier.</param>
public sealed record DossierDeLaBase(string Chemin)
{
    /// <summary>Le nom du fichier de la base, dans le dossier.</summary>
    public const string NomDuFichier = "sollicito.db";

    /// <summary>Le chemin absolu du dossier.</summary>
    public string Chemin { get; } = Path.IsPathFullyQualified(Chemin)
        ? Chemin
        : throw new ArgumentException("Le dossier de la base doit être un chemin absolu.", nameof(Chemin));

    /// <summary>Le chemin du fichier de la base.</summary>
    public string Fichier => Path.Combine(Chemin, NomDuFichier);

    /// <summary>
    /// La chaîne de connexion à la base. Sans regroupement des connexions : une fois la base refermée, plus rien ne
    /// tient son fichier ouvert.
    /// </summary>
    public string ChaineDeConnexion() => new SqliteConnectionStringBuilder
    {
        DataSource = Fichier,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true,
        Pooling = false,
    }.ToString();
}
