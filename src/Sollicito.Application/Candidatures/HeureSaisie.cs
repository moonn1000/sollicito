using System.Globalization;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// Une date et une heure telles que les envoie un champ <c>datetime-local</c> : <c>2026-03-27T23:30</c>, les secondes
/// en option, sans décalage ni <c>Z</c> (modèle de domaine, « Dates et heures »). Elles se lisent ensuite dans le fuseau
/// du poste, par le domaine.
/// </summary>
internal static class HeureSaisie
{
    // Les formes que produit un champ datetime-local, à la minute ou à la seconde, sans décalage.
    private static readonly string[] Formats = ["yyyy'-'MM'-'dd'T'HH':'mm", "yyyy'-'MM'-'dd'T'HH':'mm':'ss"];

    /// <summary>Rend vrai et l'heure saisie, sans fuseau, si le texte a l'une des formes admises.</summary>
    public static bool EssayerDeLire(string saisie, out DateTime heure) =>
        DateTime.TryParseExact(saisie, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out heure);
}
