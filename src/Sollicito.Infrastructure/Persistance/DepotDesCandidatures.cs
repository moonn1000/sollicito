using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sollicito.Application.Candidatures;
using Sollicito.Application.Contacts;
using Sollicito.Application.Relances;
using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Contacts;
using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Relances;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>Les candidatures, leurs relances, les entreprises et leurs contacts, dans la base SQLite.</summary>
internal sealed class DepotDesCandidatures(BaseDeSollicito baseDeSollicito) : IDepotDesCandidatures
{
    // La colonne de la candidature d'une relance : une clé que le domaine ne montre pas.
    private const string ColonneDeLaCandidature = "CandidatureId";

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

    // Seules celles qu'une candidature ou une offre désigne encore (#25, #36, ADR-0013) : une entreprise quittée, ou dont
    // la dernière candidature est supprimée et qu'aucune offre ne désigne, reste en base, mais n'est plus proposée.
    public async Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation) =>
        await baseDeSollicito.Entreprises.AsNoTracking()
            .Where(entreprise =>
                baseDeSollicito.Candidatures.Any(candidature => candidature.EntrepriseId == entreprise.Id)
                || baseDeSollicito.Offres.Any(offre => offre.EntrepriseId == entreprise.Id))
            .OrderBy(entreprise => entreprise.Cle)
            .ThenBy(entreprise => entreprise.Id)
            .Select(entreprise => new EntrepriseConnue(entreprise.Id, entreprise.Nom))
            .ToListAsync(annulation);

    public Task<CandidatureSuivie?> TrouverAsync(int id, CancellationToken annulation) => ChargerAsync(baseDeSollicito, id, annulation);

    public async Task<IReadOnlyList<ContactConnu>?> ListerLesContactsAsync(int entrepriseId, CancellationToken annulation)
    {
        if (!await baseDeSollicito.Entreprises.AnyAsync(entreprise => entreprise.Id == entrepriseId, annulation))
        {
            return null;
        }

        return await baseDeSollicito.Contacts.AsNoTracking()
            .Where(contact => contact.EntrepriseId == entrepriseId)
            .OrderBy(contact => contact.Id)
            .Select(contact => new ContactConnu(contact.Id, contact.Nom, contact.Email))
            .ToListAsync(annulation);
    }

    public async Task<IReadOnlyList<RelanceSignalee>> ListerLesRelancesAFaireAsync(DateOnly auPlusTardLe, CancellationToken annulation) =>
        await (
            from relance in baseDeSollicito.Relances.AsNoTracking()
            where relance.Etat == EtatDeRelance.AFaire && relance.Echeance <= auPlusTardLe
            join candidature in baseDeSollicito.Candidatures.AsNoTracking()
                on EF.Property<int>(relance, ColonneDeLaCandidature) equals candidature.Id
            join entreprise in baseDeSollicito.Entreprises.AsNoTracking() on candidature.EntrepriseId equals entreprise.Id
            join contact in baseDeSollicito.Contacts.AsNoTracking() on relance.DestinataireId equals (int?)contact.Id into contacts
            from destinataire in contacts.DefaultIfEmpty()
            orderby relance.Echeance, relance.Id
            select new RelanceSignalee(
                relance.Id,
                relance.Echeance,
                new CandidatureDeLaRelance(candidature.Id, candidature.Poste, new EntrepriseConnue(entreprise.Id, entreprise.Nom)),
                destinataire == null ? null : new ContactConnu(destinataire.Id, destinataire.Nom, destinataire.Email)))
            .ToListAsync(annulation);

    public async Task<RelancePourLeBrouillon?> LireLaRelancePourLeBrouillonAsync(int relanceId, CancellationToken annulation) =>
        await (
            from relance in baseDeSollicito.Relances.AsNoTracking()
            where relance.Id == relanceId && relance.Etat == EtatDeRelance.AFaire
            join candidature in baseDeSollicito.Candidatures.AsNoTracking()
                on EF.Property<int>(relance, ColonneDeLaCandidature) equals candidature.Id
            join contact in baseDeSollicito.Contacts.AsNoTracking() on relance.DestinataireId equals (int?)contact.Id into contacts
            from destinataire in contacts.DefaultIfEmpty()
            select new RelancePourLeBrouillon(
                relance.Id,
                candidature.Poste,
                candidature.EnvoyeeLe,
                destinataire == null ? null : new ContactConnu(destinataire.Id, destinataire.Nom, destinataire.Email)))
            .SingleOrDefaultAsync(annulation);

    // La candidature, tous ses événements et ses relances, suivis par le contexte : la correction relue retrouve ainsi le
    // changement qu'elle désigne, la clôture sa relance, et ce qui change est gardé à la validation. Les événements et
    // les relances arrivent dans l'ordre de leur enregistrement, celui de leur identifiant, sur lequel le domaine compte
    // pour trouver le dernier changement. Deux requêtes, une par collection, plutôt qu'un produit des deux.
    private static async Task<CandidatureSuivie?> ChargerAsync(BaseDeSollicito baseDeSollicito, int id, CancellationToken annulation)
    {
        var candidature = await baseDeSollicito.Candidatures
            .Include(c => c.Evenements.OrderBy(evenement => evenement.Id))
            .Include(c => c.Relances.OrderBy(relance => relance.Id))
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.Id == id, annulation);
        if (candidature is null)
        {
            return null;
        }

        var entreprise = await baseDeSollicito.Entreprises.AsNoTracking()
            .Where(e => e.Id == candidature.EntrepriseId)
            .Select(e => new EntrepriseConnue(e.Id, e.Nom))
            .SingleAsync(annulation);
        List<int> designes = [.. candidature.Relances.Select(relance => relance.DestinataireId).OfType<int>().Distinct()];
        IReadOnlyList<ContactConnu> destinataires = designes.Count == 0
            ? []
            : await baseDeSollicito.Contacts.AsNoTracking()
                .Where(contact => designes.Contains(contact.Id))
                .OrderBy(contact => contact.Id)
                .Select(contact => new ContactConnu(contact.Id, contact.Nom, contact.Email))
                .ToListAsync(annulation);
        return new CandidatureSuivie(candidature, entreprise) { Destinataires = destinataires };
    }

    private sealed class Ecriture(BaseDeSollicito baseDeSollicito, IDbContextTransaction transaction) : IEcritureDesCandidatures
    {
        public Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation) =>
            baseDeSollicito.Entreprises.SingleOrDefaultAsync(entreprise => entreprise.Cle == cle, annulation);

        public Task<bool> EntrepriseExisteAsync(int entrepriseId, CancellationToken annulation) =>
            baseDeSollicito.Entreprises.AnyAsync(entreprise => entreprise.Id == entrepriseId, annulation);

        public Task<int> CompterLesCandidaturesAsync(int entrepriseId, CancellationToken annulation) =>
            baseDeSollicito.Candidatures.CountAsync(candidature => candidature.EntrepriseId == entrepriseId, annulation);

        public Task<int> CompterLesOffresAsync(int entrepriseId, CancellationToken annulation) =>
            baseDeSollicito.Offres.CountAsync(offre => offre.EntrepriseId == entrepriseId, annulation);

        // L'offre, son entreprise et la candidature qui la désigne déjà, lues dans la transaction de l'écriture.
        public Task<OffreAPostuler?> TrouverLOffreAPostulerAsync(int offreId, CancellationToken annulation) =>
            (from offre in baseDeSollicito.Offres.AsNoTracking()
             where offre.Id == offreId
             join entreprise in baseDeSollicito.Entreprises.AsNoTracking() on offre.EntrepriseId equals (int?)entreprise.Id into entreprises
             from entreprise in entreprises.DefaultIfEmpty()
             select new OffreAPostuler(
                 offre.Id,
                 entreprise == null ? null : new EntrepriseConnue(entreprise.Id, entreprise.Nom),
                 offre.Poste,
                 baseDeSollicito.Candidatures.Where(candidature => candidature.OffreId == offre.Id).Select(candidature => (int?)candidature.Id).FirstOrDefault()))
            .SingleOrDefaultAsync(annulation);

        public Task<CandidatureSuivie?> TrouverLaCandidatureAsync(int id, CancellationToken annulation) =>
            ChargerAsync(baseDeSollicito, id, annulation);

        // Une seule instruction, dans la transaction de l'écriture : la base emporte les événements et les relances en
        // cascade, et ne vérifie les références des corrections et des clôtures (NO ACTION) qu'en fin d'instruction,
        // quand tous sont partis.
        public async Task<bool> SupprimerLaCandidatureAsync(int id, CancellationToken annulation) =>
            await baseDeSollicito.Candidatures.Where(candidature => candidature.Id == id).ExecuteDeleteAsync(annulation) == 1;

        public Task<Contact?> TrouverLeContactAsync(int id, CancellationToken annulation) =>
            baseDeSollicito.Contacts.SingleOrDefaultAsync(contact => contact.Id == id, annulation);

        // La base retire elle-même le contact de ses relances (ON DELETE SET NULL).
        public async Task<bool> SupprimerLeContactAsync(int id, CancellationToken annulation) =>
            await baseDeSollicito.Contacts.Where(contact => contact.Id == id).ExecuteDeleteAsync(annulation) == 1;

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

        public async Task AjouterAsync(Contact contact, CancellationToken annulation)
        {
            baseDeSollicito.Contacts.Add(contact);
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
