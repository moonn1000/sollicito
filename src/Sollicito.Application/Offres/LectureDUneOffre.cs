using System.Diagnostics.CodeAnalysis;
using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Offres;

/// <summary>
/// Le pas de lecture du cœur (#36, #40 ; ADR-0013, ADR-0015) : ce que le cœur admet d'une offre reçue, sans rien lire en
/// base. L'offre collée et chaque ligne d'un fichier CSV y passent, avant tout dédoublonnage.
/// <list type="number">
/// <item>Les champs, par les règles du domaine : chacun refusé à part, sans reprendre le texte reçu. Une ligne de CSV
/// admet aussi une date <c>JJ/MM/AAAA</c>.</item>
/// <item>Le texte : requis pour une offre collée ; vide, il vaut absence pour une ligne de CSV.</item>
/// <item>Puis l'empreinte, sur les champs que le connecteur annonce, dans son ordre, chacun tel que lu : la date sous sa
/// forme <c>AAAA-MM-JJ</c>, un champ vide comme une partie vide. Un connecteur qui n'annonce aucun champ, ou un champ deux
/// fois, est une erreur de programmation, pas un refus.</item>
/// </list>
/// La validation passe toujours avant l'empreinte : un non-caractère, de l'UTF-8 valide, ferait lever la normalisation.
/// </summary>
public static class LectureDUneOffre
{
    /// <summary>
    /// Lit l'offre reçue. Rend vrai et l'offre admise, ou faux et, pour chaque champ refusé, ses messages.
    /// </summary>
    /// <param name="offre">L'offre reçue d'un connecteur.</param>
    /// <param name="aujourdhui">Aujourd'hui, en date locale du poste : la date de publication ne passe pas après.</param>
    /// <param name="admise">L'offre admise, si aucun champ n'est refusé.</param>
    /// <param name="refus">Les messages des champs refusés, par champ ; vide si l'offre est admise.</param>
    public static bool EssayerDeLire(
        OffreRecue offre,
        DateOnly aujourdhui,
        [NotNullWhen(true)] out OffreAdmise? admise,
        out IReadOnlyDictionary<string, string[]> refus)
    {
        ArgumentNullException.ThrowIfNull(offre);
        ArgumentNullException.ThrowIfNull(offre.Champs);
        if (!Enum.IsDefined(offre.Source))
        {
            throw new ArgumentOutOfRangeException(nameof(offre), offre.Source, "Source inconnue.");
        }

        VerifierLesChampsDeLEmpreinte(offre.ChampsDeLEmpreinte);

        admise = null;
        var refuses = new Dictionary<string, string[]>(StringComparer.Ordinal);
        refus = refuses;
        string? texte = null;
        var texteRecu = offre.Champs.Texte;
        if (TexteRequis(offre.Source) || !string.IsNullOrWhiteSpace(texteRecu))
        {
            if (!TexteDUneOffre.EssayerDeLire(texteRecu, out texte, out var refusDuTexte))
            {
                refuses[SaisieDUneOffre.Champs.Texte] = [refusDuTexte];
            }
        }

        var lus = LireLesChamps(
            offre.Champs.Entreprise,
            offre.Champs.Poste,
            offre.Champs.Lieu,
            offre.Champs.PublieeLe,
            offre.Champs.Adresse,
            aujourdhui,
            refuses,
            dateJourEnPremierAdmise: offre.Source == SourceDOffre.FichierCsv);
        if (lus is null || refuses.Count > 0)
        {
            return false;
        }

        var (nom, champs) = lus.Value;
        var parties = offre.ChampsDeLEmpreinte.Select(champ => champ switch
        {
            ChampDeLEmpreinte.Texte => texte ?? "",
            ChampDeLEmpreinte.Poste => champs.Poste,
            ChampDeLEmpreinte.Entreprise => nom ?? "",
            ChampDeLEmpreinte.Lieu => champs.Lieu ?? "",
            ChampDeLEmpreinte.PublieeLe => champs.PublieeLe is { } date ? DateDePublication.FormeCanonique(date) : "",
            ChampDeLEmpreinte.Adresse => champs.Adresse ?? "",
            _ => throw new ArgumentOutOfRangeException(nameof(offre), champ, "Champ de l'empreinte inconnu."),
        }).ToArray();
        admise = new OffreAdmise(offre.Source, nom, champs, texte, EmpreinteDUneOffre.De(parties));
        return true;
    }

