using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.Offres;

namespace Sollicito.Api.Offres;

/// <summary>
/// Les routes de l'import d'un fichier CSV d'offres (#40, ADR-0015), derrière les contrôles de l'API comme toutes les
/// autres, et derrière la porte de la base (#28). La page lit le fichier ; l'API n'en reçoit que les octets, en base64,
/// dans le corps : jamais un chemin, un nom de fichier ni une adresse, et l'adresse des routes ne porte aucune donnée.
/// <list type="bullet">
/// <item><c>POST /api/offres/import/apercu</c> n'écrit rien : il dit ce que l'import ferait de chaque ligne.</item>
/// <item><c>POST /api/offres/import</c> reçoit le même fichier et les lignes gardées : la base se copie d'abord, comme
/// avant une restauration, puis l'examen se refait dans l'écriture exclusive, et les lignes admises s'importent en une
/// seule transaction. Sans la copie, rien n'est importé.</item>
/// </list>
/// Les journaux n'en notent que des comptes, et la sorte d'un refus : jamais une cellule, ni rien qui en soit tiré.
/// </summary>
internal static class RoutesDeLImport
{
    /// <summary>La borne du corps : le fichier de 2 Mo au plus, en base64 (× 4/3), et son enveloppe JSON.</summary>
    public const long BorneDuCorps = 3 * 1024 * 1024;

    /// <summary>Le titre d'un fichier refusé ; le motif suit, sur <c>contenu</c>.</summary>
    public const string TitreDuRefus = "Le fichier n'est pas importé : il ne suit pas le format.";

    /// <summary>Le titre d'un accord mal formé ; le motif suit, sur <c>garder</c>.</summary>
    public const string TitreDeLAccordRefuse = "L'import n'est pas fait : les lignes gardées sont mal désignées.";

    /// <summary>Le titre d'un import refusé faute de copie de la base avant lui (ADR-0015, D10 de l'ADR-0001).</summary>
    public const string TitreDeLaCopieImpossible = "L'import n'est pas fait : la copie de la base, avant lui, n'a pas pu se faire.";

    /// <summary>
    /// Le temps qu'un import attend le sémaphore des copies pour faire sa copie d'avant : moins que le temps qu'une
    /// restauration laisse aux requêtes en cours (<see cref="RestaurationDeLaBase.DelaiDesRequetesEnCours"/>), pour ne
    /// jamais la retenir.
    /// </summary>
    public static readonly TimeSpan AttenteDeLaCopie = TimeSpan.FromSeconds(5);

    /// <summary>Le motif d'un accord mal formé.</summary>
    public const string AccordMalForme = "Les lignes gardées doivent être des lignes d'offres du fichier, chacune une fois.";

    /// <summary>Le motif d'une ligne qui n'a pas autant de cellules que l'en-tête.</summary>
    public const string LigneMalFormee = "La ligne n'a pas autant de cellules que l'en-tête.";

    /// <summary>Le nom du motif d'une ligne, quand le refus ne tient à aucune colonne.</summary>
    public const string ChampDeLaLigne = "ligne";

    /// <summary>Le nom du champ du fichier, dans un refus.</summary>
    public const string ChampDuContenu = "contenu";

    /// <summary>Le nom du champ des lignes gardées, dans un refus.</summary>
    public const string ChampDesLignesGardees = "garder";

    public static void Declarer(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/offres/import/apercu", ExaminerAsync)
            .WithName("ExaminerUnFichierCsv")
            .WithSummary("L'aperçu de l'import d'un fichier CSV d'offres : ce que l'import ferait de chaque ligne, sans rien écrire")
            .WithDescription(
                "Le corps porte les octets du fichier, en base64 (contenu) : UTF-8, avec ou sans BOM, un en-tête qui nomme les colonnes, "
                + "séparateur ; ou ,. Un fichier hors du format est refusé en entier (400). Corps de 3 Mio au plus.")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("Offres");

        routes.MapPost("/api/offres/import", ImporterAsync)
            .WithName("ImporterUnFichierCsv")
            .WithSummary("Importer un fichier CSV d'offres, en une seule transaction")
            .WithDescription(
                "Le corps porte le même fichier (contenu) et les numéros des lignes dont l'utilisateur garde le doublon possible (garder). "
                + "Une ligne déjà enregistrée ou en double est écartée ; un doublon possible non gardé aussi. Corps de 3 Mio au plus. "
                + "La base se copie d'abord, comme avant une restauration : si la copie ne se fait pas, rien n'est importé (409).")
            .WithMetadata(new RequestSizeLimitAttribute(BorneDuCorps))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .WithTags("Offres");
    }

