using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Temps;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// La date et l'heure d'envoi d'une candidature, saisies dans le fuseau du poste (modèle de domaine, « Statuts d'une
/// candidature », « Départ », et « Dates et heures ») : elles se gardent en UTC, et ne sont jamais dans le futur.
/// </summary>
public static class DateDEnvoi
{
    /// <summary>La plus ancienne date d'envoi admise, en heure locale : le 1er janvier 2000 à 00:00.</summary>
    public static readonly DateTime Plancher = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Le message d'une date d'envoi dans le futur.</summary>
    public const string DansLeFutur = "La date et l'heure d'envoi ne peuvent pas être dans le futur.";

    /// <summary>
    /// Lit une date et une heure d'envoi saisies. Rend vrai et l'instant UTC, ou faux et le motif du refus : une date
    /// avant le <see cref="Plancher"/>, une heure qui n'existe pas (passage à l'heure d'été), un instant après
    /// <paramref name="maintenant"/>.
    /// </summary>
    /// <param name="heureSaisie">L'heure saisie, sans fuseau (<see cref="DateTimeKind.Unspecified"/>).</param>
    /// <param name="fuseau">Le fuseau du poste.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    /// <param name="instant">L'instant d'envoi, en UTC, s'il est admis.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(
        DateTime heureSaisie,
        TimeZoneInfo fuseau,
        DateTime maintenant,
        out DateTime instant,
        [NotNullWhen(false)] out string? erreur)
    {
        ArgumentNullException.ThrowIfNull(fuseau);
        if (maintenant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("L'instant présent se donne en UTC.", nameof(maintenant));
        }

        instant = default;
        if (heureSaisie < Plancher)
        {
            erreur = "La date d'envoi ne peut pas être antérieure au 1er janvier 2000.";
            return false;
        }

        // Plus de deux jours après l'heure locale présente : dans le futur quel que soit le décalage du fuseau. Ce
        // premier tri garde aussi la conversion loin des bornes du calendrier.
        if (heureSaisie > TimeZoneInfo.ConvertTimeFromUtc(maintenant, fuseau).AddDays(2))
        {
            erreur = DansLeFutur;
            return false;
        }

        if (!HeureLocale.EssayerVersUtc(heureSaisie, fuseau, out instant))
        {
            erreur = "Cette heure n'existe pas : ce jour-là, au passage à l'heure d'été, l'horloge avance d'une heure.";
            return false;
        }

        if (instant > maintenant)
        {
            instant = default;
            erreur = DansLeFutur;
            return false;
        }

        erreur = null;
        return true;
    }
}
