using Sollicito.Domain.Entreprises;

namespace Sollicito.Domain.Offres;

/// <summary>
/// Le poste d'une offre, normalisé pour la règle 3 du dédoublonnage (modèle de domaine, « Dédoublonner les offres ») :
/// les étapes 1 à 4 de la clé d'une entreprise (<see cref="CleDEntreprise"/>), sans retirer de forme juridique. Accents
/// retirés, « œ » et « æ » en deux lettres, minuscules, points retirés, toute autre ponctuation en espace, espaces
/// réduites : « Développeur .NET (H/F) » et « developpeur  .net h/f » donnent tous deux <c>developpeur net h f</c>.
/// </summary>
public static class PosteNormalise
{
    /// <summary>Le poste normalisé ; vide pour un poste sans lettre ni chiffre.</summary>
    /// <param name="poste">Un poste en Unicode valide (voir <see cref="Textes.TexteDUneLigne.EstAdmis"/>).</param>
    public static string De(string poste) => string.Join(' ', CleDEntreprise.MotsDe(poste));
}
