namespace Sollicito.Infrastructure.IA.EnLigne;

/// <summary>
/// Une demande à l'IA à la fois (#48, ADR-0017), pour tous les fournisseurs en ligne : une demande qui arrive pendant une
/// autre ne l'attend pas, elle échoue aussitôt, sans requête. Chaque appel coûte à l'utilisateur ; l'essai de la clé,
/// l'extraction d'une offre (#46) et le brouillon de relance (#47) en héritent.
/// </summary>
public sealed class PorteDesDemandes
{
    private int _occupee;

    /// <summary>Prend la porte ; faux si une autre demande la tient.</summary>
    public bool Entrer() => Interlocked.CompareExchange(ref _occupee, 1, 0) == 0;

    /// <summary>Rend la porte, prise par <see cref="Entrer"/>.</summary>
    public void Sortir() => Volatile.Write(ref _occupee, 0);
}
