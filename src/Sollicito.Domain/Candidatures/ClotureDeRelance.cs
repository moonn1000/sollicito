using Sollicito.Domain.Relances;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// La clôture d'une relance (#14), troisième sorte d'événement de candidature : la relance marquée faite ou abandonnée
/// entre ainsi dans l'historique, datée par l'horloge au moment où on la marque. Elle désigne la relance close, dont
/// l'état dit l'issue. Comme tout événement en vigueur, elle borne la date d'envoi corrigée (#25).
/// </summary>
public sealed class ClotureDeRelance : EvenementDeCandidature
{
    // Pour Entity Framework Core, qui lit une clôture enregistrée.
    private ClotureDeRelance(DateTime survenuLe)
        : base(survenuLe)
    {
    }

    /// <summary>La relance close, faite ou abandonnée.</summary>
    public Relance Relance { get; private set; } = null!;

    /// <summary>La clôture de <paramref name="relance"/>, que la candidature vient de clore.</summary>
    internal static ClotureDeRelance De(Relance relance, DateTime maintenant)
    {
        ArgumentNullException.ThrowIfNull(relance);
        if (relance.EstAFaire)
        {
            throw new InvalidOperationException("Seule une relance faite ou abandonnée entre dans l'historique.");
        }

        return new ClotureDeRelance(maintenant) { Relance = relance };
    }
}
