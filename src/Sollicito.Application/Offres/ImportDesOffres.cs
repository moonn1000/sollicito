using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Offres;

/// <summary>
/// Le port du cœur (ADR-0001, D6 ; #34 ; ADR-0015) : ce que devient une offre reçue d'un connecteur, seule ou dans un
/// lot. Chaque connecteur convertit ce que sa source lui donne en <see cref="OffreRecue"/>, le modèle commun, puis le
/// confie au cœur.
/// </summary>
public interface IImportDesOffres
{
    /// <summary>
    /// Valide l'offre, la dédoublonne et l'enregistre, ou dit pourquoi elle ne l'est pas. Rien ne s'enregistre ni ne
    /// s'écrase pour une offre déjà enregistrée (règle 2) ; rien ne s'enregistre pour un doublon possible (règle 3),
    /// sauf avec l'accord de l'utilisateur.
    /// </summary>
    /// <param name="offre">L'offre reçue.</param>
    /// <param name="garderLesDeux">L'accord de l'utilisateur pour l'enregistrer malgré un doublon possible.</param>
    /// <param name="annulation">L'annulation de la requête.</param>
    Task<ResultatDeLImport> ImporterAsync(OffreRecue offre, bool garderLesDeux, CancellationToken annulation);

    /// <summary>
    /// Dit ce que deviendrait chaque offre d'un lot (#40), sans rien écrire : refusée, déjà enregistrée, en double dans le
    /// lot, doublon possible, ou à importer. Le sort de chaque offre est au rang de l'offre dans le lot.
    /// </summary>
    Task<IReadOnlyList<SortDUneOffre>> ExaminerUnLotAsync(IReadOnlyList<OffreRecue> lot, CancellationToken annulation);

    /// <summary>
    /// Importe un lot (#40) en une seule écriture exclusive : l'examen se refait, puis les offres à importer, et les
    /// doublons possibles dont le rang est gardé, s'enregistrent ensemble avec leurs entreprises neuves, ou rien ne
    /// s'enregistre. Le sort de chaque offre est au rang de l'offre dans le lot.
    /// </summary>
    /// <param name="lot">Les offres reçues, dans l'ordre de leur source.</param>
    /// <param name="rangsGardes">Les rangs des doublons possibles que l'utilisateur garde ; un autre rang est sans effet.</param>
    /// <param name="annulation">L'annulation de la requête.</param>
    Task<IReadOnlyList<SortDUneOffre>> ImporterUnLotAsync(IReadOnlyList<OffreRecue> lot, IReadOnlySet<int> rangsGardes, CancellationToken annulation);
}

/// <summary>
/// Ce que l'import d'un lot dit de son écriture, en comptes seulement (#40) : le journal de l'API l'écrit, et l'essai de
/// l'arrêt brutal y lit le début et la fin de la transaction.
/// </summary>
public interface ITemoinDeLImport
{
    /// <summary>L'écriture exclusive de l'import vient de commencer, pour un lot de ce nombre d'offres.</summary>
    void EcritureCommencee(int offres);

    /// <summary>L'écriture est validée : ce nombre d'offres est importé.</summary>
    void EcritureValidee(int importees);
}

