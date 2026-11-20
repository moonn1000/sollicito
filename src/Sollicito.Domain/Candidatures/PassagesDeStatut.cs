using System.Diagnostics.CodeAnalysis;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Les passages permis entre les statuts d'une candidature (modèle de domaine, « Statuts d'une candidature ») : la
/// table de ce document fait foi, et cette classe la recopie. Tout autre passage est refusé, y compris vers le statut
/// déjà en place. Corriger le dernier changement n'est pas un passage (<see cref="Candidature.EssayerDeCorrigerLeDernierChangement"/>).
/// </summary>
public static class PassagesDeStatut
{
    private static readonly StatutDeCandidature[] DepuisEnvoyee =
        [StatutDeCandidature.Entretien, StatutDeCandidature.Refusee, StatutDeCandidature.Retiree, StatutDeCandidature.SansReponse];

    private static readonly StatutDeCandidature[] DepuisEntretien =
        [StatutDeCandidature.Proposition, StatutDeCandidature.Refusee, StatutDeCandidature.Retiree, StatutDeCandidature.SansReponse];

    private static readonly StatutDeCandidature[] DepuisProposition =
        [StatutDeCandidature.Acceptee, StatutDeCandidature.Refusee, StatutDeCandidature.Retiree, StatutDeCandidature.SansReponse];

    /// <summary>Les statuts où une candidature peut passer depuis <paramref name="statut"/> : aucun depuis un statut clos.</summary>
    public static IReadOnlyList<StatutDeCandidature> Depuis(StatutDeCandidature statut) => statut switch
    {
        StatutDeCandidature.Envoyee => DepuisEnvoyee,
        StatutDeCandidature.Entretien => DepuisEntretien,
        StatutDeCandidature.Proposition => DepuisProposition,
        StatutDeCandidature.Acceptee or StatutDeCandidature.Refusee or StatutDeCandidature.Retiree or StatutDeCandidature.SansReponse => [],
        _ => throw new ArgumentOutOfRangeException(nameof(statut), statut, "Ce statut n'existe pas."),
    };

    /// <summary>Vrai si le passage de <paramref name="actuel"/> à <paramref name="nouveau"/> est permis.</summary>
    public static bool EstPermis(StatutDeCandidature actuel, StatutDeCandidature nouveau) => Depuis(actuel).Contains(nouveau);

    /// <summary>Vrai pour les quatre statuts qui closent la candidature : Acceptée, Refusée, Retirée, Sans réponse.</summary>
    public static bool EstClos(StatutDeCandidature statut) => Depuis(statut).Count == 0;

    /// <summary>Le statut en toutes lettres, tel que les messages le citent : « Sans réponse ».</summary>
    public static string Libelle(StatutDeCandidature statut) => statut switch
    {
        StatutDeCandidature.Envoyee => "Envoyée",
        StatutDeCandidature.Entretien => "Entretien",
        StatutDeCandidature.Proposition => "Proposition",
        StatutDeCandidature.Acceptee => "Acceptée",
        StatutDeCandidature.Refusee => "Refusée",
        StatutDeCandidature.Retiree => "Retirée",
        StatutDeCandidature.SansReponse => "Sans réponse",
        _ => throw new ArgumentOutOfRangeException(nameof(statut), statut, "Ce statut n'existe pas."),
    };

    /// <summary>
    /// Rend vrai si le passage est permis ; sinon faux, et un message lisible qui dit pourquoi et ce qui reste
    /// possible. Le message ne cite que des statuts.
    /// </summary>
    public static bool EssayerDePasser(StatutDeCandidature actuel, StatutDeCandidature nouveau, [NotNullWhen(false)] out string? refus)
    {
        if (!Enum.IsDefined(nouveau))
        {
            throw new ArgumentOutOfRangeException(nameof(nouveau), nouveau, "Ce statut n'existe pas.");
        }

        if (EstPermis(actuel, nouveau))
        {
            refus = null;
            return true;
        }

        if (actuel == nouveau)
        {
            refus = $"La candidature est déjà au statut {Libelle(actuel)}.";
        }
        else if (EstClos(actuel))
        {
            refus = $"La candidature est close, au statut {Libelle(actuel)} : son statut ne change plus. "
                + "Si ce statut est une erreur, corrigez le dernier changement.";
        }
        else
        {
            refus = $"Une candidature ne passe pas du statut {Libelle(actuel)} au statut {Libelle(nouveau)}. "
                + $"Depuis {Libelle(actuel)}, elle peut passer à {Enumerer(Depuis(actuel))}.";
        }

        return false;
    }

    // « Entretien, Refusée, Retirée ou Sans réponse ».
    private static string Enumerer(IReadOnlyList<StatutDeCandidature> statuts) =>
        string.Join(", ", statuts.Take(statuts.Count - 1).Select(Libelle)) + " ou " + Libelle(statuts[^1]);
}
