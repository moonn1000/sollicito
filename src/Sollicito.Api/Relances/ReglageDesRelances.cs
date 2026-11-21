using Sollicito.Domain.Relances;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Api.Relances;

/// <summary>
/// Le délai par défaut des relances (#14, ADR-0011), deuxième réglage de l'application : il vit dans
/// <c>reglages.json</c>, à côté de la base et hors d'elle (ADR-0008), et se lit à part du dossier des sauvegardes. Il ne
/// sert qu'aux relances planifiées ensuite : le changer ne déplace aucune relance.
/// </summary>
public sealed class ReglageDesRelances(DossierDeLaBase dossier)
{
    private readonly FichierDesReglages _fichier = new(dossier);

    /// <summary>Le délai qui s'applique, et ce qui ne se lisait pas.</summary>
    public ReglageDuDelai Lire()
    {
        var lecture = _fichier.Lire();
        return new ReglageDuDelai(lecture.Reglages.DelaiDesRelancesEnVigueur, lecture.DelaiLisible, lecture.FichierLisible);
    }

    /// <summary>
    /// Change le délai par défaut, admis par <see cref="DelaiDeRelance.EstAdmis"/>. Rend l'issue : changé, refusé sur un
    /// fichier illisible en entier, que seul le choix d'un dossier des sauvegardes réécrit (ADR-0008), ou impossible à
    /// écrire.
    /// </summary>
    public async Task<IssueDuReglage> ChangerAsync(int jours)
    {
        if (!DelaiDeRelance.EstAdmis(jours))
        {
            throw new ArgumentOutOfRangeException(nameof(jours), jours, DelaiDeRelance.HorsBornes);
        }

        try
        {
            return await _fichier.EcrireLeDelaiDesRelancesAsync(jours) ? IssueDuReglage.Change : IssueDuReglage.FichierIllisible;
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            return IssueDuReglage.Impossible;
        }
    }
}

/// <summary>Le délai par défaut des relances, tel que la section « Relances » des réglages le montre.</summary>
/// <param name="DelaiParDefaut">Le délai qui s'applique, en jours : celui du réglage, ou celui d'usine.</param>
/// <param name="DelaiLisible">Faux si le délai enregistré ne se lisait pas : celui d'usine s'applique alors.</param>
/// <param name="FichierLisible">Faux si le fichier des réglages ne se lisait pas en entier : le délai ne s'y écrit pas.</param>
public sealed record ReglageDuDelai(int DelaiParDefaut, bool DelaiLisible, bool FichierLisible);

/// <summary>L'issue d'un changement du délai par défaut.</summary>
public enum IssueDuReglage
{
    /// <summary>Le délai est enregistré.</summary>
    Change,

    /// <summary>Le fichier des réglages ne se lit pas en entier : rien n'est écrit.</summary>
    FichierIllisible,

    /// <summary>Le fichier n'a pas pu s'écrire : rien ne change.</summary>
    Impossible,
}
