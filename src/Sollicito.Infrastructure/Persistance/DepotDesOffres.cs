using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sollicito.Application.Candidatures;
using Sollicito.Application.Offres;
using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>Les offres et les entreprises qu'elles désignent, dans la base SQLite (#36).</summary>
internal sealed class DepotDesOffres(BaseDeSollicito baseDeSollicito) : IDepotDesOffres
{
    public async Task<IEcritureDesOffres> CommencerUneEcritureAsync(CancellationToken annulation)
    {
        // Sous SQLite, une transaction commence par BEGIN IMMEDIATE : elle prend aussitôt le verrou d'écriture de la
        // base, et une autre écriture attend sa fin, même venue d'un autre processus.
        var transaction = await baseDeSollicito.Database.BeginTransactionAsync(annulation);
        return new Ecriture(baseDeSollicito, transaction);
    }

    public async Task<FicheDUneOffre?> LireAsync(int id, CancellationToken annulation)
    {
        var lue = await (
            from offre in baseDeSollicito.Offres.AsNoTracking()
            where offre.Id == id
            join entreprise in baseDeSollicito.Entreprises.AsNoTracking() on offre.EntrepriseId equals (int?)entreprise.Id into entreprises
            from entreprise in entreprises.DefaultIfEmpty()
            select new
            {
                offre.Id,
                offre.Source,
                Entreprise = entreprise == null ? null : new EntrepriseConnue(entreprise.Id, entreprise.Nom),
                offre.Poste,
                offre.PosteNormalise,
                offre.Lieu,
                offre.PublieeLe,
                offre.Adresse,
                offre.Texte,
                offre.AjouteeLe,
            })
            .SingleOrDefaultAsync(annulation);
        if (lue is null)
        {
            return null;
        }

        IReadOnlyList<OffreSignalee> doublons = lue.Entreprise is null
            ? []
            : await DoublonsPossiblesAsync(baseDeSollicito, lue.Entreprise.Id, lue.PosteNormalise, lue.Id, annulation);
        return new FicheDUneOffre(
            lue.Id,
            lue.Source,
            lue.Entreprise,
            lue.Poste,
            lue.Lieu,
            lue.PublieeLe,
            lue.Adresse,
            lue.Texte,
            lue.AjouteeLe,
            doublons);
    }

    // Règle 3 : même entreprise, même poste normalisé, quelle que soit la source ; les plus anciennes d'abord.
    private static async Task<IReadOnlyList<OffreSignalee>> DoublonsPossiblesAsync(
        BaseDeSollicito baseDeSollicito,
        int entrepriseId,
        string posteNormalise,
        int? sauf,
        CancellationToken annulation) =>
        await (
            from offre in baseDeSollicito.Offres.AsNoTracking()
            where offre.EntrepriseId == entrepriseId && offre.PosteNormalise == posteNormalise && (sauf == null || offre.Id != sauf)
            join entreprise in baseDeSollicito.Entreprises.AsNoTracking() on offre.EntrepriseId equals (int?)entreprise.Id
            orderby offre.AjouteeLe, offre.Id
            select new OffreSignalee(offre.Id, offre.Poste, new EntrepriseConnue(entreprise.Id, entreprise.Nom), offre.Lieu, offre.AjouteeLe))
            .ToListAsync(annulation);

    private sealed class Ecriture(BaseDeSollicito baseDeSollicito, IDbContextTransaction transaction) : IEcritureDesOffres
    {
        public Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation) =>
            baseDeSollicito.Entreprises.SingleOrDefaultAsync(entreprise => entreprise.Cle == cle, annulation);

        public Task<OffreDejaEnregistree?> TrouverParEmpreinteAsync(SourceDOffre source, string empreinte, CancellationToken annulation) =>
            baseDeSollicito.Offres.AsNoTracking()
                .Where(offre => offre.Source == source && offre.Empreinte == empreinte)
                .Select(offre => new OffreDejaEnregistree(offre.Id, offre.AjouteeLe))
                .SingleOrDefaultAsync(annulation);

        public Task<IReadOnlyList<OffreSignalee>> ListerLesDoublonsPossiblesAsync(int entrepriseId, string posteNormalise, int? sauf, CancellationToken annulation) =>
            DoublonsPossiblesAsync(baseDeSollicito, entrepriseId, posteNormalise, sauf, annulation);

        public Task<Offre?> TrouverLOffreAsync(int id, CancellationToken annulation) =>
            baseDeSollicito.Offres.SingleOrDefaultAsync(offre => offre.Id == id, annulation);

        public async Task AjouterAsync(Entreprise entreprise, CancellationToken annulation)
        {
            baseDeSollicito.Entreprises.Add(entreprise);
            await baseDeSollicito.SaveChangesAsync(annulation);
        }

        public async Task AjouterAsync(Offre offre, CancellationToken annulation)
        {
            baseDeSollicito.Offres.Add(offre);
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
