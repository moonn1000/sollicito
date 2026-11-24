namespace Sollicito.Application.IA;

/// <summary>
/// Le choix de l'adaptateur (#43, ADR-0016) : selon le réglage et la clé, l'IA choisie peut-elle servir, et sinon
/// pourquoi ? Une IA mal réglée ou une clé illisible ne cassent rien : l'adaptateur <see cref="AucuneIA"/> sert à leur
/// place, avec sa raison.
/// </summary>
public static class ChoixDuFournisseurDIA
{
    /// <summary>
    /// La raison pour laquelle aucune IA ne sert, ou <c>null</c> si le fournisseur réglé est prêt : choisi, lisible, et
    /// sa clé enregistrée et lisible s'il en prend une.
    /// </summary>
    public static RaisonDIndisponibilite? Raison(FournisseurDIA fournisseur, bool reglageLisible, EtatDeLaCle cle)
    {
        if (!reglageLisible)
        {
            return RaisonDIndisponibilite.ReglageIllisible;
        }

        if (fournisseur == FournisseurDIA.Aucun)
        {
            return RaisonDIndisponibilite.AucuneIA;
        }

        if (!fournisseur.PrendUneCle())
        {
            return null;
        }

        return cle switch
        {
            EtatDeLaCle.Enregistree => null,
            EtatDeLaCle.Illisible => RaisonDIndisponibilite.CleIllisible,
            _ => RaisonDIndisponibilite.CleAbsente,
        };
    }
}

/// <summary>La clé du fournisseur réglé, telle que l'écran la dit, sans jamais la montrer.</summary>
public enum EtatDeLaCle
{
    /// <summary>Le fournisseur réglé ne prend pas de clé : aucune IA, ou un modèle local.</summary>
    SansObjet,

    /// <summary>Aucune clé n'est enregistrée.</summary>
    Aucune,

    /// <summary>Une clé est enregistrée, et se relit.</summary>
    Enregistree,

    /// <summary>Une clé est enregistrée, mais ne se relit pas sur ce poste : elle est à ressaisir.</summary>
    Illisible,
}
