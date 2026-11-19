namespace Sollicito.Domain.Temps;

/// <summary>
/// Une heure telle que l'utilisateur la saisit, dans le fuseau de Windows sur son poste (ADR-0005, « Garder les dates
/// et les heures » ; modèle de domaine, « Dates et heures »). La conversion en UTC se fait ici seulement :
/// <list type="bullet">
/// <item>une heure qui n'existe pas, dans le trou du passage à l'heure d'été, est refusée ;</item>
/// <item>une heure qui existe deux fois, au retour à l'heure d'hiver, se lit à l'heure d'hiver.</item>
/// </list>
/// </summary>
public static class HeureLocale
{
    /// <summary>
    /// Convertit une heure locale en instant UTC, de genre <see cref="DateTimeKind.Utc"/>. Rend faux pour une heure qui
    /// n'existe pas dans le fuseau.
    /// </summary>
    /// <param name="heureLocale">L'heure saisie, sans fuseau : de genre <see cref="DateTimeKind.Unspecified"/>.</param>
    /// <param name="fuseau">Le fuseau du poste.</param>
    /// <param name="instant">L'instant UTC, si l'heure existe.</param>
    public static bool EssayerVersUtc(DateTime heureLocale, TimeZoneInfo fuseau, out DateTime instant)
    {
        ArgumentNullException.ThrowIfNull(fuseau);
        if (heureLocale.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentException("Une heure saisie n'a pas de fuseau : son genre est Unspecified.", nameof(heureLocale));
        }

        if (fuseau.IsInvalidTime(heureLocale))
        {
            instant = default;
            return false;
        }

        // Une heure ambiguë se lit à l'heure d'hiver (heure standard du fuseau), comme le fait ConvertTimeToUtc.
        instant = TimeZoneInfo.ConvertTimeToUtc(heureLocale, fuseau);
        return true;
    }
}
