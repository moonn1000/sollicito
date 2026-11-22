namespace Sollicito.Application.Offres;

/// <summary>
/// Une offre collée telle que l'interface l'envoie (#36), champ par champ, sans rien de lu ni de converti : le texte
/// collé, et les champs que l'utilisateur remplit lui-même. Ni source, ni empreinte, ni date d'ajout : l'API les donne.
/// </summary>
/// <param name="Texte">Le texte de l'offre, tel que collé.</param>
/// <param name="Entreprise">Le nom de l'entreprise, facultatif.</param>
/// <param name="Poste">Le poste, requis.</param>
/// <param name="Lieu">Le lieu, facultatif.</param>
/// <param name="PublieeLe">La date de publication, facultative : <c>AAAA-MM-JJ</c>.</param>
/// <param name="Adresse">L'adresse de l'annonce, facultative.</param>
/// <param name="GarderLesDeux">L'accord de l'utilisateur pour enregistrer l'offre malgré un doublon possible (règle 3).</param>
public sealed record SaisieDUneOffre(
    string? Texte,
    string? Entreprise,
    string? Poste,
    string? Lieu,
    string? PublieeLe,
    string? Adresse,
    bool GarderLesDeux = false)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Texte = "texte";
        public const string Entreprise = "entreprise";
        public const string Poste = "poste";
        public const string Lieu = "lieu";
        public const string PublieeLe = "publieeLe";
        public const string Adresse = "adresse";
    }
}

/// <summary>
/// La correction d'une offre telle que l'interface l'envoie (ADR-0013) : ses champs, jamais son texte, qui ne change plus
/// après l'import. Un corps qui porte un texte est illisible.
/// </summary>
/// <param name="Entreprise">Le nom de l'entreprise, facultatif.</param>
/// <param name="Poste">Le poste, requis.</param>
/// <param name="Lieu">Le lieu, facultatif.</param>
/// <param name="PublieeLe">La date de publication, facultative : <c>AAAA-MM-JJ</c>.</param>
/// <param name="Adresse">L'adresse de l'annonce, facultative.</param>
/// <param name="GarderLesDeux">L'accord de l'utilisateur pour corriger l'offre malgré un doublon possible (règle 3).</param>
public sealed record CorrectionDUneOffre(
    string? Entreprise,
    string? Poste,
    string? Lieu,
    string? PublieeLe,
    string? Adresse,
    bool GarderLesDeux = false);
