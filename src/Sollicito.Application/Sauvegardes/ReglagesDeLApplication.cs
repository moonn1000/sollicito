using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sollicito.Application.Sauvegardes;

/// <summary>
/// Les réglages de l'application (ADR-0008) : un fichier JSON du dossier de données, hors de la base, que ni les copies
/// ni une restauration n'emportent. Le dossier des sauvegardes en est le premier ; le délai des relances (#14) et le
/// fournisseur d'IA (#43) s'y ajouteront.
/// </summary>
/// <param name="DossierDesSauvegardes">
/// Le dossier choisi pour les copies du jour, en chemin absolu ; <c>null</c> pour le dossier par défaut.
/// </param>
public sealed record ReglagesDeLApplication(string? DossierDesSauvegardes)
{
    /// <summary>Le nom du fichier des réglages, dans le dossier de données.</summary>
    public const string NomDuFichier = "reglages.json";

    /// <summary>La taille au-delà de laquelle le fichier n'est pas lu : quelques réglages tiennent en bien moins.</summary>
    public const int TailleMaximale = 64 * 1024;

    private const string ProprieteDuDossier = "dossierDesSauvegardes";

    private static readonly JsonSerializerOptions Ecriture = new() { WriteIndented = true };

    /// <summary>Les réglages d'un premier lancement, ou d'un fichier absent.</summary>
    public static ReglagesDeLApplication ParDefaut { get; } = new(DossierDesSauvegardes: null);

    /// <summary>
    /// Lit le texte du fichier, ou son absence (<c>null</c>). Absent, il rend les réglages par défaut. Illisible (JSON
    /// mal formé, autre chose qu'un objet, dossier qui n'est pas un chemin absolu), il les rend aussi, en le disant.
    /// </summary>
    public static LectureDesReglages Lire(string? texte)
    {
        if (texte is null)
        {
            return new LectureDesReglages(ParDefaut, Lisible: true);
        }

        try
        {
            if (JsonNode.Parse(texte) is not JsonObject reglages)
            {
                return Illisible();
            }

            if (!reglages.TryGetPropertyValue(ProprieteDuDossier, out var dossier) || dossier is null)
            {
                return new LectureDesReglages(ParDefaut, Lisible: true);
            }

            return dossier.GetValueKind() == JsonValueKind.String
                && dossier.GetValue<string>() is { } chemin
                && Path.IsPathFullyQualified(chemin)
                && chemin.IndexOfAny(Path.GetInvalidPathChars()) < 0
                ? new LectureDesReglages(new ReglagesDeLApplication(chemin), Lisible: true)
                : Illisible();
        }
        catch (Exception erreur) when (erreur is JsonException or ArgumentException or InvalidOperationException)
        {
            // JSON mal formé, ou propriété répétée, que l'objet refuse en la lisant.
            return Illisible();
        }
    }

    /// <summary>
    /// Le texte du fichier qui garde ces réglages. Les autres réglages du fichier lisible d'avant, qu'une autre version
    /// aurait pu y ranger, restent.
    /// </summary>
    public string Ecrire(string? texteDAvant)
    {
        var reglages = LireLObjet(texteDAvant) ?? [];
        if (DossierDesSauvegardes is null)
        {
            reglages.Remove(ProprieteDuDossier);
        }
        else
        {
            reglages[ProprieteDuDossier] = DossierDesSauvegardes;
        }

        return reglages.ToJsonString(Ecriture);
    }

    private static JsonObject? LireLObjet(string? texte)
    {
        if (texte is null)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(texte) is not JsonObject objet)
            {
                return null;
            }

            // Compter les propriétés les lit toutes : une propriété répétée lève ici, pas à l'écriture.
            _ = objet.Count;
            return objet;
        }
        catch (Exception erreur) when (erreur is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static LectureDesReglages Illisible() => new(ParDefaut, Lisible: false);
}

/// <summary>Des réglages lus, et si le fichier se lisait.</summary>
/// <param name="Reglages">Les réglages lus, ou ceux par défaut.</param>
/// <param name="Lisible">Faux si le fichier existait mais ne se lisait pas : les réglages par défaut s'appliquent alors.</param>
public sealed record LectureDesReglages(ReglagesDeLApplication Reglages, bool Lisible);
