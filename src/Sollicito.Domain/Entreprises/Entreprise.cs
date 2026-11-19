using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Entreprises;

/// <summary>
/// Une organisation à qui l'on postule (modèle de domaine, « Reconnaître une entreprise »). Elle se reconnaît à sa
/// clé, tirée de son nom : deux noms de même clé désignent la même entreprise, et le nom affiché reste celui de la
/// première saisie.
/// </summary>
public sealed class Entreprise
{
    /// <summary>Les mots qui désignent le nom dans les messages de refus.</summary>
    public static readonly LibelleDuChamp LibelleDuNom = new("Indiquez l'entreprise.", "Le nom de l'entreprise");

    // Pour Entity Framework Core, qui lit une entreprise enregistrée.
    private Entreprise(string nom, string cle)
    {
        Nom = nom;
        Cle = cle;
    }

    /// <summary>L'identifiant technique, donné par la base.</summary>
    public int Id { get; private set; }

    /// <summary>Le nom affiché : celui de la première saisie.</summary>
    public string Nom { get; private set; }

    /// <summary>La clé tirée du nom (<see cref="CleDEntreprise"/>), unique.</summary>
    public string Cle { get; private set; }

    /// <summary>
    /// Lit un nom d'entreprise saisi (<see cref="TexteDUneLigne"/>). Un nom sans lettre ni chiffre, dont la clé serait
    /// vide, est refusé.
    /// </summary>
    public static bool EssayerDeLireLeNom(string? saisie, [NotNullWhen(true)] out string? nom, [NotNullWhen(false)] out string? erreur)
    {
        if (!TexteDUneLigne.EssayerDeLire(saisie, LibelleDuNom, out nom, out erreur))
        {
            return false;
        }

        if (CleDEntreprise.De(nom).Length == 0)
        {
            nom = null;
            erreur = "Le nom de l'entreprise doit contenir au moins une lettre ou un chiffre.";
            return false;
        }

        return true;
    }

    /// <summary>Une nouvelle entreprise, à partir d'un nom saisi : il doit être admis par <see cref="EssayerDeLireLeNom"/>.</summary>
    public static Entreprise Nouvelle(string? saisie)
    {
        if (!EssayerDeLireLeNom(saisie, out var nom, out var erreur))
        {
            throw new ArgumentException(erreur, nameof(saisie));
        }

        return new Entreprise(nom, CleDEntreprise.De(nom));
    }
}
