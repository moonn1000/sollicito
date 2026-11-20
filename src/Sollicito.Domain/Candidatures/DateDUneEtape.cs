using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Temps;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// La date et l'heure d'une étape, saisies dans le fuseau du poste (modèle de domaine, « Noter une étape », et « Dates
/// et heures ») : elles se gardent en UTC. Une étape peut être passée ou à venir, mais l'historique commence à l'envoi :
/// elle ne le précède pas, et ne tombe pas plus d'un an après l'instant présent.
/// </summary>
public static class DateDUneEtape
{
    /// <summary>Le message d'une étape datée avant l'envoi de la candidature.</summary>
    public const string AvantLEnvoi = "La date et l'heure ne peuvent pas précéder l'envoi de la candidature.";

    /// <summary>Le message d'une étape datée plus d'un an après l'instant présent.</summary>
    public const string TropLoin = "La date et l'heure ne peuvent pas être à plus d'un an dans le futur.";

    /// <summary>
    /// Lit une date et une heure d'étape saisies. Rend vrai et l'instant UTC, ou faux et le motif du refus : une heure
    /// avant l'envoi, ou plus d'un an après <paramref name="maintenant"/>, en heure locale, ou qui n'existe pas (passage
    /// à l'heure d'été).
    /// </summary>
    /// <param name="heureSaisie">L'heure saisie, sans fuseau (<see cref="DateTimeKind.Unspecified"/>).</param>
    /// <param name="fuseau">Le fuseau du poste.</param>
    /// <param name="maintenant">L'instant présent, en UTC.</param>
    /// <param name="envoyeeLe">L'instant de l'envoi de la candidature, en UTC.</param>
    /// <param name="instant">L'instant de l'étape, en UTC, s'il est admis.</param>
    /// <param name="erreur">Le motif du refus, sinon.</param>
    public static bool EssayerDeLire(
        DateTime heureSaisie,
        TimeZoneInfo fuseau,
        DateTime maintenant,
        DateTime envoyeeLe,
        out DateTime instant,
        [NotNullWhen(false)] out string? erreur)
    {
        ArgumentNullException.ThrowIfNull(fuseau);
        if (maintenant.Kind != DateTimeKind.Utc || envoyeeLe.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("L'instant présent et l'envoi se donnent en UTC.", nameof(maintenant));
        }

        instant = default;
        // Avant le plancher des envois, l'heure précède l'envoi, quel qu'il soit. Ce premier tri, comme le suivant,
        // garde la conversion loin des bornes du calendrier.
        if (heureSaisie < DateDEnvoi.Plancher)
        {
            erreur = AvantLEnvoi;
            return false;
        }

        if (heureSaisie > TimeZoneInfo.ConvertTimeFromUtc(maintenant, fuseau).AddYears(1))
        {
            erreur = TropLoin;
            return false;
        }

        if (!HeureLocale.EssayerVersUtc(heureSaisie, fuseau, out instant))
        {
            erreur = HeureLocale.HeureInexistante;
            return false;
        }

        if (instant < envoyeeLe)
        {
            instant = default;
            erreur = AvantLEnvoi;
            return false;
        }

        erreur = null;
        return true;
    }
}
