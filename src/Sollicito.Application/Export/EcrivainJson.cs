using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sollicito.Application.Export;

/// <summary>
/// L'export en JSON (#49, ADR-0020 ; <c>docs/export.md</c>) : un seul document, UTF-8 sans BOM, indenté, qui garde chaque
/// valeur telle qu'en base, sans rien neutraliser. Son nom et sa version d'abord, puis un tableau par entité, dans l'ordre
/// des liens : entreprises, contacts, offres, candidatures, relances, événements. Chaque ligne s'écrit dès qu'elle est
/// lue, et le tampon se vide dans le flux dès qu'il passe <see cref="FormatDeLExport.Tampon"/> : ni une table ni le
/// document ne restent en mémoire.
/// </summary>
public static class EcrivainJson
{
    // Le fichier se lit tel quel, dans un éditeur ou par un autre outil, et ne s'insère jamais dans une page : les lettres
    // accentuées et les signes (« & », « + », « < ») s'écrivent tels quels. S'échappent les caractères que JSON impose (le
    // guillemet, la barre oblique inverse, les caractères de contrôle), et ceux que cet encodeur échappe toujours : les
    // espaces autres que l'espace ordinaire (dont les insécables U+00A0 et U+202F), U+2028, U+2029, U+FEFF, l'usage privé,
    // les caractères non assignés, et ceux hors du plan de base d'Unicode, comme un émoji (une paire de \uXXXX). L'encodeur
    // par défaut de .NET les
    // échapperait tous en \uXXXX, par prudence pour une page HTML : la valeur relue serait la même, mais illisible.
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Écrit l'export de cet instantané dans le flux, sans le fermer, puis rend le nombre de lignes de chaque entité.
    /// <paramref name="etapes"/> voit passer chaque table écrite.
    /// </summary>
    public static async Task<ComptesDeLExport> EcrireAsync(
        IInstantaneDeLaBase instantane,
        Stream sortie,
        DateTime exporteLe,
        Action<EtapeDeLExport>? etapes,
        CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(instantane);
        ArgumentNullException.ThrowIfNull(sortie);

        await using var json = new Utf8JsonWriter(sortie, Options);
        json.WriteStartObject();
        json.WriteString("format", FormatDeLExport.NomDuJson);
        json.WriteNumber("version", FormatDeLExport.Version);
        json.WriteString("exporteLe", FormatDeLExport.Instant(exporteLe));

        var entreprises = await TableauAsync(json, "entreprises", instantane.Entreprises(annulation), Ecrire, annulation);
        etapes?.Invoke(EtapeDeLExport.EntreprisesEcrites);
        var contacts = await TableauAsync(json, "contacts", instantane.Contacts(annulation), Ecrire, annulation);
        etapes?.Invoke(EtapeDeLExport.ContactsEcrits);
        var offres = await TableauAsync(json, "offres", instantane.Offres(annulation), Ecrire, annulation);
        etapes?.Invoke(EtapeDeLExport.OffresEcrites);
        var candidatures = await TableauAsync(json, "candidatures", instantane.Candidatures(annulation), Ecrire, annulation);
        etapes?.Invoke(EtapeDeLExport.CandidaturesEcrites);
        var relances = await TableauAsync(json, "relances", instantane.Relances(annulation), Ecrire, annulation);
        etapes?.Invoke(EtapeDeLExport.RelancesEcrites);
        var evenements = await TableauAsync(json, "evenements", instantane.Evenements(annulation), Ecrire, annulation);
        etapes?.Invoke(EtapeDeLExport.EvenementsEcrits);

        json.WriteEndObject();
        await json.FlushAsync(annulation);
        return new ComptesDeLExport(entreprises, contacts, offres, candidatures, relances, evenements);
    }

    private static async Task<int> TableauAsync<T>(
        Utf8JsonWriter json,
        string nom,
        IAsyncEnumerable<T> lignes,
        Action<Utf8JsonWriter, T> ecrire,
        CancellationToken annulation)
    {
        json.WriteStartArray(nom);
        var nombre = 0;
        await foreach (var ligne in lignes.WithCancellation(annulation))
        {
            ecrire(json, ligne);
            nombre++;
            if (json.BytesPending >= FormatDeLExport.Tampon)
            {
                await json.FlushAsync(annulation);
            }
        }

        json.WriteEndArray();
        return nombre;
    }

