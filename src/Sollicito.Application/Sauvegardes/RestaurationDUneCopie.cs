using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sollicito.Application.Sauvegardes;

/// <summary>
/// L'identifiant d'une copie gardée, que la page renvoie à la fenêtre hôte pour la restaurer (#28, ADR-0012) : seize
/// caractères hexadécimaux en minuscules, tirés par SHA-256 du lieu, du nom et de l'instant d'écriture de la copie. Il ne
/// contient aucun chemin. Une copie réécrite sous le même nom change d'identifiant : la restauration ne prend donc que la
/// copie que l'utilisateur a vue.
/// </summary>
public static partial class IdentifiantsDesCopies
{
    /// <summary>L'identifiant de cette copie.</summary>
    public static string De(LieuDUneCopie lieu, string nom, DateTime faiteLe)
    {
        ArgumentNullException.ThrowIfNull(nom);

        var texte = string.Create(CultureInfo.InvariantCulture, $"{lieu}\n{nom}\n{faiteLe.ToUniversalTime().Ticks}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texte)).AsSpan(0, 8));
    }

    /// <summary>Vrai si le texte a la forme d'un identifiant : exactement seize caractères hexadécimaux, en minuscules.</summary>
    public static bool AEnLaForme(string? texte) => texte is not null && Forme().IsMatch(texte);

    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forme();
}

/// <summary>L'état de la base en cours, tel que <c>PRAGMA quick_check</c> le dit avant la confirmation (#28).</summary>
public enum EtatDeLaBase
{
    /// <summary>La base se relit saine : la copie d'avant se fera par <c>VACUUM INTO</c>, et se restaurera.</summary>
    Saine,

    /// <summary>La base est abîmée : la copie d'avant sera brute, ses fichiers tels quels, et ne se restaurera pas.</summary>
    Abimee,
}

/// <summary>
/// Une restauration que l'utilisateur doit confirmer : la copie choisie, telle que la liste la montrait, et l'état de la
/// base en cours. Le dialogue de l'hôte en tire sa question.
/// </summary>
/// <param name="Copie">L'identifiant de la copie.</param>
/// <param name="Nom">Le nom de son fichier, sans le dossier.</param>
/// <param name="Lieu">Son dossier.</param>
/// <param name="Sorte">Sa sorte.</param>
/// <param name="FaiteLe">L'instant de son écriture, en UTC.</param>
/// <param name="EtatDeLaBase">L'état de la base en cours, lu en lecture seule.</param>
public sealed record DemandeDeRestauration(string Copie, string Nom, LieuDUneCopie Lieu, SorteDeCopie Sorte, DateTime FaiteLe, EtatDeLaBase EtatDeLaBase);

/// <summary>La préparation d'une restauration : la demande à confirmer, ou le motif qui l'empêche déjà.</summary>
/// <param name="Demande">La demande, si la restauration peut se proposer.</param>
/// <param name="Refus">Le motif du refus, sinon.</param>
public sealed record PreparationDeLaRestauration(DemandeDeRestauration? Demande, IssueDeLaRestauration? Refus);

/// <summary>
/// Ce qu'a donné une restauration. Seules <see cref="Restauree"/> et <see cref="RestaureeDepuisUneBaseAbimee"/> ont changé
/// la base.
/// </summary>
public enum IssueDeLaRestauration
{
    /// <summary>La base est remplacée par la copie ; la copie d'avant est gardée, et se restaure.</summary>
    Restauree,

    /// <summary>La base abîmée est remplacée par la copie ; ses fichiers sont gardés tels quels, sans se restaurer.</summary>
    RestaureeDepuisUneBaseAbimee,

    /// <summary>La copie ne se relit pas saine : rien n'a changé.</summary>
    Abimee,

    /// <summary>Le fichier n'est pas une copie de Sollicito que cette version sait lire : rien n'a changé.</summary>
    Etrangere,

    /// <summary>La copie vient d'une version plus récente de Sollicito : rien n'a changé.</summary>
    PlusRecente,

    /// <summary>La copie, ou son dossier, a disparu : rien n'a changé.</summary>
    Introuvable,

    /// <summary>La copie a été réécrite depuis l'affichage de la liste : rien n'a changé.</summary>
    Changee,

    /// <summary>L'état de la base n'est plus celui que le dialogue annonçait : rien n'a changé, l'utilisateur redemande.</summary>
    BaseChangee,

    /// <summary>Une autre restauration, ou des requêtes qui ne finissent pas, occupent la base : rien n'a changé.</summary>
    Occupee,

    /// <summary>La copie d'avant n'a pas pu se faire : la base n'a pas été remplacée.</summary>
    CopieDAvantImpossible,

    /// <summary>Une erreur d'écriture ou de lecture, comme la place qui manque : rien n'a changé.</summary>
    Impossible,
}

/// <summary>
/// Les étapes d'une restauration, dans l'ordre (#28). Les tests et la version de test de la fenêtre s'y arrêtent, pour
/// essayer un arrêt brutal ou une erreur à chacune ; la version publiée ne fait rien.
/// </summary>
public enum EtapeDeLaRestauration
{
    /// <summary>La copie choisie est recopiée dans le dossier de données, pas encore vérifiée.</summary>
    CopieDeTravailEcrite,

    /// <summary>La copie de travail est vérifiée, et migrée si elle était plus ancienne.</summary>
    CopieDeTravailVerifiee,

    /// <summary>La porte de la base est fermée : plus aucune écriture n'entre.</summary>
    PorteFermee,

    /// <summary>La copie d'avant est écrite sous son nom provisoire, pas encore renommée.</summary>
    CopieDAvantEcrite,

    /// <summary>La copie d'avant porte son nom final.</summary>
    CopieDAvantFaite,

    /// <summary>Le remplacement a copié une première tranche de pages : sa transaction est ouverte.</summary>
    PendantLeRemplacement,

    /// <summary>Le remplacement est validé, avant la réouverture de la porte.</summary>
    BaseRemplacee,
}
