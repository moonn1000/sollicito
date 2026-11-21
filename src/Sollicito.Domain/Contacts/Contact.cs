using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Contacts;

/// <summary>
/// Une personne d'une entreprise, à qui adresser une relance (modèle de domaine, « Contact »). Le contact de la V1 est
/// minimal : un nom, une adresse électronique et son entreprise, tous trois requis. La fonction et le téléphone (#30)
/// attendent la V2. Il vit à part de la candidature, qui ne le désigne que par sa relance, comme destinataire.
/// </summary>
public sealed class Contact
{
    /// <summary>Les mots qui désignent le nom dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuNom = new("Indiquez le nom du contact.", "Le nom du contact");

    // Pour Entity Framework Core, qui lit un contact enregistré.
    private Contact(int entrepriseId, string nom, string email)
    {
        EntrepriseId = entrepriseId;
        Nom = nom;
        Email = email;
    }

    /// <summary>L'identifiant technique, donné par la base.</summary>
    public int Id { get; private set; }

    /// <summary>L'identifiant de son entreprise.</summary>
    public int EntrepriseId { get; private set; }

    /// <summary>Son nom.</summary>
    public string Nom { get; private set; }

    /// <summary>Son adresse électronique (<see cref="AdresseElectronique"/>).</summary>
    public string Email { get; private set; }

    /// <summary>Lit un nom saisi (<see cref="TexteDUneLigne"/>).</summary>
    public static bool EssayerDeLireLeNom(string? saisie, [NotNullWhen(true)] out string? nom, [NotNullWhen(false)] out string? erreur) =>
        TexteDUneLigne.EssayerDeLire(saisie, LibelleDuNom, out nom, out erreur);

    /// <summary>
    /// Un contact de l'entreprise donnée, déjà enregistrée. Le nom doit être admis par <see cref="EssayerDeLireLeNom"/>,
    /// et l'adresse par <see cref="AdresseElectronique.EssayerDeLire"/>.
    /// </summary>
    public static Contact Nouveau(int entrepriseId, string? nom, string? email)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entrepriseId);
        var (nomLu, emailLu) = Lire(nom, email);
        return new Contact(entrepriseId, nomLu, emailLu);
    }

    /// <summary>
    /// Corrige le nom et l'adresse, aux règles de <see cref="Nouveau"/>. Les relances qui le désignent l'affichent
    /// désormais ainsi, closes comprises.
    /// </summary>
    public void Modifier(string? nom, string? email) => (Nom, Email) = Lire(nom, email);

    private static (string Nom, string Email) Lire(string? nom, string? email)
    {
        if (!EssayerDeLireLeNom(nom, out var nomLu, out var refusDuNom))
        {
            throw new ArgumentException(refusDuNom, nameof(nom));
        }

        if (!AdresseElectronique.EssayerDeLire(email, out var emailLu, out var refusDeLAdresse))
        {
            throw new ArgumentException(refusDeLAdresse, nameof(email));
        }

        return (nomLu, emailLu);
    }
}