    private static async Task<Results<Ok<CompteRenduDUnFichier>, ValidationProblem>> ExaminerAsync(
        FichierAExaminer corps,
        ConnecteurDuFichierCsv connecteur,
        IImportDesOffres import,
        ILogger<JournalDeLImport> journal,
        CancellationToken annulation)
    {
        if (Lire(corps.Contenu, journal) is not { } fichier)
        {
            return Refuser(corps.Contenu);
        }

        var lignes = connecteur.RecevoirLesLignes(fichier);
        var sorts = await import.ExaminerUnLotAsync([.. lignes.Select(ligne => ligne.Offre)], annulation);
        var compteRendu = CompteRendu(fichier, lignes, sorts);
        JournalDeLImport.FichierExamine(journal, compteRendu.Comptes.Lignes, compteRendu.Comptes.AImporter, compteRendu.Comptes.DoublonsPossibles, compteRendu.Comptes.DejaEnregistrees + compteRendu.Comptes.EnDouble, compteRendu.Comptes.Invalides, compteRendu.Comptes.LignesVides);
        return TypedResults.Ok(compteRendu);
    }

    private static async Task<Results<Ok<CompteRenduDUnFichier>, ValidationProblem, ProblemHttpResult>> ImporterAsync(
        FichierAImporter corps,
        ConnecteurDuFichierCsv connecteur,
        IImportDesOffres import,
        SauvegardesDeLaBase sauvegardes,
        ILogger<JournalDeLImport> journal,
        CancellationToken annulation)
    {
        if (Lire(corps.Contenu, journal) is not { } fichier)
        {
            return Refuser(corps.Contenu);
        }

        var lignes = connecteur.RecevoirLesLignes(fichier);
        var numeros = fichier.Lignes.Select(ligne => ligne.Numero).ToHashSet();
        var gardees = corps.Garder ?? [];
        if (gardees.Length > LectureDUnFichierCsv.LignesMaximales || gardees.Distinct().Count() != gardees.Length || !gardees.All(numeros.Contains))
        {
            JournalDeLImport.AccordRefuse(journal);
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [ChampDesLignesGardees] = [AccordMalForme] },
                title: TitreDeLAccordRefuse);
        }

        var rangsGardes = new HashSet<int>();
        for (var rang = 0; rang < lignes.Count; rang++)
        {
            if (gardees.Contains(lignes[rang].Numero))
            {
                rangsGardes.Add(rang);
            }
        }

        // D10 de l'ADR-0001 : l'import est précédé d'une sauvegarde, la copie d'avant une restauration, faite juste avant
        // l'écriture exclusive. Sans elle, rien n'est importé.
        if (await sauvegardes.FaireLaCopieDAvantLImportAsync(AttenteDeLaCopie, annulation) is { } motif)
        {
            JournalDeLImport.CopieImpossible(journal, motif);
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: TitreDeLaCopieImpossible);
        }

        JournalDeLImport.CopieFaite(journal);
        var sorts = await import.ImporterUnLotAsync([.. lignes.Select(ligne => ligne.Offre)], rangsGardes, annulation);
        var compteRendu = CompteRendu(fichier, lignes, sorts);
        JournalDeLImport.FichierImporte(journal, compteRendu.Comptes.Importees, compteRendu.Comptes.DejaEnregistrees + compteRendu.Comptes.EnDouble + compteRendu.Comptes.DoublonsPossibles, compteRendu.Comptes.Invalides);
        return TypedResults.Ok(compteRendu);
    }

    // Le fichier lu, ou rien s'il est refusé : le journal ne note que la sorte du refus.
    private static FichierCsv? Lire(byte[]? contenu, ILogger journal)
    {
        switch (LectureDUnFichierCsv.Lire(contenu ?? []))
        {
            case LectureDuFichier.Lu lu:
                return lu.Fichier;
            case LectureDuFichier.Refuse refus:
                JournalDeLImport.FichierRefuse(journal, refus.Sorte.ToString());
                return null;
            default:
                throw new UnreachableException();
        }
    }

    // Le refus du fichier, relu pour son message : la lecture est pure, et le refus le même.
    private static ValidationProblem Refuser(byte[]? contenu) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [ChampDuContenu] = [((LectureDuFichier.Refuse)LectureDUnFichierCsv.Lire(contenu ?? [])).Message],
            },
            title: TitreDuRefus);

    // Le compte rendu, dans l'ordre du fichier : les lignes mal formées, puis le sort de chaque offre reçue, relié à son
    // numéro de ligne. Une ligne invalide ne porte que ses motifs ; une ligne admise, ses champs lus.
    private static CompteRenduDUnFichier CompteRendu(FichierCsv fichier, IReadOnlyList<(int Numero, OffreRecue Offre)> lignes, IReadOnlyList<SortDUneOffre> sorts)
    {
        var numeroDuRang = lignes.Select(ligne => ligne.Numero).ToArray();
        IReadOnlyList<int> Numeros(IReadOnlyList<int> rangs) => [.. rangs.Select(rang => numeroDuRang[rang])];

        var parNumero = new Dictionary<int, LigneDuCompteRendu>();
        foreach (var ligne in fichier.Lignes.Where(ligne => ligne.Cellules is null))
        {
            parNumero[ligne.Numero] = LigneDuCompteRendu.Invalide(ligne.Numero, new Dictionary<string, string[]>(StringComparer.Ordinal) { [ChampDeLaLigne] = [LigneMalFormee] });
        }

        for (var rang = 0; rang < sorts.Count; rang++)
        {
            var numero = numeroDuRang[rang];
            parNumero[numero] = sorts[rang] switch
            {
                SortDUneOffre.Refusee refusee => LigneDuCompteRendu.Invalide(numero, refusee.Refus),
                SortDUneOffre.AImporter a => LigneDuCompteRendu.Admise(numero, SortsDesLignes.AImporter, a.Offre),
                SortDUneOffre.Importee importee => LigneDuCompteRendu.Admise(numero, SortsDesLignes.Importee, importee.Offre) with
                {
                    Offre = importee.Id,
                    Doublons = importee.Doublons?.EnBase,
                    LignesDuFichier = importee.Doublons is null ? null : Numeros(importee.Doublons.RangsDuLot),
                },
                SortDUneOffre.DejaEnregistree deja => LigneDuCompteRendu.Admise(numero, SortsDesLignes.DejaEnregistree, deja.Offre) with { Trouvee = deja.Trouvee },
                SortDUneOffre.EnDouble enDouble => LigneDuCompteRendu.Admise(numero, SortsDesLignes.EnDouble, enDouble.Offre) with { LigneDOrigine = numeroDuRang[enDouble.RangDOrigine] },
                SortDUneOffre.DoublonPossible doublon => LigneDuCompteRendu.Admise(numero, SortsDesLignes.DoublonPossible, doublon.Offre) with
                {
                    Doublons = doublon.Doublons.EnBase,
                    LignesDuFichier = Numeros(doublon.Doublons.RangsDuLot),
                },
                _ => throw new UnreachableException(),
            };
        }

        var compte = parNumero.Values.ToLookup(ligne => ligne.Sort);
        return new CompteRenduDUnFichier(
            [.. parNumero.Values.OrderBy(ligne => ligne.Ligne)],
            new ComptesDuFichier(
                Lignes: fichier.Lignes.Count,
                LignesVides: fichier.LignesVides,
                AImporter: compte[SortsDesLignes.AImporter].Count(),
                Importees: compte[SortsDesLignes.Importee].Count(),
                DejaEnregistrees: compte[SortsDesLignes.DejaEnregistree].Count(),
                EnDouble: compte[SortsDesLignes.EnDouble].Count(),
                DoublonsPossibles: compte[SortsDesLignes.DoublonPossible].Count(),
                Invalides: compte[SortsDesLignes.Invalide].Count()));
    }
}

