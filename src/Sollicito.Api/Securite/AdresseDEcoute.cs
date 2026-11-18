using System.Globalization;
using System.Net;

namespace Sollicito.Api.Securite;

/// <summary>
/// L'adresse où l'API écoute : 127.0.0.1 et le port libre choisi au lancement (ADR-0003, « Montage »).
/// Elle est relevée une fois le serveur démarré, sur l'adresse réellement ouverte. Les contrôles des
/// en-têtes <c>Host</c> et <c>Origin</c> s'en servent ; tant qu'elle n'est pas relevée, ils refusent tout.
/// </summary>
public sealed class AdresseDEcoute
{
    private Releve? _releve;

    /// <summary>Valeur attendue de l'en-tête <c>Host</c> : « 127.0.0.1:port ».</summary>
    public string Hote => Lire().Hote;

    /// <summary>Origine de l'interface, servie à la même adresse que l'API : « http://127.0.0.1:port ».</summary>
    public string Origine => Lire().Origine;

    /// <summary>Vrai si la valeur est exactement l'en-tête <c>Host</c> attendu.</summary>
    public bool EstLHote(string? valeur) =>
        Volatile.Read(ref _releve) is { } releve && string.Equals(valeur, releve.Hote, StringComparison.Ordinal);

    /// <summary>Vrai si la valeur est exactement l'origine de l'interface.</summary>
    public bool EstLOrigine(string? valeur) =>
        Volatile.Read(ref _releve) is { } releve && string.Equals(valeur, releve.Origine, StringComparison.Ordinal);

    /// <summary>
    /// Relève l'adresse d'écoute. Toute autre écoute qu'une seule adresse <c>http</c> sur 127.0.0.1,
    /// même ajoutée par la configuration, est refusée : l'API ne démarre pas.
    /// </summary>
    internal void Relever(IReadOnlyCollection<string> adresses)
    {
        if (adresses.Count != 1
            || !Uri.TryCreate(adresses.Single(), UriKind.Absolute, out var adresse)
            || adresse.Scheme != Uri.UriSchemeHttp
            || !IPAddress.TryParse(adresse.Host, out var ip)
            || !ip.Equals(IPAddress.Loopback)
            || adresse.Port is <= 0 or > IPEndPoint.MaxPort)
        {
            throw new InvalidOperationException(
                "L'API locale n'écoute que sur une seule adresse http, en 127.0.0.1 (ADR-0001, D9). Adresses ouvertes : "
                + string.Join(", ", adresses) + ".");
        }

        var hote = string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{adresse.Port}");
        Volatile.Write(ref _releve, new Releve(hote, "http://" + hote));
    }

    private Releve Lire() =>
        Volatile.Read(ref _releve) ?? throw new InvalidOperationException("L'adresse d'écoute n'est pas encore relevée : le serveur n'a pas démarré.");

    private sealed record Releve(string Hote, string Origine);
}
