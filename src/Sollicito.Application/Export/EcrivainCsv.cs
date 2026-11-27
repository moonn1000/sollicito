using System.Buffers;
using System.IO.Compression;
using System.Text;

namespace Sollicito.Application.Export;

/// <summary>
/// L'export en CSV (#49, ADR-0020 ; <c>docs/export.md</c>) : une archive ZIP d'un fichier par entité, pour un tableur, et
/// <c>export.csv</c>, qui porte le nom et la version du format. Chaque fichier est en UTF-8 avec BOM, au séparateur
/// <c>;</c>, aux guillemets de la RFC 4180, ses lignes finies par CRLF, avec les noms des champs du JSON en en-tête.
/// Chaque cellule qui commence comme une formule reçoit une apostrophe en tête (<see cref="Cellule"/>). Chaque ligne s'écrit
/// dès qu'elle est lue, une entrée de l'archive à la fois : ni une table ni l'archive ne restent en mémoire. Le CSV sert à
/// lire ses données dans un tableur ; le JSON, qui garde chaque valeur telle quelle, fait foi.
/// </summary>
public static class EcrivainCsv
{
    /// <summary>Le séparateur des cellules, celui d'Excel en Belgique et en France.</summary>
    public const char Separateur = ';';

    /// <summary>La fin d'une ligne, celle de la RFC 4180.</summary>
    public const string FinDeLigne = "\r\n";

    /// <summary>Les fichiers de l'archive, dans l'ordre où elle les écrit.</summary>
    public static readonly IReadOnlyList<string> Fichiers =
        ["export.csv", "entreprises.csv", "contacts.csv", "offres.csv", "candidatures.csv", "relances.csv", "evenements.csv"];

    // Les onze débuts d'une cellule qu'OWASP demande de neutraliser : ceux d'une formule, leurs formes pleine chasse, la
    // tabulation, le retour chariot et le saut de ligne (page « CSV Injection », lue le 2026-11-27).
    private static readonly SearchValues<char> DebutsDUneFormule = SearchValues.Create("=+-@\t\r\n＝＋－＠");

    // Ce qui oblige à entourer une cellule de guillemets : le séparateur, un guillemet, une fin de ligne.
    private static readonly SearchValues<char> AEntourer = SearchValues.Create(";\"\r\n");

    // L'UTF-8 avec son BOM, qu'Excel lit sans assistant (ADR-0015, « Contexte »).
    private static readonly UTF8Encoding Utf8AvecBom = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    /// <summary>
    /// Une cellule, telle qu'elle s'écrit dans le fichier : vide pour une valeur absente ; précédée d'une apostrophe si elle
    /// commence comme une formule ; puis entre guillemets, chaque guillemet doublé, si elle porte le séparateur, un
    /// guillemet ou une fin de ligne. Le reste de la valeur ne change pas.
    /// </summary>
    public static string Cellule(string? valeur)
    {
        if (string.IsNullOrEmpty(valeur))
        {
            return "";
        }

        var neutralisee = DebutsDUneFormule.Contains(valeur[0]) ? "'" + valeur : valeur;
        return neutralisee.AsSpan().ContainsAny(AEntourer)
            ? "\"" + neutralisee.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : neutralisee;
    }

