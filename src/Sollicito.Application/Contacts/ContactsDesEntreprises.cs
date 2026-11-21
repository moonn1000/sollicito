using Sollicito.Application.Candidatures;
using Sollicito.Domain.Contacts;

namespace Sollicito.Application.Contacts;

/// <summary>
/// Les contacts d'une entreprise (#14, absorbe #30) : les lister, en ajouter un, le modifier ou le supprimer. Chaque
/// écriture se fait dans l'écriture exclusive des candidatures. Les refus ne reprennent jamais le texte saisi.
/// </summary>
public sealed class ContactsDesEntreprises(IDepotDesCandidatures depot)
{
    /// <summary>Les contacts de l'entreprise, dans l'ordre de leur ajout ; rien si l'entreprise n'existe pas.</summary>
    public Task<IReadOnlyList<ContactConnu>?> ListerAsync(int entrepriseId, CancellationToken annulation) =>
        depot.ListerLesContactsAsync(entrepriseId, annulation);

    /// <summary>Ajoute un contact à l'entreprise, ou rend les refus de ses champs, sans rien ajouter.</summary>
    public async Task<ResultatDUnContact> AjouterAsync(int entrepriseId, SaisieDUnContact saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (Refus(saisie) is { } refus)
        {
            return new ResultatDUnContact.Refuse(refus);
        }

        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        if (!await ecriture.EntrepriseExisteAsync(entrepriseId, annulation))
        {
            return new ResultatDUnContact.Introuvable();
        }

        var contact = Contact.Nouveau(entrepriseId, saisie.Nom, saisie.Email);
        await ecriture.AjouterAsync(contact, annulation);
        await ecriture.ValiderAsync(annulation);
        return new ResultatDUnContact.Fait(ContactConnu.De(contact));
    }

    /// <summary>
    /// Corrige le nom et l'adresse d'un contact, ou rend les refus de ses champs, sans rien changer. Ses relances
    /// l'affichent désormais ainsi, closes comprises.
    /// </summary>
    public async Task<ResultatDUnContact> ModifierAsync(int id, SaisieDUnContact saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (Refus(saisie) is { } refus)
        {
            return new ResultatDUnContact.Refuse(refus);
        }

        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var contact = await ecriture.TrouverLeContactAsync(id, annulation);
        if (contact is null)
        {
            return new ResultatDUnContact.Introuvable();
        }

        contact.Modifier(saisie.Nom, saisie.Email);
        await ecriture.ValiderAsync(annulation);
        return new ResultatDUnContact.Fait(ContactConnu.De(contact));
    }

    /// <summary>
    /// Supprime un contact, définitivement. Il quitte toutes ses relances, à faire comme closes, qui restent sans
    /// destinataire. Rend faux s'il n'existe pas.
    /// </summary>
    public async Task<bool> SupprimerAsync(int id, CancellationToken annulation)
    {
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        if (!await ecriture.SupprimerLeContactAsync(id, annulation))
        {
            return false;
        }

        await ecriture.ValiderAsync(annulation);
        return true;
    }

    // Les refus, dans l'ordre des champs du formulaire : nom, adresse. Rien si les deux sont admis.
    private static Dictionary<string, string[]>? Refus(SaisieDUnContact saisie)
    {
        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Contact.EssayerDeLireLeNom(saisie.Nom, out _, out var refusDuNom))
        {
            refus[SaisieDUnContact.Champs.Nom] = [refusDuNom];
        }

        if (!AdresseElectronique.EssayerDeLire(saisie.Email, out _, out var refusDeLAdresse))
        {
            refus[SaisieDUnContact.Champs.Email] = [refusDeLAdresse];
        }

        return refus.Count > 0 ? refus : null;
    }
}

/// <summary>Un contact tel que l'écran l'envoie, champ par champ, sans rien de lu ni de converti.</summary>
/// <param name="Nom">Le nom, tel que saisi.</param>
/// <param name="Email">L'adresse électronique, telle que saisie.</param>
public sealed record SaisieDUnContact(string? Nom, string? Email)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Nom = "nom";
        public const string Email = "email";
    }
}

/// <summary>Un contact enregistré, tel que l'écran le montre.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Nom">Son nom.</param>
/// <param name="Email">Son adresse électronique.</param>
public sealed record ContactConnu(int Id, string Nom, string Email)
{
    /// <summary>Le contact, tel que l'écran le montre.</summary>
    public static ContactConnu De(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return new ContactConnu(contact.Id, contact.Nom, contact.Email);
    }
}

/// <summary>Ce que rend l'ajout ou la modification d'un contact.</summary>
public abstract record ResultatDUnContact
{
    private ResultatDUnContact()
    {
    }

    /// <summary>Le contact est enregistré : tel qu'il est désormais.</summary>
    public sealed record Fait(ContactConnu Contact) : ResultatDUnContact;

    /// <summary>L'entreprise, ou le contact, n'existe pas.</summary>
    public sealed record Introuvable : ResultatDUnContact;

    /// <summary>La saisie est refusée : pour chaque champ refusé, ses messages. Rien ne change.</summary>
    public sealed record Refuse(IReadOnlyDictionary<string, string[]> Refus) : ResultatDUnContact;
}
