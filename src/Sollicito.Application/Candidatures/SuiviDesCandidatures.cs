using Sollicito.Domain.Candidatures;

namespace Sollicito.Application.Candidatures;

/// <summary>
/// Faire avancer une candidature et relire son historique (#12, absorbe #41) : lire sa fiche, changer son statut,
/// corriger son dernier changement, noter une étape. Chaque action lit la candidature et l'écrit dans une même
/// écriture exclusive : deux actions simultanées ne partent jamais du même état. L'horloge date les changements de
/// statut, et donne le fuseau où se lit l'heure saisie d'une étape.
/// </summary>
public sealed class SuiviDesCandidatures(IDepotDesCandidatures depot, TimeProvider horloge)
{
    /// <summary>Le message d'un statut absent.</summary>
    public const string StatutManquant = "Indiquez le nouveau statut.";

    /// <summary>Le message d'un statut qui n'existe pas.</summary>
    public const string StatutInconnu = "Ce statut n'existe pas.";

    /// <summary>Le message d'un changement à corriger qui n'est plus le dernier, ou qui n'existe pas.</summary>
    public const string PasLeDernierChangement = "Ce changement de statut n'est pas le dernier à corriger : la fiche a changé entre-temps.";

    /// <summary>Le message d'un type d'étape absent ou inconnu.</summary>
    public const string TypeInconnu = "Choisissez le type de l'étape : appel, entretien, échange ou autre.";

    /// <summary>Le message d'une date et d'une heure d'étape absentes.</summary>
    public const string DateManquante = "Indiquez la date et l'heure.";

    /// <summary>Le message d'une date et d'une heure d'étape illisibles.</summary>
    public const string DateIllisible = "La date et l'heure s'écrivent AAAA-MM-JJTHH:MM, par exemple 2026-11-20T10:00.";

    /// <summary>La fiche de la candidature, ou rien si elle n'existe pas.</summary>
    public async Task<FicheDUneCandidature?> LireLaFicheAsync(int id, CancellationToken annulation)
    {
        var suivie = await depot.TrouverAsync(id, annulation);
        return suivie is null ? null : FicheDUneCandidature.De(suivie);
    }

    /// <summary>Fait passer la candidature au statut saisi, si le diagramme d'états le permet.</summary>
    public async Task<ResultatDuSuivi> ChangerLeStatutAsync(int id, SaisieDUnStatut saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (string.IsNullOrEmpty(saisie.Statut) || !EssayerDeLireLeNom(saisie.Statut, out StatutDeCandidature nouveau))
        {
            return new ResultatDuSuivi.Refuse(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [SaisieDUnStatut.Champs.Statut] = [string.IsNullOrEmpty(saisie.Statut) ? StatutManquant : StatutInconnu],
            });
        }

        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var suivie = await ecriture.TrouverLaCandidatureAsync(id, annulation);
        if (suivie is null)
        {
            return new ResultatDuSuivi.Introuvable();
        }

        if (!suivie.Candidature.EssayerDeChangerDeStatut(nouveau, Maintenant(), out var refus))
        {
            return new ResultatDuSuivi.NonPermis(refus);
        }

