using Microsoft.Data.Sqlite;
using Sollicito.Application.Sauvegardes;

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

    /// <summary>Le nom du dossier des sauvegardes par défaut, dans le dossier de la base (ADR-0001, D10).</summary>
    public const string NomDuDossierDesSauvegardes = "Sauvegardes";

    /// <summary>Le chemin du fichier de la base.</summary>
    public string Fichier => Path.Combine(Chemin, NomDuFichier);

    /// <summary>
    /// Le dossier des sauvegardes par défaut, sur le disque de la base : les copies du jour y vont tant qu'aucun autre
    /// n'est choisi, et les copies d'avant une mise à jour toujours (ADR-0008).
    /// </summary>
    public string DossierDesSauvegardes => Path.Combine(Chemin, NomDuDossierDesSauvegardes);

    /// <summary>Le fichier des réglages de l'application, hors de la base (ADR-0008).</summary>
    public string FichierDesReglages => Path.Combine(Chemin, ReglagesDeLApplication.NomDuFichier);

    /// <summary>Le nom du fichier de la clé d'IA chiffrée, dans le dossier de données (ADR-0016).</summary>
    public const string NomDuFichierDeLaCle = "cle-ia.json";

    /// <summary>Le nom du trousseau de Data Protection, dans le dossier de données (ADR-0001, D10 ; ADR-0016).</summary>
    public const string NomDuTrousseau = "Trousseau";

    /// <summary>
    /// Le fichier de la clé d'IA, chiffrée par Data Protection, hors de la base et de ses copies (ADR-0016) : la clé ne
    /// vit nulle part ailleurs, jamais en clair.
    /// </summary>
    public string FichierDeLaCle => Path.Combine(Chemin, NomDuFichierDeLaCle);

    /// <summary>
    /// Le trousseau de Data Protection, dont les clés sont protégées par DPAPI en portée utilisateur (ADR-0001, D9 ;
    /// ADR-0016). Il se déduit de ce dossier, et de lui seul : jamais du dossier par défaut de Data Protection.
    /// </summary>
    public string Trousseau => Path.Combine(Chemin, NomDuTrousseau);

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
