namespace Sollicito.Domain.Tests.Outils;

/// <summary>Les fuseaux des tests : celui de Windows en Belgique, que les tests fixent au lieu de prendre celui du poste.</summary>
internal static class Fuseaux
{
    /// <summary>« Romance Standard Time », l'équivalent Windows d'Europe/Brussels : UTC+1 en hiver, UTC+2 en été.</summary>
    public static TimeZoneInfo Bruxelles { get; } = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    /// <summary>Une heure saisie, sans fuseau.</summary>
    public static DateTime Saisie(int annee, int mois, int jour, int heure, int minute) =>
        new(annee, mois, jour, heure, minute, 0, DateTimeKind.Unspecified);

    /// <summary>Un instant UTC.</summary>
    public static DateTime Utc(int annee, int mois, int jour, int heure, int minute) =>
        new(annee, mois, jour, heure, minute, 0, DateTimeKind.Utc);
}