    /// <summary>
    /// Lit l'entreprise, facultative, et les champs d'une offre ; ajoute les refus de chaque champ, sans reprendre le
    /// texte saisi. Rend rien si un champ est refusé. La correction d'une offre les lit de même.
    /// </summary>
    internal static (string? Nom, ChampsDUneOffre Champs)? LireLesChamps(
        string? entreprise,
        string? poste,
        string? lieu,
        string? publieeLe,
        string? adresse,
        DateOnly aujourdhui,
        Dictionary<string, string[]> refus,
        bool dateJourEnPremierAdmise = false)
    {
        string? nom = null;
        if (!string.IsNullOrWhiteSpace(entreprise) && !Entreprise.EssayerDeLireLeNom(entreprise, out nom, out var refusDeLEntreprise))
        {
            refus[SaisieDUneOffre.Champs.Entreprise] = [refusDeLEntreprise];
        }

        if (!ChampsDUneOffre.EssayerDeLire(poste, lieu, publieeLe, adresse, aujourdhui, dateJourEnPremierAdmise, out var champs, out var refusDesChamps))
        {
            Ajouter(refus, SaisieDUneOffre.Champs.Poste, refusDesChamps.Poste);
            Ajouter(refus, SaisieDUneOffre.Champs.Lieu, refusDesChamps.Lieu);
            Ajouter(refus, SaisieDUneOffre.Champs.PublieeLe, refusDesChamps.PublieeLe);
            Ajouter(refus, SaisieDUneOffre.Champs.Adresse, refusDesChamps.Adresse);
            return null;
        }

        return refus.Count > 0 ? null : (nom, champs);
    }

    // Le texte est requis pour une offre collée ; une ligne de CSV s'en passe (#40).
    private static bool TexteRequis(SourceDOffre source) => source == SourceDOffre.TexteColle;

    // Au moins un champ, chacun une fois, chacun connu : sinon, le connecteur est faux (#98, point 16).
    private static void VerifierLesChampsDeLEmpreinte(IReadOnlyList<ChampDeLEmpreinte>? champs)
    {
        if (champs is null || champs.Count == 0 || champs.Distinct().Count() != champs.Count || !champs.All(Enum.IsDefined))
        {
            throw new ArgumentException("Un connecteur annonce au moins un champ de l'empreinte, chacun une fois.", nameof(champs));
        }
    }

    private static void Ajouter(Dictionary<string, string[]> refus, string champ, string? motif)
    {
        if (motif is not null)
        {
            refus[champ] = [motif];
        }
    }
}

/// <summary>
/// Une offre reçue que le cœur admet (<see cref="LectureDUneOffre"/>) : ses champs lus, son texte, et son empreinte. Elle
/// n'est pas encore dédoublonnée.
/// </summary>
/// <param name="Source">Sa source.</param>
/// <param name="NomDeLEntreprise">Le nom de son entreprise, tel que lu, ou rien pour une offre anonyme.</param>
/// <param name="Champs">Ses champs, admis.</param>
/// <param name="Texte">Son texte, retouché, ou rien.</param>
/// <param name="Empreinte">Son empreinte (<see cref="EmpreinteDUneOffre"/>).</param>
public sealed record OffreAdmise(SourceDOffre Source, string? NomDeLEntreprise, ChampsDUneOffre Champs, string? Texte, string Empreinte)
{
    /// <summary>La clé de son entreprise (<see cref="CleDEntreprise"/>), ou rien pour une offre anonyme.</summary>
    public string? CleDeLEntreprise => NomDeLEntreprise is null ? null : CleDEntreprise.De(NomDeLEntreprise);

    /// <summary>Son poste normalisé, que compare la règle 3.</summary>
    public string PosteNormalise => Domain.Offres.PosteNormalise.De(Champs.Poste);
}