/// <summary>
/// Le cœur des offres (#36, #40 ; ADR-0013, ADR-0015 ; modèle de domaine, « Dédoublonner les offres ») : il lit les
/// offres reçues (<see cref="LectureDUneOffre"/>), applique les règles 2 et 3, contre la base et à l'intérieur d'un lot,
/// reconnaît l'entreprise par sa clé, sans jamais la renommer, et enregistre, dans une écriture exclusive : deux imports
/// simultanés d'un même texte, ou d'un même poste, ne partent jamais du même état.
/// </summary>
public sealed class ImportDesOffres(IDepotDesOffres depot, TimeProvider horloge, ITemoinDeLImport temoin) : IImportDesOffres
{
    public async Task<ResultatDeLImport> ImporterAsync(OffreRecue offre, bool garderLesDeux, CancellationToken annulation)
    {
        var maintenant = horloge.GetUtcNow().UtcDateTime;
        if (!LectureDUneOffre.EssayerDeLire(offre, Aujourdhui(horloge), out var admise, out var refus))
        {
            return new ResultatDeLImport.Refusee(refus);
        }

        int id;
        await using (var ecriture = await depot.CommencerUneEcritureAsync(annulation))
        {
            // Règle 2 : la même offre, recollée, n'est ni recréée ni écrasée.
            if (await ecriture.TrouverParEmpreinteAsync(admise.Source, admise.Empreinte, annulation) is { } deja)
            {
                return new ResultatDeLImport.DejaEnregistree(deja);
            }

            var entreprise = admise.CleDeLEntreprise is { } cle ? await ecriture.TrouverLEntrepriseAsync(cle, annulation) : null;

            // Règle 3 : une entreprise qui n'est pas encore enregistrée n'a aucune offre, donc aucun doublon possible.
            if (entreprise is not null && !garderLesDeux)
            {
                var doublons = await ecriture.ListerLesDoublonsPossiblesAsync(entreprise.Id, admise.PosteNormalise, sauf: null, annulation);
                if (doublons.Count > 0)
                {
                    return new ResultatDeLImport.DoublonPossible(doublons);
                }
            }

            if (entreprise is null && admise.NomDeLEntreprise is { } nom)
            {
                entreprise = Entreprise.Nouvelle(nom);
                await ecriture.AjouterAsync(entreprise, annulation);
            }

            var importee = Offre.Importer(admise.Source, admise.Empreinte, entreprise?.Id, admise.Champs, admise.Texte, maintenant);
            await ecriture.AjouterAsync(importee, annulation);
            await ecriture.ValiderAsync(annulation);
            id = importee.Id;
        }

        return new ResultatDeLImport.Enregistree(await depot.LireAsync(id, annulation)
            ?? throw new InvalidOperationException("L'offre enregistrée ne se relit pas."));
    }

    public async Task<IReadOnlyList<SortDUneOffre>> ExaminerUnLotAsync(IReadOnlyList<OffreRecue> lot, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(lot);

        // L'écriture exclusive donne un état stable, qu'aucun import ne change pendant l'examen ; quittée sans être
        // validée, elle n'écrit rien.
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        return (await ExaminerAsync(ecriture, lot, Aujourdhui(horloge), annulation)).Sorts;
    }

    public async Task<IReadOnlyList<SortDUneOffre>> ImporterUnLotAsync(IReadOnlyList<OffreRecue> lot, IReadOnlySet<int> rangsGardes, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(rangsGardes);

        var maintenant = horloge.GetUtcNow().UtcDateTime;
        var aujourdhui = Aujourdhui(horloge);
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        temoin.EcritureCommencee(lot.Count);
        var (sorts, entreprises) = await ExaminerAsync(ecriture, lot, aujourdhui, annulation);

        var aImporter = new List<(int Rang, OffreAdmise Offre, DoublonsDUneOffre? Doublons)>();
        for (var rang = 0; rang < sorts.Length; rang++)
        {
            switch (sorts[rang])
            {
                case SortDUneOffre.AImporter a:
                    aImporter.Add((rang, a.Offre, null));
                    break;
                case SortDUneOffre.DoublonPossible d when rangsGardes.Contains(rang):
                    aImporter.Add((rang, d.Offre, d.Doublons));
                    break;
            }
        }

        // Une entreprise neuve se crée une fois, sous le nom de la première offre importée qui la désigne.
        var offres = new List<Offre>(aImporter.Count);
        foreach (var (_, offre, _) in aImporter)
        {
            int? entrepriseId = null;
            if (offre.CleDeLEntreprise is { } cle && offre.NomDeLEntreprise is { } nom)
            {
                if (entreprises[cle] is not { } entreprise)
                {
                    entreprise = Entreprise.Nouvelle(nom);
                    await ecriture.AjouterAsync(entreprise, annulation);
                    entreprises[cle] = entreprise;
                }

                entrepriseId = entreprise.Id;
            }

            offres.Add(Offre.Importer(offre.Source, offre.Empreinte, entrepriseId, offre.Champs, offre.Texte, maintenant));
        }

        await ecriture.AjouterAsync(offres, annulation);
        await ecriture.ValiderAsync(annulation);
        temoin.EcritureValidee(offres.Count);

        for (var i = 0; i < aImporter.Count; i++)
        {
            var (rang, offre, doublons) = aImporter[i];
            sorts[rang] = new SortDUneOffre.Importee(offres[i].Id, offre, doublons);
        }

        return sorts;
    }

