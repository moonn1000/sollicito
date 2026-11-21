using System.Text.RegularExpressions;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// Les restes qu'un arrêt brutal laisse dans le dossier de données (#26, ADR-0008) : la copie de travail d'une mise à jour
/// interrompue, de la taille de la base, et son journal ; les fichiers provisoires des réglages et de la dernière copie.
/// Chacun ne vit que le temps d'une opération, que le processus arrêté n'a pas finie. Ils se reconnaissent à leur nom,
/// avec sa marque tirée au hasard : ni la base, ni ses journaux, ni aucun autre fichier n'y répondent.
/// </summary>
public static partial class RestesDUnArret
{
    /// <summary>
    /// Retire ces restes, et rend leur nombre. À n'appeler que sous le <see cref="VerrouDeLInstance"/> du dossier, avant
    /// toute mise à jour : aucune autre opération ne peut alors les tenir.
    /// </summary>
    public static async Task<int> RetirerAsync(DossierDeLaBase dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        string[] noms;
        try
        {
            noms = [.. Directory.EnumerateFiles(dossier.Chemin).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var retires = 0;
        foreach (var nom in noms)
        {
            var copie = CopieDeTravail().Match(nom);
            if (copie.Success)
            {
                // La copie et ses journaux d'un bloc ; un journal resté seul désigne la même copie.
                await CopiesVerifiees.EffacerAsync(Path.Combine(dossier.Chemin, copie.Groups["copie"].Value));
                retires++;
            }
            else if (FichierProvisoire().IsMatch(nom))
            {
                await CopiesVerifiees.EffacerAsync(Path.Combine(dossier.Chemin, nom));
                retires++;
            }
        }

        return retires;
    }

    [GeneratedRegex(@"^(?<copie>sollicito\.db\.[0-9a-f]{32}\.migration)(-journal|-wal|-shm)?$", RegexOptions.CultureInvariant)]
    private static partial Regex CopieDeTravail();

    [GeneratedRegex(@"^(reglages|sauvegardes)\.json\.[0-9a-f]{32}\.provisoire$", RegexOptions.CultureInvariant)]
    private static partial Regex FichierProvisoire();
}
