using Sollicito.Application.Candidatures;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Un dépôt dont chaque appel échoue, avec un message qui reprend une donnée : la route qui s'en sert lève une
/// exception, comme le ferait une base illisible.
/// </summary>
/// <param name="donnee">La donnée fictive que cite le message de l'exception.</param>
public sealed class DepotEnPanne(string donnee) : IDepotDesCandidatures
{
    public Task<IEcritureDesCandidatures> CommencerUneEcritureAsync(CancellationToken annulation) => throw Panne();

    public Task<IReadOnlyList<CandidatureListee>> ListerAsync(CancellationToken annulation) => throw Panne();

    public Task<IReadOnlyList<EntrepriseConnue>> ListerLesEntreprisesAsync(CancellationToken annulation) => throw Panne();

    public Task<CandidatureSuivie?> TrouverAsync(int id, CancellationToken annulation) => throw Panne();

    private InvalidOperationException Panne() => new($"Panne simulée en lisant {donnee}.");
}
