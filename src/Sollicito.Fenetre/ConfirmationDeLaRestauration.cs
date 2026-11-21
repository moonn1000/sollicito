using System.Globalization;
using Sollicito.Application.Relances;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Fenetre;

/// <summary>La question que pose le dialogue de la restauration : son titre, et ce que l'utilisateur doit savoir.</summary>
/// <param name="Titre">La question, avec la date de la copie.</param>
/// <param name="Texte">Ce qui sera perdu, et ce que Sollicito garde d'abord.</param>
internal sealed record QuestionDeRestauration(string Titre, string Texte);

/// <summary>
/// Ce qui demande à l'utilisateur s'il restaure une copie (#28, ADR-0012) : un dialogue de Windows dans la version
/// publiée, qu'un script de la page ne peut pas cliquer. Rien n'est remplacé sans son oui.
/// </summary>
internal interface IConfirmationDeLaRestauration
{
    /// <summary>Vrai si l'utilisateur a choisi « Restaurer » ; faux s'il a annulé ou fermé le dialogue.</summary>
    bool Confirmer(IWin32Window proprietaire, QuestionDeRestauration question);
}

/// <summary>Les textes du dialogue de la restauration, mot pour mot (#28).</summary>
internal static class TextesDeLaRestauration
{
    /// <summary>Le bouton qui restaure.</summary>
    public const string BoutonRestaurer = "Restaurer";

    /// <summary>Le bouton qui renonce, choisi par défaut.</summary>
    public const string BoutonAnnuler = "Annuler";

    private const string PerteDUneBaseSaine =
        "Votre base sera remplacée par cette copie : tout ce que vous avez saisi ou modifié depuis sera perdu. Sollicito "
        + "garde d'abord une copie de votre base actuelle : vous pourrez y revenir depuis les réglages.";

    // Une base abîmée ne se copie plus par VACUUM INTO : ses fichiers se gardent tels quels, et ne se restaurent pas. Le
    // dialogue ne promet donc aucun retour.
    private const string PerteDUneBaseAbimee =
        "Votre base actuelle est abîmée : elle sera remplacée par cette copie, et tout ce que vous avez saisi ou modifié "
        + "depuis sera perdu. Sollicito garde d'abord ses fichiers tels quels, dans le dossier des sauvegardes par défaut, "
        + "mais ne pourra pas les restaurer.";

    private const string AutreInstallation =
        "Cette copie vient d'une autre installation de Sollicito : un autre poste, ou celui-ci avant une réinstallation.";

    /// <summary>La question pour cette demande : la date de la copie, à l'heure de ce poste, et ce qui sera perdu.</summary>
    public static QuestionDeRestauration Question(DemandeDeRestauration demande, TimeZoneInfo fuseau)
    {
        ArgumentNullException.ThrowIfNull(demande);

        var texte = demande.EtatDeLaBase == EtatDeLaBase.Saine ? PerteDUneBaseSaine : PerteDUneBaseAbimee;
        if (demande.Sorte == SorteDeCopie.DuJourDUneAutreInstallation)
        {
            texte += "\n\n" + AutreInstallation;
        }

        return new QuestionDeRestauration($"Restaurer la copie du {DateEtHeure(demande.FaiteLe, fuseau)} ?", texte);
    }

    /// <summary>Un instant, à l'heure de ce fuseau, en français, sans dépendre de la langue de Windows : « 18 novembre 2026 à 21:04 ».</summary>
    public static string DateEtHeure(DateTime instantUtc, TimeZoneInfo fuseau)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc), fuseau);
        return $"{BrouillonDeRelance.EnToutesLettres(DateOnly.FromDateTime(local))} à {local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>Le dialogue de Windows de la version publiée : « Restaurer » ou « Annuler », ce dernier par défaut.</summary>
internal sealed class ConfirmationParLeDialogue : IConfirmationDeLaRestauration
{
    public bool Confirmer(IWin32Window proprietaire, QuestionDeRestauration question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var restaurer = new TaskDialogButton(TextesDeLaRestauration.BoutonRestaurer);
        var annuler = new TaskDialogButton(TextesDeLaRestauration.BoutonAnnuler);
        var page = new TaskDialogPage
        {
            Caption = "Sollicito",
            Heading = question.Titre,
            Text = question.Texte,
            Icon = TaskDialogIcon.Warning,
            Buttons = { restaurer, annuler },
            DefaultButton = annuler,
            AllowCancel = true,
        };
        return TaskDialog.ShowDialog(proprietaire, page) == restaurer;
    }
}
