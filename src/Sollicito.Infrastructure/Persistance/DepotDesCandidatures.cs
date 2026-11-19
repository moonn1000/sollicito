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

    public async Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation) =>
        await baseDeSollicito.Entreprises.AsNoTracking()
            .OrderBy(entreprise => entreprise.Cle)
            .ThenBy(entreprise => entreprise.Id)
            .Select(entreprise => new EntrepriseConnue(entreprise.Id, entreprise.Nom))
            .ToListAsync(annulation);

    private sealed class Ecriture(BaseDeSollicito baseDeSollicito, IDbContextTransaction transaction) : IEcritureDesCandidatures
    {
        public Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation) =>
            baseDeSollicito.Entreprises.SingleOrDefaultAsync(entreprise => entreprise.Cle == cle, annulation);

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

        public Task ValiderAsync(CancellationToken annulation) => transaction.CommitAsync(annulation);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
