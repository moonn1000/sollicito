using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Primitives;

namespace Sollicito.Api.Securite;

/// <summary>
/// Le jeton de l'API locale (ADR-0001, D9) : 256 bits tirés d'un générateur cryptographique à chaque
/// lancement. Il ne vit qu'en mémoire : jamais dans une route, un fichier, la ligne de commande ou un
/// journal. Une requête ne le présente que dans l'en-tête <see cref="EnTete"/> ; ailleurs, il ne vaut rien.
/// </summary>
public sealed class JetonDeLancement
{
    /// <summary>Seul en-tête où le jeton est lu : un jeton placé dans un cookie ou dans l'adresse est ignoré.</summary>
    public const string EnTete = "Sollicito-Jeton";

    /// <summary>Taille du jeton : 32 octets, soit 256 bits.</summary>
    public const int TailleEnOctets = 32;

    private readonly string _valeur = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TailleEnOctets));

    /// <summary>
    /// La valeur du jeton, que seule la fenêtre hôte remettra à l'interface, par son canal interne (#13).
    /// Une méthode et non une propriété, pour qu'aucune sérialisation ne l'emporte par mégarde.
    /// </summary>
    public string Valeur() => _valeur;

    /// <summary>
    /// Vrai si les en-têtes portent le jeton, une seule fois, dans son en-tête dédié. Le contenu se compare
    /// en temps constant ; seule la longueur, qui n'est pas secrète, peut écourter la comparaison.
    /// </summary>
    public bool EstPresentDans(IHeaderDictionary enTetes)
    {
        ArgumentNullException.ThrowIfNull(enTetes);
        return Accepte(enTetes[EnTete]);
    }

    /// <summary>Ne montre jamais la valeur, pour qu'un journal ou un message d'erreur ne puisse pas l'écrire.</summary>
    public override string ToString() => "jeton de lancement (valeur masquée)";

    private bool Accepte(StringValues valeurs)
    {
        if (valeurs.Count != 1 || valeurs[0] is not { } presente)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            MemoryMarshal.AsBytes(presente.AsSpan()),
            MemoryMarshal.AsBytes(_valeur.AsSpan()));
    }
}
