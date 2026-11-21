using Sollicito.Application.Sauvegardes;

namespace Sollicito.Api.Sauvegardes;

/// <summary>
/// Ce qui voit passer les étapes d'une restauration (#28) : rien dans la version publiée. Les tests s'y arrêtent, ou y
/// lèvent une erreur ; la version de test de la fenêtre y attend qu'on tue son processus, pour essayer un arrêt brutal à
/// chaque étape. Appelé sur le fil de la restauration, jamais sur celui de la fenêtre.
/// </summary>
public interface IEtapesDeLaRestauration
{
    /// <summary>La restauration vient d'atteindre cette étape.</summary>
    void Atteinte(EtapeDeLaRestauration etape);
}

/// <summary>Les étapes de la version publiée : rien ne s'y passe.</summary>
public sealed class AucuneEtape : IEtapesDeLaRestauration
{
    /// <summary>L'instance unique.</summary>
    public static readonly AucuneEtape Instance = new();

    public void Atteinte(EtapeDeLaRestauration etape)
    {
    }
}
