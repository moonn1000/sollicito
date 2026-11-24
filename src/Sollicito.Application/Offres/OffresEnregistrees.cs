using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Offres;

/// <summary>
/// Les offres enregistrées (#36, ADR-0013) : lire la fiche d'une offre, et corriger ses champs, jamais son texte. La
/// correction reconnaît l'entreprise par sa clé, sans la renommer, et refait la comparaison de la règle 3 quand
/// l'entreprise ou le poste normalisé change, dans la même écriture exclusive que la correction.
/// </summary>
public sealed class OffresEnregistrees(IDepotDesOffres depot, TimeProvider horloge)
{
    /// <summary>La fiche de l'offre, doublons possibles et candidature compris ; rien si elle n'existe pas.</summary>
    public Task<FicheDUneOffre?> LireAsync(int id, CancellationToken annulation) => depot.LireAsync(id, annulation);

    /// <summary>Toutes les offres, sans leur texte, les plus récemment ajoutées d'abord (#38).</summary>
    public Task<IReadOnlyList<OffreListee>> ListerAsync(CancellationToken annulation) => depot.ListerAsync(annulation);

    /// <summary>Corrige les champs de l'offre, ou dit pourquoi elle ne l'est pas, sans rien changer.</summary>
    public async Task<ResultatDeLaCorrection> CorrigerAsync(int id, CorrectionDUneOffre correction, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(correction);

        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var lus = LectureDUneOffre.LireLesChamps(
            correction.Entreprise,
            correction.Poste,
            correction.Lieu,
            correction.PublieeLe,
            correction.Adresse,
            ImportDesOffres.Aujourdhui(horloge),
            refus);
        if (lus is null || refus.Count > 0)
        {
            return new ResultatDeLaCorrection.Refusee(refus);
        }

        await using (var ecriture = await depot.CommencerUneEcritureAsync(annulation))
        {
            var offre = await ecriture.TrouverLOffreAsync(id, annulation);
            if (offre is null)
            {
                return new ResultatDeLaCorrection.Introuvable();
            }

            var entreprise = lus.Value.Nom is null ? null : await ecriture.TrouverLEntrepriseAsync(CleDEntreprise.De(lus.Value.Nom), annulation);
            if (entreprise is not null && !correction.GarderLesDeux && offre.ChangeraitLeDoublonnage(entreprise.Id, lus.Value.Champs))
            {
                var doublons = await ecriture.ListerLesDoublonsPossiblesAsync(entreprise.Id, PosteNormalise.De(lus.Value.Champs.Poste), sauf: offre.Id, annulation);
                if (doublons.Count > 0)
                {
                    return new ResultatDeLaCorrection.DoublonPossible(doublons);
                }
            }

            if (entreprise is null && lus.Value.Nom is not null)
            {
                entreprise = Entreprise.Nouvelle(lus.Value.Nom);
                await ecriture.AjouterAsync(entreprise, annulation);
            }

            offre.Corriger(entreprise?.Id, lus.Value.Champs);
            await ecriture.ValiderAsync(annulation);
        }

        return new ResultatDeLaCorrection.Corrigee(await depot.LireAsync(id, annulation)
            ?? throw new InvalidOperationException("L'offre corrigée ne se relit pas."));
    }
}

/// <summary>Ce que rend la correction d'une offre.</summary>
public abstract record ResultatDeLaCorrection
{
    private ResultatDeLaCorrection()
    {
    }

    /// <summary>L'offre est corrigée : sa fiche, doublons possibles compris.</summary>
    public sealed record Corrigee(FicheDUneOffre Fiche) : ResultatDeLaCorrection;

    /// <summary>Un champ est refusé : pour chaque champ refusé, ses messages. Rien n'a changé.</summary>
    public sealed record Refusee(IReadOnlyDictionary<string, string[]> Refus) : ResultatDeLaCorrection;

    /// <summary>Des offres de même entreprise et de même poste sont enregistrées (règle 3) : rien n'a changé sans l'accord.</summary>
    public sealed record DoublonPossible(IReadOnlyList<OffreSignalee> Doublons) : ResultatDeLaCorrection;

    /// <summary>L'offre n'existe pas.</summary>
    public sealed record Introuvable : ResultatDeLaCorrection;
}
