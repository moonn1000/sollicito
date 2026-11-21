using System.Net;
using System.Text.Json.Nodes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Domain.Contacts;
using static Sollicito.Api.Tests.Outils.AppelsDesRelances;

namespace Sollicito.Api.Tests;

/// <summary>
/// Les contacts d'une entreprise, par l'API sur une vraie base SQLite (#14, absorbe #30) : ajouter, lister, modifier,
/// supprimer ; des refus qui ne reprennent pas le texte saisi ; un contact supprimé qui quitte ses relances, à faire
/// comme closes. Toutes les données sont fictives.
/// </summary>
public sealed class ContactsTests
{
    [Fact]
    public async Task Un_contact_s_ajoute_a_son_entreprise_et_se_relit()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var entreprise = await EntrepriseAsync(api);

        var jeanne = await api.AjouterUnContactAvecSuccesAsync(entreprise, "  Jeanne Martin ", " jeanne.martin@exemple.invalid ");
        var paul = await api.AjouterUnContactAvecSuccesAsync(entreprise, "Paul Lambert", "paul@exemple.invalid");

        Assert.Equal("Jeanne Martin", jeanne["nom"]!.GetValue<string>());
        Assert.Equal("jeanne.martin@exemple.invalid", jeanne["email"]!.GetValue<string>());
        Assert.Equal([jeanne.ToJsonString(), paul.ToJsonString()], (await api.ContactsAsync(entreprise)).Select(contact => contact!.ToJsonString()));
        Assert.Empty(await api.ContactsAsync(await EntrepriseAsync(api, "Brasserie Lune Rousse")));
    }

    [Fact]
    public async Task Les_refus_des_champs_n_ajoutent_rien_et_ne_reprennent_pas_le_texte_saisi()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var entreprise = await EntrepriseAsync(api);

        foreach (var (nom, email, refus) in new (string?, string?, (string Champ, string Message)[])[]
        {
            (null, "jeanne@exemple.invalid", [("nom", "Indiquez le nom du contact.")]),
            ("Jeanne", null, [("email", AdresseElectronique.Manquante)]),
            ("Jeanne", "jeanne@exemple.invalid,pirate@exemple.invalid", [("email", AdresseElectronique.Illisible)]),
            ("Jeanne", "jeanne@exemple.invalid?bcc=pirate@exemple.invalid", [("email", AdresseElectronique.Illisible)]),
            ("Jeanne", new string('a', 250) + "@exemple.invalid", [("email", AdresseElectronique.TropLongue)]),
            ("Jeanne\nMartin", "pas-une-adresse", [("nom", "Le nom du contact ne doit contenir ni saut de ligne, ni tabulation, ni autre caractère de contrôle ou invalide."), ("email", AdresseElectronique.Illisible)]),
        })
        {
            using var reponse = await api.AjouterUnContactAsync(entreprise, nom, email);

            Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
            var texte = await reponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var corps = JsonNode.Parse(texte)!;
            Assert.Equal("Le contact n'est pas enregistré : corrigez les champs signalés.", corps["title"]!.GetValue<string>());
            Assert.Equal(refus.Select(r => r.Champ), corps["errors"]!.AsObject().Select(champ => champ.Key));
            Assert.Equal(refus.Select(r => r.Message), corps["errors"]!.AsObject().Select(champ => champ.Value![0]!.GetValue<string>()));
            Assert.DoesNotContain("pirate", texte, StringComparison.Ordinal);
            Assert.DoesNotContain("pas-une-adresse", texte, StringComparison.Ordinal);
        }

        Assert.Equal(0, await api.CompterLesContactsAsync());
    }

    [Theory]
    [InlineData("""{"nom":"Jeanne","email":"jeanne@exemple.invalid","fonction":"RH"}""")]
    [InlineData("""{"nom":"Jeanne","email":["jeanne@exemple.invalid"]}""")]
    [InlineData("""{"nom":"Jeanne","email":"jeanne@exemple.invalid","entreprise":2}""")]
    public async Task Un_corps_illisible_n_ajoute_rien(string corps)
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var entreprise = await EntrepriseAsync(api);

        using var reponse = await api.EcrireAsync(HttpMethod.Post, $"/api/entreprises/{Texte(entreprise)}/contacts", corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(0, await api.CompterLesContactsAsync());
    }

    [Fact]
    public async Task Une_entreprise_ou_un_contact_inconnu_rend_404()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);

        using var liste = await api.EnvoyerAsync(HttpMethod.Get, "/api/entreprises/42/contacts", jeton: true);
        using var ajout = await api.AjouterUnContactAsync(42, "Jeanne", "jeanne@exemple.invalid");
        using var modification = await api.ModifierLeContactAsync(42, "Jeanne", "jeanne@exemple.invalid");
        using var suppression = await api.SupprimerLeContactAsync(42);

        Assert.Equal(HttpStatusCode.NotFound, liste.StatusCode);
        Assert.Equal("Cette entreprise n'existe pas.", (await AppelsDesCandidatures.LireAsync(ajout))["title"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, ajout.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, modification.StatusCode);
        Assert.Equal("Ce contact n'existe pas.", (await AppelsDesCandidatures.LireAsync(modification))["title"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, suppression.StatusCode);
        Assert.Equal(0, await api.CompterLesContactsAsync());
    }

    [Fact]
    public async Task Un_contact_modifie_se_montre_ainsi_dans_ses_relances_closes_comprises()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, entreprise) = await CandidatureAsync(api);
        var contact = Id(await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid"));
        var relance = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, destinataire: contact)));
        using (var faite = await api.MarquerFaiteAsync(candidature, relance))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        using var reponse = await api.ModifierLeContactAsync(contact, "Jeanne Dupont", "jeanne.dupont@exemple.invalid");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var modifie = (await AppelsDesCandidatures.LireAsync(reponse)).ToJsonString();
        Assert.Equal(new JsonObject { ["id"] = contact, ["nom"] = "Jeanne Dupont", ["email"] = "jeanne.dupont@exemple.invalid" }.ToJsonString(), modifie);
        var fiche = await api.FicheAsync(candidature);
        Assert.Equal(modifie, fiche["relances"]![0]!["destinataire"]!.ToJsonString());
        Assert.Equal(modifie, fiche["historique"]![0]!["destinataire"]!.ToJsonString());

        using var refusee = await api.ModifierLeContactAsync(contact, "", "jeanne");
        Assert.Equal(HttpStatusCode.BadRequest, refusee.StatusCode);
        Assert.Equal("Le contact n'est pas modifié : corrigez les champs signalés.", (await AppelsDesCandidatures.LireAsync(refusee))["title"]!.GetValue<string>());
        Assert.Equal(modifie, (await api.ContactsAsync(entreprise))[0]!.ToJsonString());
    }

    [Fact]
    public async Task Un_contact_supprime_quitte_ses_relances_a_faire_comme_closes()
    {
        await using var api = await ApiLancee.LancerAsync(new HorlogeDeTest().Utiliser);
        var (candidature, entreprise) = await CandidatureAsync(api);
        var contact = Id(await api.AjouterUnContactAvecSuccesAsync(entreprise, "Jeanne Martin", "jeanne@exemple.invalid"));
        var gardee = Id(await api.AjouterUnContactAvecSuccesAsync(entreprise, "Paul Lambert", "paul@exemple.invalid"));
        var close = Id(RelanceAFaire(await api.PlanifierAvecSuccesAsync(candidature, destinataire: contact)));
        using (var faite = await api.MarquerFaiteAsync(candidature, close))
        {
            Assert.Equal(HttpStatusCode.OK, faite.StatusCode);
        }

        await api.PlanifierAvecSuccesAsync(candidature, delai: 14, destinataire: contact);

        using var reponse = await api.SupprimerLeContactAsync(contact);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.Equal([gardee], (await api.ContactsAsync(entreprise)).Select(Id));
        var fiche = await api.FicheAsync(candidature);
        Assert.All(fiche["relances"]!.AsArray(), relance => Assert.Null(relance!["destinataire"]));
        Assert.Equal(2, fiche["relances"]!.AsArray().Count);
        Assert.Null(fiche["historique"]![0]!["destinataire"]);

        using var deuxFois = await api.SupprimerLeContactAsync(contact);
        Assert.Equal(HttpStatusCode.NotFound, deuxFois.StatusCode);
    }

    private static async Task<int> EntrepriseAsync(ApiLancee api, string nom = "Lumière Verte SA") =>
        (await CandidatureAsync(api, nom)).Entreprise;

    private static async Task<(int Candidature, int Entreprise)> CandidatureAsync(ApiLancee api, string nom = "Lumière Verte SA")
    {
        var candidature = await api.EnregistrerAvecSuccesAsync(nom, "Comptable", "2026-11-10T09:00");
        return (AppelsDuSuivi.IdDe(candidature), Id(candidature["entreprise"]));
    }
}
