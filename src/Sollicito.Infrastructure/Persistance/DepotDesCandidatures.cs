using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sollicito.Application.Candidatures;
using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Entreprises;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>Les candidatures et les entreprises, dans la base SQLite.</summary>
internal sealed class DepotDesCandidatures(BaseDeSollicito baseDeSollicito) : IDepotDesCandidatures
{
    public async Task<IEcritureDesCandidatures> CommencerUneEcritureAsync(CancellationToken annulation)
    {
        // Sous SQLite, une transaction commence par BEGIN IMMEDIATE : elle prend aussitôt le verrou d'écriture de la
        // base, et une autre écriture attend sa fin, même venue d'un autre processus.
        var transaction = await baseDeSollicito.Database.BeginTransactionAsync(annulation);
        return new Ecriture(baseDeSollicito, transaction);
    }

    public async Task<IReadOnlyList<CandidatureListee>> ListerAsync(CancellationToken annulation) =>
        await (
            from candidature in baseDeSollicito.Candidatures.AsNoTracking()
            join entreprise in baseDeSollicito.Entreprises.AsNoTracking() on candidature.EntrepriseId equals entreprise.Id
            orderby candidature.EnvoyeeLe descending, candidature.Id descending
            select new CandidatureListee(
                candidature.Id,
                new EntrepriseConnue(entreprise.Id, entreprise.Nom),
                candidature.Poste,
                candidature.EnvoyeeLe,
                candidature.Statut))
            .ToListAsync(annulation);

    // Seules celles qu'une candidature porte encore : une entreprise quittée ou dont la dernière candidature est
    // supprimée reste en base, mais n'est plus proposée (#25).
    public async Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation) =>
        await baseDeSollicito.Entreprises.AsNoTracking()
            .Where(entreprise => baseDeSollicito.Candidatures.Any(candidature => candidature.EntrepriseId == entreprise.Id))
            .OrderBy(entreprise => entreprise.Cle)
            .ThenBy(entreprise => entreprise.Id)
            .Select(entreprise => new EntrepriseConnue(entreprise.Id, entreprise.Nom))
            .ToListAsync(annulation);

    public Task<CandidatureSuivie?> TrouverAsync(int id, CancellationToken annulation) => ChargerAsync(baseDeSollicito, id, annulation);

    // La candidature et tous ses événements, suivis par le contexte : la correction relue retrouve ainsi le changement
    // qu'elle désigne, et ce qui change est gardé à la validation. Les événements arrivent dans l'ordre de leur
    // enregistrement, celui de leur identifiant, sur lequel le domaine compte pour trouver le dernier changement.
    private static async Task<CandidatureSuivie?> ChargerAsync(BaseDeSollicito baseDeSollicito, int id, CancellationToken annulation)
    {
        var candidature = await baseDeSollicito.Candidatures
            .Include(c => c.Evenements.OrderBy(evenement => evenement.Id))
            .SingleOrDefaultAsync(c => c.Id == id, annulation);
        if (candidature is null)
        {
            return null;
        }

        var entreprise = await baseDeSollicito.Entreprises.AsNoTracking()
            .Where(e => e.Id == candidature.EntrepriseId)
            .Select(e => new EntrepriseConnue(e.Id, e.Nom))
            .SingleAsync(annulation);
        return new CandidatureSuivie(candidature, entreprise);
    }

    private sealed class Ecriture(BaseDeSollicito baseDeSollicito, IDbContextTransaction transaction) : IEcritureDesCandidatures
    {
        public Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation) =>
            baseDeSollicito.Entreprises.SingleOrDefaultAsync(entreprise => entreprise.Cle == cle, annulation);

        public Task<CandidatureSuivie?> TrouverLaCandidatureAsync(int id, CancellationToken annulation) =>
            ChargerAsync(baseDeSollicito, id, annulation);

        // Une seule instruction, dans la transaction de l'écriture : la base emporte les événements en cascade, et ne
        // vérifie l'auto-référence des corrections (NO ACTION) qu'en fin d'instruction, quand tous sont partis.
        public async Task<bool> SupprimerLaCandidatureAsync(int id, CancellationToken annulation) =>
            await baseDeSollicito.Candidatures.Where(candidature => candidature.Id == id).ExecuteDeleteAsync(annulation) == 1;

        public async Task AjouterAsync(Entreprise entreprise, CancellationToken annulation)
        {
            baseDeSollicito.Entreprises.Add(entreprise);
            await baseDeSollicito.SaveChangesAsync(annulation);
        }

        public async Task AjouterAsync(Candidature candidature, CancellationToken annulation)
        {
            baseDeSollicito.Candidatures.Add(candidature);
            await baseDeSollicito.SaveChangesAsync(annulation);
        }

        public async Task ValiderAsync(CancellationToken annulation)
        {
            await baseDeSollicito.SaveChangesAsync(annulation);
            await transaction.CommitAsync(annulation);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
