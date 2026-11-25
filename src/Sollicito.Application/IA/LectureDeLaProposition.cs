using System.Text.Json;
using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.IA;

/// <summary>
/// La lecture de la réponse d'une extraction (#46, ADR-0018) : le texte de l'IA n'est pas fiable (ADR-0001, D7 et D9).
/// Il doit être un objet JSON seul, aux cinq clés connues, chaque valeur une chaîne ou <c>null</c>, et chaque chaîne admise
/// par la règle de son champ au collage, appelée telle quelle : une proposition passe donc toujours le collage, champ par
/// champ. Une seule faute écarte toute la réponse (<see cref="IssueDeLExtraction.HorsSchema"/>). Deux contrôles de
/// vraisemblance n'écartent que leur champ : une date de publication hors bornes, et une adresse qui ne figure pas telle
/// quelle dans le texte de l'offre. Le motif d'un refus ne reprend jamais la réponse, ni le nom d'une clé inconnue.
/// </summary>
public static class LectureDeLaProposition
{
    /// <summary>Les cinq clés, écrites à l'identique, dans l'ordre de leur lecture.</summary>
    public static readonly IReadOnlyList<string> Cles = ["entreprise", "poste", "lieu", "publieeLe", "adresse"];

    /// <summary>
    /// La lecture stricte : ni commentaire, ni virgule finale, ni propriété répétée, et une profondeur bornée. Un objet ou un
    /// tableau dans une valeur se lit, pour être refusé comme un type inattendu.
    /// </summary>
    internal static readonly JsonDocumentOptions LectureStricte = new()
    {
        MaxDepth = 4,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    /// <summary>
    /// Lit <paramref name="reponse"/>, le texte de l'IA, contre <paramref name="texteDeLOffre"/>, le texte envoyé. Rend
    /// les champs proposés, ou l'issue « hors schéma ». Ne lève jamais d'exception sur une réponse, quelle qu'elle soit.
    /// </summary>
    public static IssueDeLExtraction Lire(string reponse, string texteDeLOffre, DateOnly aujourdhui)
    {
        ArgumentNullException.ThrowIfNull(reponse);
        ArgumentNullException.ThrowIfNull(texteDeLOffre);
        if (!Deballer(reponse, out var json))
        {
            return HorsSchema(MotifHorsSchema.JsonIllisible);
        }

        try
        {
            using var document = JsonDocument.Parse(json, LectureStricte);
            return LireLObjet(document.RootElement, texteDeLOffre, aujourdhui);
        }
        catch (Exception erreur) when (erreur is JsonException or InvalidOperationException)
        {
            // Du JSON mal formé, une propriété répétée, ou un échappement de substitut sans son pareil, dans un nom ou une
            // valeur, que la lecture refuse en levant InvalidOperationException : la réponse n'est pas fiable.
            return HorsSchema(MotifHorsSchema.JsonIllisible);
        }
    }

    /// <summary>
    /// Le JSON de la réponse : sans ses blancs du début et de la fin, et sans le seul bloc de code Markdown qui l'entoure,
    /// s'il y en a un, ouvert par une ligne « ``` » ou « ```json », fermé par une ligne « ``` ». Rien d'autre n'est toléré.
    /// </summary>
    internal static bool Deballer(string reponse, out string json)
    {
        var lu = reponse.Trim();
        if (!lu.StartsWith("```", StringComparison.Ordinal))
        {
            json = lu;
            return true;
        }

        var lignes = lu.Split('\n');
        if (lignes.Length < 3
            || lignes[0].TrimEnd() is not ("```" or "```json")
            || lignes[^1].Trim() != "```")
        {
            json = "";
            return false;
        }

        json = string.Join('\n', lignes[1..^1]).Trim();
        return true;
    }

    // L'objet, ses cinq clés, puis chaque valeur par la règle de son champ, dans l'ordre des clés.
    private static IssueDeLExtraction LireLObjet(JsonElement racine, string texteDeLOffre, DateOnly aujourdhui)
    {
        if (racine.ValueKind != JsonValueKind.Object)
        {
            return HorsSchema(MotifHorsSchema.PasUnObjet);
        }

        var valeurs = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var propriete in racine.EnumerateObject())
        {
            if (!Cles.Contains(propriete.Name, StringComparer.Ordinal))
            {
                return HorsSchema(MotifHorsSchema.CleInconnue);
            }

            switch (propriete.Value.ValueKind)
            {
                case JsonValueKind.Null:
                    valeurs[propriete.Name] = null;
                    break;
                case JsonValueKind.String:
                    valeurs[propriete.Name] = propriete.Value.GetString();
                    break;
                default:
                    return HorsSchema(MotifHorsSchema.TypeInattendu);
            }
        }

        if (valeurs.Count != Cles.Count)
        {
            return HorsSchema(MotifHorsSchema.CleAbsente);
        }

        var ecartes = new List<ChampEcarte>();

        string? entreprise = null;
        if (Renseignee(valeurs["entreprise"]) is { } saisieDeLEntreprise)
        {
            if (!Entreprise.EssayerDeLireLeNom(saisieDeLEntreprise, out entreprise, out _))
            {
                return Refusee(ChampDeLaProposition.Entreprise);
            }
        }

        string? poste = null;
        if (Renseignee(valeurs["poste"]) is { } saisieDuPoste)
        {
            if (!Offre.EssayerDeLireLePoste(saisieDuPoste, out poste, out _))
            {
                return Refusee(ChampDeLaProposition.Poste);
            }
        }

        string? lieu = null;
        if (Renseignee(valeurs["lieu"]) is { } saisieDuLieu)
        {
            if (!Offre.EssayerDeLireLeLieu(saisieDuLieu, out lieu, out _))
            {
                return Refusee(ChampDeLaProposition.Lieu);
            }
        }

        string? publieeLe = null;
        if (Renseignee(valeurs["publieeLe"]) is { } saisieDeLaDate)
        {
            if (DateDePublication.EssayerDeLire(saisieDeLaDate, aujourdhui, out var date, out var refusDeLaDate))
            {
                publieeLe = DateDePublication.FormeCanonique(date!.Value);
            }
            else if (refusDeLaDate is DateDePublication.TropAncienne or DateDePublication.Future)
            {
                // De bonne forme, mais invraisemblable : une date limite de candidature, par exemple.
                ecartes.Add(new ChampEcarte(ChampDeLaProposition.PublieeLe, MotifDeLEcart.DateHorsBornes));
            }
            else
            {
                return Refusee(ChampDeLaProposition.PublieeLe);
            }
        }

        string? adresse = null;
        if (Renseignee(valeurs["adresse"]) is { } saisieDeLAdresse)
        {
            if (!AdresseDUneAnnonce.EssayerDeLire(saisieDeLAdresse, out var lue, out _))
            {
                return Refusee(ChampDeLaProposition.Adresse);
            }

            // L'IA ne propose jamais une adresse que le texte de l'offre ne porte pas : inventée, venue d'ailleurs, ou tronquée.
            if (FigureDansLeTexte(lue!, texteDeLOffre))
            {
                adresse = lue;
            }
            else
            {
                ecartes.Add(new ChampEcarte(ChampDeLaProposition.Adresse, MotifDeLEcart.AdresseAbsenteDuTexte));
            }
        }

        return new IssueDeLExtraction.Proposee(new ChampsProposes(entreprise, poste, lieu, publieeLe, adresse), ecartes);
    }

    /// <summary>
    /// Vrai si <paramref name="adresse"/> figure telle quelle dans le texte, entière : suivie de la fin du texte, d'un blanc,
    /// ou d'une ponctuation qui la ferme. Un début d'adresse (le seul domaine, <c>…/offres/4</c> pour <c>…/offres/42</c>)
    /// ne compte pas.
    /// </summary>
    internal static bool FigureDansLeTexte(string adresse, string texte)
    {
        for (var debut = texte.IndexOf(adresse, StringComparison.Ordinal); debut >= 0; debut = texte.IndexOf(adresse, debut + 1, StringComparison.Ordinal))
        {
            var fin = debut + adresse.Length;
            if (fin == texte.Length || char.IsWhiteSpace(texte[fin]) || ")]}>»\"'.,;:!?".Contains(texte[fin], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Une valeur vide, ou faite de blancs, ne propose rien.
    private static string? Renseignee(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur;

    private static IssueDeLExtraction.HorsSchema HorsSchema(MotifHorsSchema motif) => new(motif, null);

    private static IssueDeLExtraction.HorsSchema Refusee(ChampDeLaProposition champ) => new(MotifHorsSchema.ChampRefuse, champ);
}
