using Sollicito.Application.Candidatures;
using Sollicito.Application.Relances;
using Sollicito.Api.Sauvegardes;

namespace Sollicito.Api.Relances;

/// <summary>
/// Les brouillons des relances, pour la fenêtre hôte (#14, ADR-0011) : elle seule ouvre le lien <c>mailto</c>, et ne
/// reçoit de la page que l'identifiant d'une relance. Les champs viennent de la base, lus ici en mémoire, sans route
/// HTTP : l'adresse du contact, validée à sa saisie, et le texte du brouillon, construit à partir du poste et de la date
/// d'envoi, validés eux aussi. Le service ouvre sa propre portée, comme une requête : la fenêtre le résout dans les
/// services de l'API, hors de toute requête.
/// </summary>
public sealed class BrouillonsDesRelances(IServiceScopeFactory portees, TimeProvider horloge, PorteDeLaBase porte)
{
    /// <summary>
    /// Le brouillon de la relance, si elle est à faire ; rien si elle n'existe pas, si elle est close, ou pendant une
    /// restauration, porte de la base fermée (#28).
    /// </summary>
    public async Task<BrouillonDeRelance?> PreparerAsync(int relanceId, CancellationToken annulation)
    {
        if (!porte.EssayerDEntrer())
        {
            return null;
        }

        try
        {
            await using var portee = portees.CreateAsyncScope();
            var depot = portee.ServiceProvider.GetRequiredService<IDepotDesCandidatures>();
            var relance = await depot.LireLaRelancePourLeBrouillonAsync(relanceId, annulation);
            return relance is null ? null : BrouillonDeRelance.De(relance, horloge.LocalTimeZone);
        }
        finally
        {
            porte.Sortir();
        }
    }
}