    /// <summary>
    /// Écrit l'archive de cet instantané dans le flux, sans le fermer, puis rend le nombre de lignes de chaque entité. Les
    /// entrées de l'archive portent l'instant de l'export, à l'heure de ce fuseau. <paramref name="etapes"/> voit passer
    /// chaque table écrite.
    /// </summary>
    public static async Task<ComptesDeLExport> EcrireAsync(
        IInstantaneDeLaBase instantane,
        Stream sortie,
        DateTime exporteLe,
        TimeZoneInfo fuseau,
        Action<EtapeDeLExport>? etapes,
        CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(instantane);
        ArgumentNullException.ThrowIfNull(sortie);
        ArgumentNullException.ThrowIfNull(fuseau);

        var dateDesEntrees = TimeZoneInfo.ConvertTime(new DateTimeOffset(exporteLe), fuseau);
        using var archive = new ZipArchive(sortie, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        await FichierAsync(
            archive,
            dateDesEntrees,
            "export.csv",
            ["format", "version", "exporteLe"],
            Une([FormatDeLExport.NomDuCsv, FormatDeLExport.Nombre(FormatDeLExport.Version), FormatDeLExport.Instant(exporteLe)]),
            ligne => ligne,
            annulation);

        var entreprises = await FichierAsync(
            archive,
            dateDesEntrees,
            "entreprises.csv",
            ["id", "nom", "cle"],
            instantane.Entreprises(annulation),
            entreprise => [FormatDeLExport.Nombre(entreprise.Id), entreprise.Nom, entreprise.Cle],
            annulation);
        etapes?.Invoke(EtapeDeLExport.EntreprisesEcrites);

        var contacts = await FichierAsync(
            archive,
            dateDesEntrees,
            "contacts.csv",
            ["id", "entrepriseId", "nom", "email"],
            instantane.Contacts(annulation),
            contact => [FormatDeLExport.Nombre(contact.Id), FormatDeLExport.Nombre(contact.EntrepriseId), contact.Nom, contact.Email],
            annulation);
        etapes?.Invoke(EtapeDeLExport.ContactsEcrits);

        var offres = await FichierAsync(
            archive,
            dateDesEntrees,
            "offres.csv",
            ["id", "source", "entrepriseId", "poste", "posteNormalise", "lieu", "publieeLe", "adresse", "texte", "empreinte", "ajouteeLe"],
            instantane.Offres(annulation),
            offre =>
            [
                FormatDeLExport.Nombre(offre.Id),
                offre.Source,
                Nombre(offre.EntrepriseId),
                offre.Poste,
                offre.PosteNormalise,
                offre.Lieu,
                offre.PublieeLe is { } date ? FormatDeLExport.Date(date) : null,
                offre.Adresse,
                offre.Texte,
                offre.Empreinte,
                FormatDeLExport.Instant(offre.AjouteeLe),
            ],
            annulation);
        etapes?.Invoke(EtapeDeLExport.OffresEcrites);

        var candidatures = await FichierAsync(
            archive,
            dateDesEntrees,
            "candidatures.csv",
            ["id", "entrepriseId", "offreId", "poste", "envoyeeLe", "statut"],
            instantane.Candidatures(annulation),
            candidature =>
            [
                FormatDeLExport.Nombre(candidature.Id),
                FormatDeLExport.Nombre(candidature.EntrepriseId),
                Nombre(candidature.OffreId),
                candidature.Poste,
                FormatDeLExport.Instant(candidature.EnvoyeeLe),
                candidature.Statut,
            ],
            annulation);
        etapes?.Invoke(EtapeDeLExport.CandidaturesEcrites);

        var relances = await FichierAsync(
            archive,
            dateDesEntrees,
            "relances.csv",
            ["id", "candidatureId", "destinataireId", "echeance", "etat"],
            instantane.Relances(annulation),
            relance =>
            [
                FormatDeLExport.Nombre(relance.Id),
                FormatDeLExport.Nombre(relance.CandidatureId),
                Nombre(relance.DestinataireId),
                FormatDeLExport.Date(relance.Echeance),
                relance.Etat,
            ],
            annulation);
        etapes?.Invoke(EtapeDeLExport.RelancesEcrites);

        var evenements = await FichierAsync(
            archive,
            dateDesEntrees,
            "evenements.csv",
            ["id", "candidatureId", "sorte", "survenuLe", "ancien", "nouveau", "corrigeId", "type", "note", "relanceId"],
            instantane.Evenements(annulation),
            Evenement,
            annulation);
        etapes?.Invoke(EtapeDeLExport.EvenementsEcrits);

        return new ComptesDeLExport(entreprises, contacts, offres, candidatures, relances, evenements);
    }

    // Une colonne par champ, vide hors de la sorte de l'événement : « corrigeId » vient de la colonne de sa sorte.
    private static string?[] Evenement(EvenementExporte evenement)
    {
        var corrigeId = evenement.Sorte switch
        {
            FormatDeLExport.ChangementDeStatut => evenement.CorrigeId,
            FormatDeLExport.Etape => evenement.EtapeCorrigeeId,
            FormatDeLExport.ClotureDeRelance => null,
            _ => throw new InvalidDataException("Un événement de la base est d'une sorte que Sollicito n'écrit pas."),
        };
        var deStatut = evenement.Sorte == FormatDeLExport.ChangementDeStatut;
        var etape = evenement.Sorte == FormatDeLExport.Etape;
        return
        [
            FormatDeLExport.Nombre(evenement.Id),
            FormatDeLExport.Nombre(evenement.CandidatureId),
            evenement.Sorte,
            FormatDeLExport.Instant(evenement.SurvenuLe),
            deStatut ? evenement.Ancien : null,
            deStatut ? evenement.Nouveau : null,
            Nombre(corrigeId),
            etape ? evenement.Type : null,
            etape ? evenement.Note : null,
            evenement.Sorte == FormatDeLExport.ClotureDeRelance ? Nombre(evenement.RelanceId) : null,
        ];
    }

    private static async Task<int> FichierAsync<T>(
        ZipArchive archive,
        DateTimeOffset dateDesEntrees,
        string nom,
        string[] enTete,
        IAsyncEnumerable<T> lignes,
        Func<T, string?[]> cellules,
        CancellationToken annulation)
    {
        var entree = archive.CreateEntry(nom, CompressionLevel.Optimal);
        entree.LastWriteTime = dateDesEntrees;
        await using var flux = entree.Open();
        await using var texte = new StreamWriter(flux, Utf8AvecBom, FormatDeLExport.Tampon) { NewLine = FinDeLigne };
        await EcrireAsync(texte, enTete, annulation);
        var nombre = 0;
        await foreach (var ligne in lignes.WithCancellation(annulation))
        {
            await EcrireAsync(texte, cellules(ligne), annulation);
            nombre++;
        }

        return nombre;
    }

    private static async Task EcrireAsync(StreamWriter texte, string?[] cellules, CancellationToken annulation)
    {
        for (var rang = 0; rang < cellules.Length; rang++)
        {
            if (rang > 0)
            {
                await texte.WriteAsync(Separateur);
            }

            await texte.WriteAsync(Cellule(cellules[rang]).AsMemory(), annulation);
        }

        await texte.WriteAsync(FinDeLigne.AsMemory(), annulation);
    }

    private static string? Nombre(int? nombre) => nombre is { } valeur ? FormatDeLExport.Nombre(valeur) : null;

    private static async IAsyncEnumerable<string?[]> Une(string?[] ligne)
    {
        await Task.CompletedTask;
        yield return ligne;
    }
}
