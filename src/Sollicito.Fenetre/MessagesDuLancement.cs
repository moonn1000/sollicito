using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Fenetre;

/// <summary>Ce que la fenêtre dit d'un lancement qui a échoué, sans jamais reprendre le message d'une exception.</summary>
internal static class MessagesDuLancement
{
    /// <summary>Le message d'un lancement qui a échoué pour une autre raison qu'une mise à jour de la base.</summary>
    public const string ApiNonDemarree = "Sollicito n'a pas pu démarrer son API locale.";

    /// <summary>
    /// Le message d'une mise à jour de la base ratée, qui dit que la base n'a pas changé et où se trouve la copie d'avant
    /// (#26) ; pour toute autre erreur, le message général. Aucun ne cite de chemin du poste.
    /// </summary>
    public static string Pour(Exception erreur) =>
        Chaine(erreur).OfType<MiseAJourDeLaBaseEchouee>().FirstOrDefault()?.Message ?? ApiNonDemarree;

    // L'erreur, ses erreurs intérieures, et celles d'un groupe : l'hôte en réunit plusieurs.
    private static IEnumerable<Exception> Chaine(Exception erreur)
    {
        for (Exception? courante = erreur; courante is not null; courante = courante.InnerException)
        {
            yield return courante;
            if (courante is AggregateException groupe)
            {
                foreach (var interieure in groupe.InnerExceptions.SelectMany(Chaine))
                {
                    yield return interieure;
                }
            }
        }
    }
}
