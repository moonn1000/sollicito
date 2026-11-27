using Sollicito.Application.Export;

namespace Sollicito.Api.Export;

/// <summary>
/// Ce qui voit passer les étapes d'un export (#49) : rien dans la version publiée. Les tests y écrivent la base en plein
/// export, ou y lèvent une erreur du disque. Appelé sur le fil de l'export, jamais sur celui de la fenêtre.
/// </summary>
public interface IEtapesDeLExport
{
    /// <summary>L'export vient d'atteindre cette étape.</summary>
    void Atteinte(EtapeDeLExport etape);
}

/// <summary>Les étapes de la version publiée : rien ne s'y passe.</summary>
public sealed class AucuneEtapeDExport : IEtapesDeLExport
{
    /// <summary>L'instance unique.</summary>
    public static readonly AucuneEtapeDExport Instance = new();

    public void Atteinte(EtapeDeLExport etape)
    {
    }
}
