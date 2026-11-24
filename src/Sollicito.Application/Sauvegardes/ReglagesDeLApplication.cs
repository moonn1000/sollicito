using System.Text.Json;
using System.Text.Json.Nodes;
using Sollicito.Application.IA;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Sauvegardes;

/// <summary>
/// Les réglages de l'application (ADR-0008) : un fichier JSON du dossier de données, hors de la base, que ni les copies
/// ni une restauration n'emportent. Le dossier des sauvegardes en est le premier (#26), le délai par défaut des relances
/// le deuxième (#14, ADR-0011), le fournisseur d'IA le troisième (#43, ADR-0016).
/// <list type="bullet">
/// <item>Chaque réglage se lit à part : un réglage mal écrit rend sa valeur par défaut, en le disant, sans toucher aux
/// autres.</item>
/// <item>Chaque écriture ne change que son réglage, et garde le reste du fichier, réglages inconnus compris.</item>
/// </list>
/// </summary>
/// <param name="DossierDesSauvegardes">
/// Le dossier choisi pour les copies du jour, en chemin absolu ; <c>null</c> pour le dossier par défaut.
/// </param>
/// <param name="DelaiDesRelances">
/// Le délai par défaut d'une relance, en jours ; <c>null</c> pour celui d'usine (<see cref="DelaiDeRelance.ParDefaut"/>).
/// </param>
/// <param name="Fournisseur">
/// Le fournisseur d'IA choisi ; <c>null</c> tant qu'aucun choix n'est enregistré, ce qui vaut aucune IA. « Aucune IA »
/// choisie s'écrit, elle : la carte du tableau de bord ne la propose plus (#43).
/// </param>
public sealed record ReglagesDeLApplication(string? DossierDesSauvegardes, int? DelaiDesRelances = null, FournisseurDIA? Fournisseur = null)
{
    /// <summary>Le nom du fichier des réglages, dans le dossier de données.</summary>
    public const string NomDuFichier = "reglages.json";

    /// <summary>La taille au-delà de laquelle le fichier n'est pas lu : quelques réglages tiennent en bien moins.</summary>
    public const int TailleMaximale = 64 * 1024;

    private const string ProprieteDuDossier = "dossierDesSauvegardes";

    private const string ProprieteDuDelai = "delaiDesRelances";

    private const string ProprieteDuFournisseur = "fournisseurDIA";

    private static readonly JsonSerializerOptions Ecriture = new() { WriteIndented = true };

    /// <summary>Les réglages d'un premier lancement, ou d'un fichier absent.</summary>
    public static ReglagesDeLApplication ParDefaut { get; } = new(DossierDesSauvegardes: null);

    /// <summary>Le délai par défaut des relances qui s'applique : celui du réglage, ou celui d'usine.</summary>
    public int DelaiDesRelancesEnVigueur => DelaiDesRelances ?? DelaiDeRelance.ParDefaut;

    /// <summary>Le fournisseur d'IA qui s'applique : celui du réglage, ou aucun.</summary>
    public FournisseurDIA FournisseurEnVigueur => Fournisseur ?? FournisseurDIA.Aucun;

    /// <summary>
    /// Lit le texte du fichier, ou son absence (<c>null</c>). Absent, il rend les réglages par défaut. Illisible en entier
    /// (JSON mal formé, propriété répétée, autre chose qu'un objet), il les rend aussi, en le disant pour chaque réglage.
    /// Lisible, chaque réglage se lit à part : un dossier qui n'est pas un chemin absolu, un délai qui n'est pas un entier
    /// de <see cref="DelaiDeRelance.Minimum"/> à <see cref="DelaiDeRelance.Maximum"/>, ou un fournisseur d'IA qui n'est pas
    /// l'un des noms exacts, rend sa valeur par défaut, en le disant, sans toucher aux autres.
    /// </summary>
    public static LectureDesReglages Lire(string? texte)
    {
        if (texte is null)
        {
            return new LectureDesReglages(ParDefaut, Lisible: true);
        }

        if (LireLObjet(texte) is not { } reglages)
        {
            return LectureDesReglages.IllisibleEnEntier;
        }

        var dossierLisible = EssayerDeLireLeDossier(reglages, out var dossier);
        var delaiLisible = EssayerDeLireLeDelai(reglages, out var delai);
        var fournisseurLisible = EssayerDeLireLeFournisseur(reglages, out var fournisseur);
        return new LectureDesReglages(
            new ReglagesDeLApplication(dossier, delai, fournisseur),
            dossierLisible,
            delaiLisible,
            FournisseurLisible: fournisseurLisible);
    }

    /// <summary>
    /// Le texte du fichier où le dossier des sauvegardes est désormais <paramref name="dossier"/> (<c>null</c> pour le
    /// dossier par défaut, qui retire la propriété). Le reste du fichier lisible d'avant demeure ; un fichier illisible
    /// en entier se réécrit avec ce seul réglage, comme le veut le choix d'un dossier (ADR-0008).
    /// </summary>
    public static string AvecLeDossier(string? texteDAvant, string? dossier) =>
        Remplacer(texteDAvant, ProprieteDuDossier, dossier is null ? null : JsonValue.Create(dossier));

