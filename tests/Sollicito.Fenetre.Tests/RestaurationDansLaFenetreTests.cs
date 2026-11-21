using System.Text.Json;
using Sollicito.Application.Sauvegardes;

namespace Sollicito.Fenetre.Tests;

/// <summary>
/// La restauration vue de la fenêtre (#28, ADR-0012) : le message de la page ne porte que l'identifiant d'une copie, dans
/// une forme stricte, jamais un chemin ; la réponse ne dit que l'issue ; le dialogue montre la date de la copie et ce qui
/// sera perdu, et ne promet un retour que si la copie d'avant se restaure.
/// </summary>
public sealed class RestaurationDansLaFenetreTests
{
    private static readonly Uri Origine = new("http://127.0.0.1:52344");

    [Theory]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef"}""")]
    [InlineData("http://127.0.0.1:52344/index.html", """{ "copie": "0123456789abcdef", "type": "restaurerUneSauvegarde" }""")]
    public void L_interface_demande_la_restauration_d_une_copie_par_son_seul_identifiant(string source, string message)
    {
        Assert.True(ReglesDeLaFenetre.DemandeLaRestauration(source, message, Origine, out var copie));
        Assert.Equal("0123456789abcdef", copie);
        // Ce message n'ouvre aucun autre dialogue, ni ne reçoit le jeton.
        Assert.False(ReglesDeLaFenetre.DemandeLeChoixDuDossier(source, message, Origine));
        Assert.False(ReglesDeLaFenetre.DemandeLeJeton(source, message, Origine));
    }

    [Theory]
    [InlineData("http://localhost:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef"}""")]
    [InlineData("https://exemple.invalid/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef"}""")]
    [InlineData(null, """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef","chemin":"C:\\Sauvegardes\\copie.db"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef","copie":"fedcba9876543210"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","type":"restaurerUneSauvegarde"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789ABCDEF"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcde"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"0123456789abcdef0"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"C:\\Sauvegardes\\sollicito-1a2b3c4d-2026-11-18.db"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":"sollicito-1a2b3c4d-2026-11-18.db"}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":1234567890123456}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurerUneSauvegarde","copie":["0123456789abcdef"]}""")]
    [InlineData("http://127.0.0.1:52344/", """{"type":"restaurer","copie":"0123456789abcdef"}""")]
    [InlineData("http://127.0.0.1:52344/", "{pas du json")]
    [InlineData("http://127.0.0.1:52344/", "")]
    [InlineData("http://127.0.0.1:52344/", null)]
    public void Aucun_autre_message_ne_demande_une_restauration(string? source, string? message)
    {
        Assert.False(ReglesDeLaFenetre.DemandeLaRestauration(source, message, Origine, out _));
    }

    [Theory]
    [InlineData("EnCours", "enCours")]
    [InlineData("Annulee", "annulee")]
    [InlineData("Restauree", "restauree")]
    [InlineData("RestaureeDepuisUneBaseAbimee", "restaureeDepuisUneBaseAbimee")]
    [InlineData("Abimee", "abimee")]
    [InlineData("Etrangere", "etrangere")]
    [InlineData("PlusRecente", "plusRecente")]
    [InlineData("Introuvable", "introuvable")]
    [InlineData("Changee", "changee")]
    [InlineData("BaseChangee", "baseChangee")]
    [InlineData("Occupee", "occupee")]
    [InlineData("CopieDAvantImpossible", "copieDAvantImpossible")]
    [InlineData("Impossible", "impossible")]
    public void La_reponse_ne_porte_que_la_copie_et_l_issue(string issue, string attendue)
    {
        using var message = JsonDocument.Parse(ReglesDeLaFenetre.MessageDeLaRestauration("0123456789abcdef", Enum.Parse<IssueDansLaFenetre>(issue)));

        Assert.Equal(["type", "copie", "issue"], message.RootElement.EnumerateObject().Select(champ => champ.Name));
        Assert.Equal("restauration", message.RootElement.GetProperty("type").GetString());
        Assert.Equal("0123456789abcdef", message.RootElement.GetProperty("copie").GetString());
        Assert.Equal(attendue, message.RootElement.GetProperty("issue").GetString());
    }

    [Fact]
    public void Chaque_issue_d_une_restauration_a_la_sienne_dans_la_fenetre()
    {
        Assert.All(Enum.GetValues<IssueDeLaRestauration>(), issue =>
            Assert.Equal(issue.ToString(), ReglesDeLaFenetre.DansLaFenetre(issue).ToString()));
    }

    [Fact]
    public void Le_dialogue_montre_la_date_de_la_copie_ce_qui_sera_perdu_et_le_retour_possible()
    {
        var question = TextesDeLaRestauration.Question(Demande(SorteDeCopie.DuJour, EtatDeLaBase.Saine), Bruxelles);

        Assert.Equal("Restaurer la copie du 18 novembre 2026 à 21:04 ?", question.Titre);
        Assert.Equal(
            "Votre base sera remplacée par cette copie : tout ce que vous avez saisi ou modifié depuis sera perdu. Sollicito "
            + "garde d'abord une copie de votre base actuelle : vous pourrez y revenir depuis les réglages.",
            question.Texte);
    }

    [Fact]
    public void Devant_une_base_abimee_le_dialogue_ne_promet_aucun_retour()
    {
        var question = TextesDeLaRestauration.Question(Demande(SorteDeCopie.AvantMiseAJour, EtatDeLaBase.Abimee), Bruxelles);

        Assert.Equal(
            "Votre base actuelle est abîmée : elle sera remplacée par cette copie, et tout ce que vous avez saisi ou modifié "
            + "depuis sera perdu. Sollicito garde d'abord ses fichiers tels quels, dans le dossier des sauvegardes par défaut, "
            + "mais ne pourra pas les restaurer.",
            question.Texte);
        Assert.DoesNotContain("y revenir", question.Texte, StringComparison.Ordinal);
    }

    [Fact]
    public void La_copie_d_une_autre_installation_le_dit()
    {
        var question = TextesDeLaRestauration.Question(Demande(SorteDeCopie.DuJourDUneAutreInstallation, EtatDeLaBase.Saine), Bruxelles);

        Assert.EndsWith(
            "\n\nCette copie vient d'une autre installation de Sollicito : un autre poste, ou celui-ci avant une réinstallation.",
            question.Texte,
            StringComparison.Ordinal);
    }

    [Fact]
    public void La_date_s_ecrit_en_francais_a_l_heure_du_poste_premier_du_mois_compris()
    {
        Assert.Equal("1er décembre 2026 à 00:30", TextesDeLaRestauration.DateEtHeure(new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc), Bruxelles));
    }

    private static TimeZoneInfo Bruxelles => TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    private static DemandeDeRestauration Demande(SorteDeCopie sorte, EtatDeLaBase etat) =>
        new("0123456789abcdef", "sollicito-1a2b3c4d-2026-11-18.db", LieuDUneCopie.DossierDesCopies, sorte, new DateTime(2026, 11, 18, 20, 4, 0, DateTimeKind.Utc), etat);
}
