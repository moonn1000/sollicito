namespace Sollicito.Application.Sauvegardes;

/// <summary>
/// Ce que Sollicito copie, et ce qu'il garde (#26, ADR-0008) : une copie par jour d'utilisation, et les sept dernières
/// copies du jour de l'installation. La rotation ne touche qu'aux copies du jour de cette installation : ni aux autres
/// fichiers du dossier, ni aux copies d'un autre poste, ni aux copies d'avant une mise à jour, qui sont toutes gardées.
/// </summary>
public static class PlanDesCopies
{
    /// <summary>Le nombre de copies du jour que garde le dossier des sauvegardes.</summary>
    public const int CopiesDuJourGardees = 7;

    /// <summary>Le jour local d'un instant, dans le fuseau du poste : celui qui date la copie du jour.</summary>
    public static DateOnly JourLocal(DateTimeOffset instant, TimeZoneInfo fuseau) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, fuseau).DateTime);

    /// <summary>Vrai si le dossier, dont voici les noms de fichiers, n'a pas encore la copie de ce jour pour l'installation.</summary>
    public static bool ACopier(IEnumerable<string> noms, string installation, DateOnly jour)
    {
        ArgumentNullException.ThrowIfNull(noms);

        var nomDuJour = NomsDesCopies.DuJour(installation, jour);
        return !noms.Contains(nomDuJour, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Les copies du jour de l'installation à effacer une fois une nouvelle copie vérifiée : toutes, sauf les sept plus
    /// récentes, par le jour de leur nom. La copie qui vient d'être écrite, <paramref name="aGarder"/>, est toujours
    /// gardée, et compte parmi les sept : des copies datées d'après elle, par une horloge qui avançait, ne la chassent pas.
    /// </summary>
    public static IReadOnlyList<string> AEffacer(IEnumerable<string> noms, string installation, string? aGarder = null)
    {
        ArgumentNullException.ThrowIfNull(noms);

        var copies = CopiesDuJour(noms, installation);
        var gardees = copies.Where(copie => copie.Nom == aGarder)
            .Concat(copies.Where(copie => copie.Nom != aGarder))
            .Take(CopiesDuJourGardees)
            .Select(copie => copie.Nom)
            .ToHashSet(StringComparer.Ordinal);
        return [.. copies.Where(copie => !gardees.Contains(copie.Nom)).Select(copie => copie.Nom)];
    }

    /// <summary>Les copies du jour de l'installation, de la plus récente à la plus ancienne.</summary>
    public static IReadOnlyList<CopieNommee.DuJour> CopiesDuJour(IEnumerable<string> noms, string installation)
    {
        ArgumentNullException.ThrowIfNull(noms);

        return [.. noms
            .Select(NomsDesCopies.Lire)
            .OfType<CopieNommee.DuJour>()
            .Where(copie => string.Equals(copie.Installation, installation, StringComparison.Ordinal))
            .OrderByDescending(copie => copie.Jour)];
    }

    /// <summary>Les copies d'avant une mise à jour, dans l'ordre des migrations, de la plus récente à la plus ancienne.</summary>
    public static IReadOnlyList<CopieNommee.AvantMiseAJour> CopiesDAvantUneMiseAJour(IEnumerable<string> noms)
    {
        ArgumentNullException.ThrowIfNull(noms);

        return [.. noms
            .Select(NomsDesCopies.Lire)
            .OfType<CopieNommee.AvantMiseAJour>()
            .OrderByDescending(copie => copie.Migration, StringComparer.Ordinal)];
    }
}