    /// <summary>Aujourd'hui, en date locale du poste : la date de publication ne passe pas après.</summary>
    internal static DateOnly Aujourdhui(TimeProvider horloge) =>
        DelaiDeRelance.JourLocal(horloge.GetUtcNow().UtcDateTime, horloge.LocalTimeZone);

    // L'examen d'un lot, dans une écriture : chaque offre lue, puis la règle 2 contre la base et les offres d'avant du lot,
    // puis la règle 3 contre la base et les offres d'avant du lot qu'aucune règle 2 n'écarte. Le sort d'une offre ne
    // dépend pas de l'accord donné aux autres : l'examen et l'import d'un même lot, sur une même base, disent la même
    // chose. Rend aussi les entreprises du lot déjà en base, par clé (rien pour une entreprise neuve).
    private static async Task<(SortDUneOffre[] Sorts, Dictionary<string, Entreprise?> Entreprises)> ExaminerAsync(
        IEcritureDesOffres ecriture,
        IReadOnlyList<OffreRecue> lot,
        DateOnly aujourdhui,
        CancellationToken annulation)
    {
        var sorts = new SortDUneOffre[lot.Count];
        var empreintes = new Dictionary<(SourceDOffre Source, string Empreinte), int>();
        var postes = new Dictionary<(string Cle, string Poste), List<int>>();
        var entreprises = new Dictionary<string, Entreprise?>(StringComparer.Ordinal);
        for (var rang = 0; rang < lot.Count; rang++)
        {
            if (!LectureDUneOffre.EssayerDeLire(lot[rang], aujourdhui, out var admise, out var refus))
            {
                sorts[rang] = new SortDUneOffre.Refusee(refus);
                continue;
            }

            if (await ecriture.TrouverParEmpreinteAsync(admise.Source, admise.Empreinte, annulation) is { } deja)
            {
                sorts[rang] = new SortDUneOffre.DejaEnregistree(admise, deja);
                continue;
            }

            if (!empreintes.TryAdd((admise.Source, admise.Empreinte), rang))
            {
                sorts[rang] = new SortDUneOffre.EnDouble(admise, empreintes[(admise.Source, admise.Empreinte)]);
                continue;
            }

            DoublonsDUneOffre? doublons = null;
            if (admise.CleDeLEntreprise is { } cle)
            {
                if (!entreprises.TryGetValue(cle, out var enBase))
                {
                    enBase = await ecriture.TrouverLEntrepriseAsync(cle, annulation);
                    entreprises[cle] = enBase;
                }

                IReadOnlyList<OffreSignalee> trouvees = enBase is null
                    ? []
                    : await ecriture.ListerLesDoublonsPossiblesAsync(enBase.Id, admise.PosteNormalise, sauf: null, annulation);
                var memePoste = (cle, admise.PosteNormalise);
                if (!postes.TryGetValue(memePoste, out var rangs))
                {
                    rangs = [];
                    postes[memePoste] = rangs;
                }

                if (trouvees.Count > 0 || rangs.Count > 0)
                {
                    doublons = new DoublonsDUneOffre(trouvees, [.. rangs]);
                }

                rangs.Add(rang);
            }

            sorts[rang] = doublons is null ? new SortDUneOffre.AImporter(admise) : new SortDUneOffre.DoublonPossible(admise, doublons);
        }

        return (sorts, entreprises);
    }
}