/// <summary>
/// Le journal de l'import d'un fichier CSV (#40) : des comptes et la sorte d'un refus, jamais une cellule. Témoin de
/// l'écriture, il note deux lignes de comptes, au début et à la fin de la transaction : l'essai de l'arrêt brutal y lit si
/// un arrêt est tombé pendant l'écriture.
/// </summary>
internal sealed partial class JournalDeLImport(ILogger<JournalDeLImport> journal) : ITemoinDeLImport
{
    [LoggerMessage(EventId = 70, Level = LogLevel.Information, Message = "Fichier CSV refusé ({Sorte}).")]
    internal static partial void FichierRefuse(ILogger journal, string sorte);

    [LoggerMessage(EventId = 71, Level = LogLevel.Information, Message = "Fichier CSV examiné : {Lignes} lignes d'offres, {AImporter} à importer, {DoublonsPossibles} doublons possibles, {Ecartees} écartées, {Invalides} invalides, {Vides} lignes vides.")]
    internal static partial void FichierExamine(ILogger journal, int lignes, int aImporter, int doublonsPossibles, int ecartees, int invalides, int vides);

    [LoggerMessage(EventId = 72, Level = LogLevel.Information, Message = "Fichier CSV importé : {Importees} offres importées, {Ecartees} écartées, {Invalides} invalides.")]
    internal static partial void FichierImporte(ILogger journal, int importees, int ecartees, int invalides);

