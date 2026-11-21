using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Sauvegardes;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>La restauration d'une copie (#28), appelée comme la fenêtre hôte l'appelle : en mémoire, après le oui.</summary>
public static class AppelsDeLaRestauration
{
    /// <summary>Le service de la restauration de l'API lancée.</summary>
    public static RestaurationDeLaBase Restauration(this ApiLancee api) => api.Services.GetRequiredService<RestaurationDeLaBase>();

    /// <summary>La copie de la liste qui porte ce nom.</summary>
    public static CopieGardee Copie(this ApiLancee api, string nom) => Assert.Single(api.Sauvegardes().Etat.Copies, copie => copie.Nom == nom);

    /// <summary>Prépare puis restaure cette copie, comme après un oui dans le dialogue, et rend l'issue.</summary>
    public static async Task<IssueDeLaRestauration> RestaurerAsync(this ApiLancee api, CopieGardee copie)
    {
        var preparation = await api.Restauration().PreparerAsync(copie.Id, TestContext.Current.CancellationToken);
        if (preparation.Demande is not { } demande)
        {
            return preparation.Refus!.Value;
        }

        return await api.Restauration().RestaurerAsync(demande, TestContext.Current.CancellationToken);
    }

    /// <summary>Ajoute aux services de l'API ces étapes de la restauration.</summary>
    public static Action<IWebHostBuilder> AvecLesEtapes(IEtapesDeLaRestauration etapes) =>
        builder => builder.ConfigureTestServices(services => services.AddSingleton(etapes));
}

/// <summary>
/// Des étapes de restauration pour les tests : à l'étape choisie, elles lèvent une erreur, ou s'arrêtent jusqu'à ce que le
/// test les relâche. Elles notent les étapes vues.
/// </summary>
public sealed class EtapesDeTest : IEtapesDeLaRestauration
{
    private readonly TaskCompletionSource _atteinte = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _relachee = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>L'étape où agir ; aucune par défaut.</summary>
    public EtapeDeLaRestauration? Arret { get; set; }

    /// <summary>L'erreur à lever à l'arrêt ; sans elle, l'étape attend <see cref="Relacher"/>.</summary>
    public Exception? Erreur { get; set; }

    /// <summary>Les étapes vues, dans l'ordre.</summary>
    public List<EtapeDeLaRestauration> Vues { get; } = [];

    /// <summary>Se termine quand la restauration atteint l'arrêt.</summary>
    public Task Atteinte => _atteinte.Task;

    /// <summary>Relâche une restauration arrêtée.</summary>
    public void Relacher() => _relachee.TrySetResult();

    void IEtapesDeLaRestauration.Atteinte(EtapeDeLaRestauration etape)
    {
        lock (Vues)
        {
            Vues.Add(etape);
        }

        if (etape != Arret)
        {
            return;
        }

        if (Erreur is not null)
        {
            throw Erreur;
        }

        _atteinte.TrySetResult();
        _relachee.Task.Wait(TimeSpan.FromSeconds(30));
    }
}