/// <summary>Ce que rend l'import d'une offre seule.</summary>
public abstract record ResultatDeLImport
{
    private ResultatDeLImport()
    {
    }

    /// <summary>L'offre est enregistrée : sa fiche, doublons possibles compris.</summary>
    public sealed record Enregistree(FicheDUneOffre Fiche) : ResultatDeLImport;

    /// <summary>Un champ est refusé : pour chaque champ refusé, ses messages. Rien n'est enregistré.</summary>
    public sealed record Refusee(IReadOnlyDictionary<string, string[]> Refus) : ResultatDeLImport;

    /// <summary>La même offre est déjà enregistrée (règle 2) : rien n'est enregistré ni écrasé.</summary>
    public sealed record DejaEnregistree(OffreDejaEnregistree Offre) : ResultatDeLImport;

    /// <summary>Des offres de même entreprise et de même poste sont enregistrées (règle 3) : rien n'est enregistré sans l'accord.</summary>
    public sealed record DoublonPossible(IReadOnlyList<OffreSignalee> Doublons) : ResultatDeLImport;
}

/// <summary>Le sort d'une offre d'un lot (#40), à l'examen comme à l'import.</summary>
public abstract record SortDUneOffre
{
    private SortDUneOffre()
    {
    }

    /// <summary>Un champ est refusé : pour chaque champ refusé, ses messages, jamais le texte reçu.</summary>
    public sealed record Refusee(IReadOnlyDictionary<string, string[]> Refus) : SortDUneOffre;

    /// <summary>Une offre de même source et de même empreinte est déjà en base (règle 2) : écartée.</summary>
    public sealed record DejaEnregistree(OffreAdmise Offre, OffreDejaEnregistree Trouvee) : SortDUneOffre;

    /// <summary>Une offre d'avant du lot a la même empreinte (règle 2) : écartée.</summary>
    public sealed record EnDouble(OffreAdmise Offre, int RangDOrigine) : SortDUneOffre;

    /// <summary>
    /// Un doublon possible (règle 3) : à l'examen, il attend l'accord ; à l'import, il est écarté, faute d'accord.
    /// </summary>
    public sealed record DoublonPossible(OffreAdmise Offre, DoublonsDUneOffre Doublons) : SortDUneOffre;

    /// <summary>À l'examen : l'offre s'importera.</summary>
    public sealed record AImporter(OffreAdmise Offre) : SortDUneOffre;

    /// <summary>À l'import : l'offre est enregistrée ; un doublon possible gardé garde ses doublons.</summary>
    public sealed record Importee(int Id, OffreAdmise Offre, DoublonsDUneOffre? Doublons) : SortDUneOffre;
}

/// <summary>Les doublons possibles d'une offre d'un lot (règle 3) : les offres en base, et les rangs des offres d'avant du lot.</summary>
/// <param name="EnBase">Les offres enregistrées de même entreprise et de même poste normalisé, les plus anciennes d'abord.</param>
/// <param name="RangsDuLot">Les rangs des offres d'avant du lot, de même entreprise et de même poste normalisé.</param>
public sealed record DoublonsDUneOffre(IReadOnlyList<OffreSignalee> EnBase, IReadOnlyList<int> RangsDuLot);

/// <summary>Le témoin qui ne note rien : celui des tests qui ne regardent pas l'écriture.</summary>
public sealed class AucunTemoin : ITemoinDeLImport
{
    /// <summary>Le seul témoin muet.</summary>
    public static readonly AucunTemoin Instance = new();

    private AucunTemoin()
    {
    }

    public void EcritureCommencee(int offres)
    {
    }

    public void EcritureValidee(int importees)
    {
    }
}
