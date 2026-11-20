using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Textes;

namespace Sollicito.Domain.Candidatures;

/// <summary>
/// Une étape de l'historique qui ne change pas le statut : un appel, un entretien, un échange (#41, absorbée par
/// #12). L'utilisateur la date lui-même, passée ou à venir, et peut y joindre une note (modèle de domaine, « Noter une
/// étape »). Une étape « Entretien » note un entretien ; le statut Entretien dit qu'on en a obtenu un.
/// </summary>
public sealed class Etape : EvenementDeCandidature
{
    /// <summary>La longueur maximale d'une note, en unités UTF-16.</summary>
    public const int LongueurMaximaleDeLaNote = 2000;

    private const string SujetDeLaNote = "La note";

    // Pour Entity Framework Core, qui lit une étape enregistrée.
    private Etape(DateTime survenuLe, TypeDEtape type, string? note)
        : base(survenuLe)
    {
        Type = type;
        Note = note;
    }

    /// <summary>Le type de l'étape.</summary>
    public TypeDEtape Type { get; private set; }

    /// <summary>La note, facultative : un texte libre, sur plusieurs lignes.</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// Pour une correction (#25), l'étape qu'elle corrige, qui reste dans l'historique, marquée comme corrigée ; rien pour
    /// une étape notée. Une correction se corrige à son tour : la dernière de la chaîne est la version en vigueur.
    /// </summary>
    public Etape? Corrige { get; private set; }

    /// <summary>Vrai pour la correction d'une autre étape.</summary>
    public bool EstUneCorrection => Corrige is not null;

    /// <summary>Lit une note saisie (<see cref="TexteSurPlusieursLignes"/>) : vide, elle ne fait aucune note.</summary>
    public static bool EssayerDeLireLaNote(string? saisie, out string? note, [NotNullWhen(false)] out string? erreur) =>
        TexteSurPlusieursLignes.EssayerDeLire(saisie, SujetDeLaNote, LongueurMaximaleDeLaNote, out note, out erreur);

    /// <summary>Une étape, dont la candidature a déjà vérifié la date ; la note doit être admise par <see cref="EssayerDeLireLaNote"/>.</summary>
    internal static Etape Nouvelle(TypeDEtape type, DateTime survenuLe, string? note)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Ce type d'étape n'existe pas.");
        }

        if (!EssayerDeLireLaNote(note, out var noteLue, out var erreur))
        {
            throw new ArgumentException(erreur, nameof(note));
        }

        return new Etape(survenuLe, type, noteLue);
    }

    /// <summary>
    /// La correction de <paramref name="corrigee"/>, dont la candidature a déjà vérifié qu'elle est en vigueur, et la
    /// date : une étape à son tour, aux mêmes règles, qui désigne l'étape qu'elle corrige.
    /// </summary>
    internal static Etape Correction(Etape corrigee, TypeDEtape type, DateTime survenuLe, string? note)
    {
        var correction = Nouvelle(type, survenuLe, note);
        correction.Corrige = corrigee;
        return correction;
    }
}
