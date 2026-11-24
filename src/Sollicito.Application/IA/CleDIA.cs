using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sollicito.Application.IA;

/// <summary>
/// La clé d'API d'un fournisseur d'IA en ligne (#43, ADR-0016), en mémoire. Elle ne s'écrit jamais d'elle-même : son
/// texte (<see cref="ToString"/>) ne la montre pas, et elle refuse de passer en JSON, dans un sens comme dans l'autre.
/// Seule <see cref="Valeur"/> la rend, à la garde qui la chiffre et, avec #48, à l'adaptateur qui l'envoie à son
/// fournisseur. Elle ne naît que d'une saisie admise par <see cref="FormeDUneCle"/>.
/// </summary>
[DebuggerDisplay("[clé d'IA masquée]")]
[JsonConverter(typeof(RefusDuJson))]
public sealed class CleDIA
{
    private readonly string _valeur;

    internal CleDIA(string valeur) => _valeur = valeur;

    /// <summary>La clé elle-même, à ne passer qu'à ce qui la chiffre ou l'envoie à son fournisseur.</summary>
    public string Valeur() => _valeur;

    /// <summary>Un texte qui ne montre pas la clé.</summary>
    public override string ToString() => "[clé d'IA masquée]";

    /// <summary>Refuse d'écrire ou de lire une clé en JSON : aucune réponse, aucun journal ne doit la porter.</summary>
    internal sealed class RefusDuJson : JsonConverter<CleDIA>
    {
        public override CleDIA Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException("Une clé d'IA ne se lit jamais en JSON.");

        public override void Write(Utf8JsonWriter writer, CleDIA value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("Une clé d'IA ne s'écrit jamais en JSON.");
    }
}