    /// <summary>
    /// Le texte du fichier où le délai par défaut des relances est désormais <paramref name="jours"/> (<c>null</c> pour
    /// celui d'usine, qui retire la propriété). Le reste du fichier lisible d'avant demeure. L'appelant ne l'écrit pas sur
    /// un fichier illisible en entier : il le réécrirait sans le dossier perdu, et l'alerte des sauvegardes tomberait.
    /// </summary>
    public static string AvecLeDelaiDesRelances(string? texteDAvant, int? jours)
    {
        if (jours is { } valeur && !DelaiDeRelance.EstAdmis(valeur))
        {
            throw new ArgumentOutOfRangeException(nameof(jours), jours, DelaiDeRelance.HorsBornes);
        }

        return Remplacer(texteDAvant, ProprieteDuDelai, jours is null ? null : JsonValue.Create(jours.Value));
    }

    /// <summary>
    /// Le texte du fichier où le fournisseur d'IA est désormais <paramref name="fournisseur"/>, écrit même pour
    /// « aucune IA », qui devient un choix (#43). Le reste du fichier lisible d'avant demeure. L'appelant ne l'écrit pas
    /// sur un fichier illisible en entier, comme le délai, et efface d'abord la clé d'un autre fournisseur (ADR-0016).
    /// </summary>
    public static string AvecLeFournisseurDIA(string? texteDAvant, FournisseurDIA fournisseur) =>
        Remplacer(texteDAvant, ProprieteDuFournisseur, JsonValue.Create(fournisseur.Nom()));

    private static string Remplacer(string? texteDAvant, string propriete, JsonNode? valeur)
    {
        var reglages = LireLObjet(texteDAvant) ?? [];
        if (valeur is null)
        {
            reglages.Remove(propriete);
        }
        else
        {
            reglages[propriete] = valeur;
        }

        return reglages.ToJsonString(Ecriture);
    }

    // Absent ou nul : le dossier par défaut, lisible. Sinon, un chemin absolu valide, ou illisible.
    private static bool EssayerDeLireLeDossier(JsonObject reglages, out string? dossier)
    {
        dossier = null;
        if (!reglages.TryGetPropertyValue(ProprieteDuDossier, out var valeur) || valeur is null)
        {
            return true;
        }

        if (valeur.GetValueKind() == JsonValueKind.String
            && valeur.GetValue<string>() is { } chemin
            && Path.IsPathFullyQualified(chemin)
            && chemin.IndexOfAny(Path.GetInvalidPathChars()) < 0)
        {
            dossier = chemin;
            return true;
        }

        return false;
    }

    // Absent ou nul : le délai d'usine, lisible. Sinon, un entier JSON admis, ou illisible.
    private static bool EssayerDeLireLeDelai(JsonObject reglages, out int? delai)
    {
        delai = null;
        if (!reglages.TryGetPropertyValue(ProprieteDuDelai, out var valeur) || valeur is null)
        {
            return true;
        }

        if (valeur.GetValueKind() == JsonValueKind.Number
            && valeur.AsValue().TryGetValue(out int jours)
            && DelaiDeRelance.EstAdmis(jours))
        {
            delai = jours;
            return true;
        }

        return false;
    }

    // Absent ou nul : jamais choisi, lisible. Sinon, l'un des noms exacts, ou illisible : ni nombre, ni autre casse.
    private static bool EssayerDeLireLeFournisseur(JsonObject reglages, out FournisseurDIA? fournisseur)
    {
        fournisseur = null;
        if (!reglages.TryGetPropertyValue(ProprieteDuFournisseur, out var valeur) || valeur is null)
        {
            return true;
        }

        if (valeur.GetValueKind() == JsonValueKind.String && FournisseursDIA.EssayerDeLire(valeur.GetValue<string>(), out var lu))
        {
            fournisseur = lu;
            return true;
        }

        return false;
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
}

/// <summary>Des réglages lus, et ce qui ne se lisait pas.</summary>
/// <param name="Reglages">Les réglages lus, chacun à sa valeur par défaut s'il ne se lisait pas.</param>
/// <param name="Lisible">
/// Faux si le dossier des sauvegardes ne se lisait pas, ou le fichier entier : le dossier par défaut s'applique alors.
/// </param>
/// <param name="DelaiLisible">
/// Faux si le délai des relances ne se lisait pas, ou le fichier entier : le délai d'usine s'applique alors.
/// </param>
/// <param name="FichierLisible">
/// Faux si le fichier existait mais ne se lisait pas en entier : seul le choix d'un dossier des sauvegardes le réécrit.
/// </param>
/// <param name="FournisseurLisible">
/// Faux si le fournisseur d'IA ne se lisait pas, ou le fichier entier : aucune IA ne s'applique alors.
/// </param>
public sealed record LectureDesReglages(
    ReglagesDeLApplication Reglages,
    bool Lisible,
    bool DelaiLisible = true,
    bool FichierLisible = true,
    bool FournisseurLisible = true)
{
    /// <summary>Un fichier présent qui ne se lit pas en entier : chaque réglage à sa valeur par défaut, et illisible.</summary>
    public static LectureDesReglages IllisibleEnEntier { get; } =
        new(ReglagesDeLApplication.ParDefaut, Lisible: false, DelaiLisible: false, FichierLisible: false, FournisseurLisible: false);
}
