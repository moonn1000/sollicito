namespace Sollicito.Domain.Relances;

/// <summary>
/// Le délai d'une relance, en jours, compté depuis la date locale de l'envoi de la candidature (modèle de domaine,
/// « Relances » et « Dates et heures ») : l'échéance se calcule sur la date, jamais en heures. Un calcul en heures
/// tomberait un jour trop tard quand le passage à l'heure d'été se place entre l'envoi et l'échéance.
/// </summary>
public static class DelaiDeRelance
{
    /// <summary>Le plus court délai admis.</summary>
    public const int Minimum = 1;

    /// <summary>Le plus long délai admis.</summary>
    public const int Maximum = 365;

    /// <summary>Le délai par défaut d'usine, quand le réglage n'en donne pas d'autre.</summary>
    public const int ParDefaut = 7;

    /// <summary>Le message d'un délai hors des bornes.</summary>
    public const string HorsBornes = "Le délai d'une relance compte de 1 à 365 jours.";

    /// <summary>Vrai pour un délai admis, de <see cref="Minimum"/> à <see cref="Maximum"/> jours.</summary>
    public static bool EstAdmis(int jours) => jours is >= Minimum and <= Maximum;

    /// <summary>Le jour local d'un instant, dans le fuseau du poste.</summary>
    /// <param name="instant">L'instant, en UTC.</param>
    /// <param name="fuseau">Le fuseau du poste.</param>
    public static DateOnly JourLocal(DateTime instant, TimeZoneInfo fuseau)
    {
        ArgumentNullException.ThrowIfNull(fuseau);
        if (instant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Un instant se donne en UTC.", nameof(instant));
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(instant, fuseau));
    }

    /// <summary>
    /// L'échéance d'une relance : la date locale de l'envoi, plus le délai en jours. Elle peut être déjà passée, pour une
    /// candidature saisie après coup.
    /// </summary>
    /// <param name="envoyeeLe">L'instant de l'envoi, en UTC.</param>
    /// <param name="fuseau">Le fuseau du poste.</param>
    /// <param name="jours">Le délai, admis par <see cref="EstAdmis"/>.</param>
    public static DateOnly Echeance(DateTime envoyeeLe, TimeZoneInfo fuseau, int jours)
    {
        if (!EstAdmis(jours))
        {
            throw new ArgumentOutOfRangeException(nameof(jours), jours, HorsBornes);
        }

        return JourLocal(envoyeeLe, fuseau).AddDays(jours);
    }
}