    [LoggerMessage(EventId = 73, Level = LogLevel.Information, Message = "Import refusé : lignes gardées mal désignées.")]
    internal static partial void AccordRefuse(ILogger journal);

    [LoggerMessage(EventId = 76, Level = LogLevel.Information, Message = "Import d'un fichier CSV : copie de la base faite avant lui.")]
    internal static partial void CopieFaite(ILogger journal);

    [LoggerMessage(EventId = 77, Level = LogLevel.Warning, Message = "Import refusé : la copie de la base avant lui n'a pas pu se faire ({Motif}).")]
    internal static partial void CopieImpossible(ILogger journal, string motif);

    public void EcritureCommencee(int offres) => Commencee(journal, offres);

    public void EcritureValidee(int importees) => Validee(journal, importees);

    [LoggerMessage(EventId = 74, Level = LogLevel.Information, Message = "Import d'un fichier CSV : écriture commencée ({Offres} offres reçues).")]
    private static partial void Commencee(ILogger journal, int offres);

    [LoggerMessage(EventId = 75, Level = LogLevel.Information, Message = "Import d'un fichier CSV : écriture validée ({Importees} offres importées).")]
    private static partial void Validee(ILogger journal, int importees);
}

/// <summary>Le corps de l'aperçu : les octets du fichier, en base64, et rien d'autre.</summary>
/// <param name="Contenu">Les octets du fichier.</param>
public sealed record FichierAExaminer(byte[]? Contenu);

/// <summary>Le corps de l'import : les octets du même fichier, et les numéros des lignes dont le doublon possible est gardé.</summary>
/// <param name="Contenu">Les octets du fichier.</param>
/// <param name="Garder">Les numéros de ligne des doublons possibles gardés ; un autre numéro de ligne d'offre est sans effet.</param>
public sealed record FichierAImporter(byte[]? Contenu, int[]? Garder);

/// <summary>Le compte rendu d'un fichier, à l'aperçu comme à l'import : chaque ligne d'offre, dans l'ordre du fichier, puis les comptes.</summary>
public sealed record CompteRenduDUnFichier(IReadOnlyList<LigneDuCompteRendu> Lignes, ComptesDuFichier Comptes);