        await ecriture.ValiderAsync(annulation);
        return new ResultatDuSuivi.Fait(FicheDUneCandidature.De(suivie));
    }

    /// <summary>
    /// Corrige le dernier changement de statut, si c'est bien <paramref name="changement"/> : la fiche que l'écran
    /// montrait est encore celle de la base, et une demande répétée ne corrige pas un changement de plus.
    /// </summary>
    public async Task<ResultatDuSuivi> CorrigerLeDernierChangementAsync(int id, int changement, CancellationToken annulation)
    {
        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var suivie = await ecriture.TrouverLaCandidatureAsync(id, annulation);
        if (suivie is null)
        {
            return new ResultatDuSuivi.Introuvable();
        }

        var candidature = suivie.Candidature;
        var aCorriger = candidature.ChangementACorriger;
        if (aCorriger is not null && aCorriger.Id != changement)
        {
            return new ResultatDuSuivi.NonPermis(PasLeDernierChangement);
        }

        if (!candidature.EssayerDeCorrigerLeDernierChangement(Maintenant(), out var refus))
        {
            return new ResultatDuSuivi.NonPermis(refus);
        }

        await ecriture.ValiderAsync(annulation);
        return new ResultatDuSuivi.Fait(FicheDUneCandidature.De(suivie));
    }

    /// <summary>Note une étape, qui ne change pas le statut, ou rend les refus de ses champs, sans rien noter.</summary>
    public async Task<ResultatDuSuivi> NoterUneEtapeAsync(int id, SaisieDUneEtape saisie, CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(saisie);

        await using var ecriture = await depot.CommencerUneEcritureAsync(annulation);
        var suivie = await ecriture.TrouverLaCandidatureAsync(id, annulation);
        if (suivie is null)
        {
            return new ResultatDuSuivi.Introuvable();
        }

        var candidature = suivie.Candidature;
        var refus = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(saisie.Type) || !EssayerDeLireLeNom(saisie.Type, out TypeDEtape type))
        {
            type = default;
            refus[SaisieDUneEtape.Champs.Type] = [TypeInconnu];
        }

        if (!EssayerDeLireLaDate(saisie.SurvenuLe, candidature.EnvoyeeLe, out var survenueLe, out var erreur))
        {
            refus[SaisieDUneEtape.Champs.SurvenuLe] = [erreur];
        }

        if (!Etape.EssayerDeLireLaNote(saisie.Note, out var note, out erreur))
        {
            refus[SaisieDUneEtape.Champs.Note] = [erreur];
        }

        if (refus.Count > 0)
        {
            return new ResultatDuSuivi.Refuse(refus);
        }

        candidature.NoterUneEtape(type, survenueLe, note);
        await ecriture.ValiderAsync(annulation);
        return new ResultatDuSuivi.Fait(FicheDUneCandidature.De(suivie));
    }

    private DateTime Maintenant() => horloge.GetUtcNow().UtcDateTime;

    private bool EssayerDeLireLaDate(string? saisie, DateTime envoyeeLe, out DateTime instant, out string erreur)
    {
        instant = default;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = DateManquante;
            return false;
        }

        if (!HeureSaisie.EssayerDeLire(saisie, out var heureSaisie))
        {
            erreur = DateIllisible;
            return false;
        }

        if (!DateDUneEtape.EssayerDeLire(heureSaisie, horloge.LocalTimeZone, Maintenant(), envoyeeLe, out instant, out var motif))
        {
            erreur = motif;
            return false;
        }

        erreur = "";
        return true;
    }

    // Une valeur énumérée écrite exactement par son nom, comme l'interface l'envoie : ni nombre, ni autre casse, ni
    // liste de noms séparés par des virgules, que lirait Enum.TryParse.
    private static bool EssayerDeLireLeNom<TEnum>(string saisie, out TEnum valeur)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), saisie, StringComparison.Ordinal))
            {
                valeur = candidate;
                return true;
            }
        }

        valeur = default;
        return false;
    }
}

/// <summary>Un nouveau statut, tel que l'écran l'envoie : le nom d'un statut, par exemple <c>Entretien</c>.</summary>
/// <param name="Statut">Le nom du statut voulu.</param>
public sealed record SaisieDUnStatut(string? Statut)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Statut = "statut";
    }
}

/// <summary>Une étape telle que l'écran l'envoie, champ par champ, sans rien de lu ni de converti.</summary>
/// <param name="Type">Le nom du type de l'étape : <c>Appel</c>, <c>Entretien</c>, <c>Echange</c> ou <c>Autre</c>.</param>
/// <param name="SurvenuLe">La date et l'heure telles que l'utilisateur les a tapées, sans décalage : <c>2026-11-20T10:00</c>.</param>
/// <param name="Note">La note, facultative.</param>
public sealed record SaisieDUneEtape(string? Type, string? SurvenuLe, string? Note)
{
    /// <summary>Les noms des champs, tels que les messages de refus les désignent.</summary>
    public static class Champs
    {
        public const string Type = "type";
        public const string SurvenuLe = "survenuLe";
        public const string Note = "note";
    }
}

/// <summary>Ce que rend une action sur une candidature.</summary>
public abstract record ResultatDuSuivi
{
    private ResultatDuSuivi()
    {
    }

    /// <summary>L'action est faite : la fiche à jour.</summary>
    public sealed record Fait(FicheDUneCandidature Fiche) : ResultatDuSuivi;

    /// <summary>La candidature n'existe pas.</summary>
    public sealed record Introuvable : ResultatDuSuivi;

    /// <summary>La saisie est refusée : pour chaque champ refusé, ses messages. Rien ne change.</summary>
    public sealed record Refuse(IReadOnlyDictionary<string, string[]> Refus) : ResultatDuSuivi;

    /// <summary>L'action n'est pas permise dans l'état présent de la candidature, pour ce motif lisible. Rien ne change.</summary>
    public sealed record NonPermis(string Motif) : ResultatDuSuivi;
}