    private static void Ecrire(Utf8JsonWriter json, EntrepriseExportee entreprise)
    {
        json.WriteStartObject();
        json.WriteNumber("id", entreprise.Id);
        json.WriteString("nom", entreprise.Nom);
        json.WriteString("cle", entreprise.Cle);
        json.WriteEndObject();
    }

    private static void Ecrire(Utf8JsonWriter json, ContactExporte contact)
    {
        json.WriteStartObject();
        json.WriteNumber("id", contact.Id);
        json.WriteNumber("entrepriseId", contact.EntrepriseId);
        json.WriteString("nom", contact.Nom);
        json.WriteString("email", contact.Email);
        json.WriteEndObject();
    }

    private static void Ecrire(Utf8JsonWriter json, OffreExportee offre)
    {
        json.WriteStartObject();
        json.WriteNumber("id", offre.Id);
        json.WriteString("source", offre.Source);
        Nombre(json, "entrepriseId", offre.EntrepriseId);
        json.WriteString("poste", offre.Poste);
        json.WriteString("posteNormalise", offre.PosteNormalise);
        Texte(json, "lieu", offre.Lieu);
        Date(json, "publieeLe", offre.PublieeLe);
        Texte(json, "adresse", offre.Adresse);
        Texte(json, "texte", offre.Texte);
        json.WriteString("empreinte", offre.Empreinte);
        json.WriteString("ajouteeLe", FormatDeLExport.Instant(offre.AjouteeLe));
        json.WriteEndObject();
    }

    private static void Ecrire(Utf8JsonWriter json, CandidatureExportee candidature)
    {
        json.WriteStartObject();
        json.WriteNumber("id", candidature.Id);
        json.WriteNumber("entrepriseId", candidature.EntrepriseId);
        Nombre(json, "offreId", candidature.OffreId);
        json.WriteString("poste", candidature.Poste);
        json.WriteString("envoyeeLe", FormatDeLExport.Instant(candidature.EnvoyeeLe));
        json.WriteString("statut", candidature.Statut);
        json.WriteEndObject();
    }

    private static void Ecrire(Utf8JsonWriter json, RelanceExportee relance)
    {
        json.WriteStartObject();
        json.WriteNumber("id", relance.Id);
        json.WriteNumber("candidatureId", relance.CandidatureId);
        Nombre(json, "destinataireId", relance.DestinataireId);
        json.WriteString("echeance", FormatDeLExport.Date(relance.Echeance));
        json.WriteString("etat", relance.Etat);
        json.WriteEndObject();
    }

    // Un événement porte ses champs communs, puis ceux de sa sorte : une sorte que Sollicito n'écrit pas ne s'exporte pas.
    private static void Ecrire(Utf8JsonWriter json, EvenementExporte evenement)
    {
        json.WriteStartObject();
        json.WriteNumber("id", evenement.Id);
        json.WriteNumber("candidatureId", evenement.CandidatureId);
        json.WriteString("sorte", evenement.Sorte);
        json.WriteString("survenuLe", FormatDeLExport.Instant(evenement.SurvenuLe));
        switch (evenement.Sorte)
        {
            case FormatDeLExport.ChangementDeStatut:
                Texte(json, "ancien", evenement.Ancien);
                Texte(json, "nouveau", evenement.Nouveau);
                Nombre(json, "corrigeId", evenement.CorrigeId);
                break;
            case FormatDeLExport.Etape:
                Texte(json, "type", evenement.Type);
                Texte(json, "note", evenement.Note);
                Nombre(json, "corrigeId", evenement.EtapeCorrigeeId);
                break;
            case FormatDeLExport.ClotureDeRelance:
                Nombre(json, "relanceId", evenement.RelanceId);
                break;
            default:
                throw new InvalidDataException("Un événement de la base est d'une sorte que Sollicito n'écrit pas.");
        }

        json.WriteEndObject();
    }

    private static void Texte(Utf8JsonWriter json, string nom, string? valeur)
    {
        if (valeur is null)
        {
            json.WriteNull(nom);
        }
        else
        {
            json.WriteString(nom, valeur);
        }
    }

    private static void Nombre(Utf8JsonWriter json, string nom, int? valeur)
    {
        if (valeur is { } nombre)
        {
            json.WriteNumber(nom, nombre);
        }
        else
        {
            json.WriteNull(nom);
        }
    }

    private static void Date(Utf8JsonWriter json, string nom, DateOnly? valeur)
    {
        if (valeur is { } date)
        {
            json.WriteString(nom, FormatDeLExport.Date(date));
        }
        else
        {
            json.WriteNull(nom);
        }
    }
}