/// <summary>
/// Le sort d'une ligne d'offre. Une ligne invalide ne porte que son numéro et ses motifs, jamais son contenu ; une ligne
/// admise, son poste, son entreprise et son lieu tels que lus, et ce qui explique son sort.
/// </summary>
/// <param name="Ligne">Son numéro, celui d'un tableur : l'en-tête est la ligne 1.</param>
/// <param name="Sort">Son sort (<see cref="SortsDesLignes"/>).</param>
/// <param name="Poste">Le poste lu, pour une ligne admise.</param>
/// <param name="Entreprise">Le nom de l'entreprise lu, ou rien.</param>
/// <param name="Lieu">Le lieu lu, ou rien.</param>
/// <param name="Offre">L'identifiant de l'offre importée.</param>
/// <param name="Trouvee">L'offre déjà enregistrée, de même empreinte (règle 2).</param>
/// <param name="LigneDOrigine">La ligne d'avant du fichier, de même empreinte (règle 2).</param>
/// <param name="Doublons">Les offres en base de même entreprise et de même poste (règle 3).</param>
/// <param name="LignesDuFichier">Les lignes d'avant du fichier, de même entreprise et de même poste (règle 3).</param>
/// <param name="Refus">Les motifs d'une ligne invalide, par colonne, ou sous « ligne ».</param>
public sealed record LigneDuCompteRendu(
    int Ligne,
    string Sort,
    string? Poste,
    string? Entreprise,
    string? Lieu,
    int? Offre,
    OffreDejaEnregistree? Trouvee,
    int? LigneDOrigine,
    IReadOnlyList<OffreSignalee>? Doublons,
    IReadOnlyList<int>? LignesDuFichier,
    IReadOnlyDictionary<string, string[]>? Refus)
{
    internal static LigneDuCompteRendu Invalide(int ligne, IReadOnlyDictionary<string, string[]> refus) =>
        new(ligne, SortsDesLignes.Invalide, null, null, null, null, null, null, null, null, refus);

    internal static LigneDuCompteRendu Admise(int ligne, string sort, OffreAdmise offre) =>
        new(ligne, sort, offre.Champs.Poste, offre.NomDeLEntreprise, offre.Champs.Lieu, null, null, null, null, null, null);
}

/// <summary>Les sorts d'une ligne, tels que la réponse les écrit.</summary>
public static class SortsDesLignes
{
    /// <summary>À l'aperçu : la ligne s'importera.</summary>
    public const string AImporter = "aImporter";

    /// <summary>À l'import : la ligne est importée.</summary>
    public const string Importee = "importee";

    /// <summary>Une offre de même empreinte est déjà en base : écartée.</summary>
    public const string DejaEnregistree = "dejaEnregistree";

    /// <summary>Une ligne d'avant du fichier a la même empreinte : écartée.</summary>
    public const string EnDouble = "enDouble";

    /// <summary>Un doublon possible : à l'aperçu, il attend l'accord ; à l'import, il est écarté faute d'accord.</summary>
    public const string DoublonPossible = "doublonPossible";

    /// <summary>La ligne est invalide : non importée.</summary>
    public const string Invalide = "invalide";
}

/// <summary>Les comptes d'un fichier.</summary>
/// <param name="Lignes">Les lignes d'offres, en-tête et lignes vides non comprises.</param>
/// <param name="LignesVides">Les lignes vides, ignorées.</param>
/// <param name="AImporter">À l'aperçu, les lignes qui s'importeront.</param>
/// <param name="Importees">À l'import, les lignes importées.</param>
/// <param name="DejaEnregistrees">Les lignes déjà enregistrées, écartées.</param>
/// <param name="EnDouble">Les lignes en double dans le fichier, écartées.</param>
/// <param name="DoublonsPossibles">Les doublons possibles : à l'aperçu, en attente de l'accord ; à l'import, écartés.</param>
/// <param name="Invalides">Les lignes invalides.</param>
public sealed record ComptesDuFichier(int Lignes, int LignesVides, int AImporter, int Importees, int DejaEnregistrees, int EnDouble, int DoublonsPossibles, int Invalides);
