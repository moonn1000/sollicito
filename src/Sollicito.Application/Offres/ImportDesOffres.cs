using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;
using Sollicito.Domain.Relances;

namespace Sollicito.Application.Offres;

/// <summary>
/// Le port du cœur (ADR-0001, D6 ; #34) : ce que devient une offre reçue d'un connecteur. Chaque connecteur convertit
/// ce que sa source lui donne en <see cref="OffreRecue"/>, le modèle commun, puis le confie au cœur.
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
}

/// <summary>
/// Le cœur des offres (#36, ADR-0013 ; modèle de domaine, « Dédoublonner les offres ») : il valide les champs reçus par
/// les règles du domaine, calcule l'empreinte sur les parties du connecteur, applique les règles 2 et 3, reconnaît
/// l'entreprise par sa clé, sans jamais la renommer, et enregistre, dans une écriture exclusive : deux imports simultanés
/// d'un même texte, ou d'un même poste, ne partent jamais du même état.
/// </summary>
public sealed class ImportDesOffres(IDepotDesOffres depot, TimeProvider horloge) : IImportDesOffres
{
    public async Task<ResultatDeLImport> ImporterAsync(OffreRecue offre, bool garderLesDeux, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(offre);
        ArgumentNullException.ThrowIfNull(offre.Champs);

        var maintenant = horloge.GetUtcNow().UtcDateTime;
        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string? texte = null;
        if (offre.Champs.Texte is not null || TexteRequis(offre.Source))
        {
            if (!TexteDUneOffre.EssayerDeLire(offre.Champs.Texte, out texte, out var refusDuTexte))
            {
                refus[SaisieDUneOffre.Champs.Texte] = [refusDuTexte];
            }
        }

        var lus = LireLesChamps(offre.Champs.Entreprise, offre.Champs.Poste, offre.Champs.Lieu, offre.Champs.PublieeLe, offre.Champs.Adresse, Aujourdhui(horloge), refus);
        if (lus is null || refus.Count > 0)
        {
            return new ResultatDeLImport.Refusee(refus);
        }

        var empreinte = EmpreinteDUneOffre.De(offre.PartiesDeLEmpreinte);
        int id;
        await using (var ecriture = await depot.CommencerUneEcritureAsync(annulation))
        {
            // Règle 2 : la même offre, recollée, n'est ni recréée ni écrasée.
            if (await ecriture.TrouverParEmpreinteAsync(offre.Source, empreinte, annulation) is { } deja)
            {
                return new ResultatDeLImport.DejaEnregistree(deja);
            }

            var entreprise = lus.Value.Nom is null ? null : await ecriture.TrouverLEntrepriseAsync(CleDEntreprise.De(lus.Value.Nom), annulation);

            // Règle 3 : une entreprise qui n'est pas encore enregistrée n'a aucune offre, donc aucun doublon possible.
            if (entreprise is not null && !garderLesDeux)
            {
                var doublons = await ecriture.ListerLesDoublonsPossiblesAsync(entreprise.Id, PosteNormalise.De(lus.Value.Champs.Poste), sauf: null, annulation);
                if (doublons.Count > 0)
                {
                    return new ResultatDeLImport.DoublonPossible(doublons);
                }
            }

            if (entreprise is null && lus.Value.Nom is not null)
            {
                entreprise = Entreprise.Nouvelle(lus.Value.Nom);
                await ecriture.AjouterAsync(entreprise, annulation);
            }

            var importee = Offre.Importer(offre.Source, empreinte, entreprise?.Id, lus.Value.Champs, texte, maintenant);
            await ecriture.AjouterAsync(importee, annulation);
            await ecriture.ValiderAsync(annulation);
            id = importee.Id;
        }

        return new ResultatDeLImport.Enregistree(await depot.LireAsync(id, annulation)
            ?? throw new InvalidOperationException("L'offre enregistrée ne se relit pas."));
    }

    /// <summary>
    /// Lit l'entreprise, facultative, et les champs d'une offre ; ajoute les refus de chaque champ, sans reprendre le
    /// texte saisi. Rend rien si un champ est refusé. La correction d'une offre les lit de même.
    /// </summary>
    internal static (string? Nom, ChampsDUneOffre Champs)? LireLesChamps(
        string? entreprise,
        string? poste,
        string? lieu,
        string? publieeLe,
        string? adresse,
        DateOnly aujourdhui,
        Dictionary<string, string[]> refus)
    {
        string? nom = null;
        if (!string.IsNullOrWhiteSpace(entreprise) && !Entreprise.EssayerDeLireLeNom(entreprise, out nom, out var refusDeLEntreprise))
        {
            refus[SaisieDUneOffre.Champs.Entreprise] = [refusDeLEntreprise];
        }

        if (!ChampsDUneOffre.EssayerDeLire(poste, lieu, publieeLe, adresse, aujourdhui, out var champs, out var refusDesChamps))
        {
            Ajouter(refus, SaisieDUneOffre.Champs.Poste, refusDesChamps.Poste);
            Ajouter(refus, SaisieDUneOffre.Champs.Lieu, refusDesChamps.Lieu);
            Ajouter(refus, SaisieDUneOffre.Champs.PublieeLe, refusDesChamps.PublieeLe);
            Ajouter(refus, SaisieDUneOffre.Champs.Adresse, refusDesChamps.Adresse);
            return null;
        }

        return refus.Count > 0 ? null : (nom, champs);
    }

    /// <summary>Aujourd'hui, en date locale du poste : la date de publication ne passe pas après.</summary>
    internal static DateOnly Aujourdhui(TimeProvider horloge) =>
        DelaiDeRelance.JourLocal(horloge.GetUtcNow().UtcDateTime, horloge.LocalTimeZone);

    // Le texte est requis pour une offre collée ; une ligne de CSV pourra s'en passer (#40).
    private static bool TexteRequis(SourceDOffre source) => source == SourceDOffre.TexteColle;

    private static void Ajouter(Dictionary<string, string[]> refus, string champ, string? motif)
    {
        if (motif is not null)
        {
            refus[champ] = [motif];
        }
    }
}

/// <summary>Ce que rend l'import d'une offre.</summary>
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
