namespace Sollicito.Application.IA;

/// <summary>
/// L'adaptateur « aucune IA » (#43) : il ne contacte rien, et répond toujours que l'IA est indisponible, avec sa raison.
/// C'est lui qui sert tant qu'aucune IA n'est choisie, ou qu'elle ne peut pas servir : réglage illisible, clé absente ou
/// illisible, fournisseur pas encore branché. Les fonctions qui s'appuient sur l'IA (#46, #47) le disent alors à
/// l'utilisateur, et tout le reste fonctionne.
/// </summary>
public sealed class AucuneIA(RaisonDIndisponibilite raison) : IFournisseurDIA
{
    /// <summary>Pourquoi aucune IA ne répond.</summary>
    public RaisonDIndisponibilite Raison { get; } = raison;

    /// <inheritdoc />
    public FournisseurDIA Fournisseur => FournisseurDIA.Aucun;

    /// <inheritdoc />
    public Task<ReponseDeLIA> DemanderAsync(DemandeALIA demande, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(demande);
        annulation.ThrowIfCancellationRequested();
        return Task.FromResult<ReponseDeLIA>(new ReponseDeLIA.Indisponible(Raison));
    }
}
