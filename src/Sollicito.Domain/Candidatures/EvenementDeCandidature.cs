namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Une étape datée de l'historique d'une candidature (modèle de domaine, « Événement de candidature : une entité,
/// trois sortes »). La V1 en a trois sortes : le changement de statut et l'étape, que #12 apporte, et la clôture de
/// relance, qui vient avec les relances (#14). Un événement n'existe que par sa candidature, qui seule l'ajoute ;
/// l'historique se fait par ajouts seulement : aucun événement ne change ni ne disparaît.
/// </summary>
public abstract class EvenementDeCandidature
{
    /// <summary>Un événement survenu à l'instant donné, en UTC.</summary>
    protected EvenementDeCandidature(DateTime survenuLe)
    {
        if (survenuLe.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Un événement se date en UTC.", nameof(survenuLe));
        }

        SurvenuLe = survenuLe;
    }

    /// <summary>L'identifiant technique, donné par la base : il suit l'ordre d'enregistrement.</summary>
    public int Id { get; private set; }

    /// <summary>L'instant de l'événement, en UTC.</summary>
    public DateTime SurvenuLe { get; private set; }
}
