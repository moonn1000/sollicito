using System.Diagnostics;
using System.Net;
using Sollicito.Api.Sauvegardes;
using Sollicito.Api.Tests.Outils;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using static Sollicito.Api.Tests.Outils.AppelsDeLImport;
using static Sollicito.Api.Tests.Outils.AppelsDesSauvegardes;

namespace Sollicito.Api.Tests;

/// <summary>
/// La copie avant l'import d'un fichier CSV (#40, ADR-0015 ; D10 de l'ADR-0001) : chaque import est précédé de la copie
/// d'avant une restauration (#28), vérifiée, listée et restaurable ; sans elle, rien n'est importé. L'aperçu et un import
/// refusé n'en font aucune, et l'import n'attend jamais une restauration qui tient les copies. Tout se passe dans des
/// dossiers temporaires, jamais dans celui de l'utilisateur ; données fictives.
/// </summary>
public sealed class CopieAvantLImportTests : IAsyncLifetime
{
    private static readonly DateOnly Le18 = new(2026, 11, 18);

    // Le titre du refus, et l'attente des copies par l'import, tels que l'API les fixe (RoutesDeLImport).
    private const string TitreDeLaCopieImpossible = "L'import n'est pas fait : la copie de la base, avant lui, n'a pas pu se faire.";
    private static readonly TimeSpan AttenteDeLaCopie = TimeSpan.FromSeconds(5);

    private static readonly byte[] Fichier = Csv("poste;entreprise;lieu\nChauffeur;Lumière Verte;Namur\nBrasseur;Brasserie Lune Rousse;Liège\n");

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private string ParDefaut => new DossierDeLaBase(_dossier).DossierDesSauvegardes;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await SupprimerAsync(_dossier);

    [Fact]
    public async Task Chaque_import_est_precede_d_une_copie_de_la_base_listee_et_restaurable()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        await api.CollerAvecSuccesAsync("Offre collée fictive.", "Comptable", entreprise: "Lumière Verte SA", lieu: "Liège");

        Assert.Equal(2, (await api.ImporterAvecSuccesAsync(Fichier))["comptes"]!["importees"]!.GetValue<int>());

        // La copie d'avant une restauration, vérifiée, porte la base d'avant l'import : la seule offre collée. Elle se
        // liste aussitôt, à part de la rotation.
        var copie = Assert.Single(api.Sauvegardes().Etat.Copies, gardee => gardee.Sorte == SorteDeCopie.AvantRestauration);
        Assert.Equal([copie.Nom], CopiesDAvant());
        Assert.Matches("^sollicito-avant-restauration-[0-9]{8}-[0-9]{9}\\.db$", copie.Nom);
        Assert.True(EstSaine(Path.Combine(ParDefaut, copie.Nom)));
        Assert.Equal(["Comptable"], OffresDe(Path.Combine(ParDefaut, copie.Nom)));

        // Un second import, au même instant de l'horloge, en fait une autre : aucune ne remplace une copie gardée.
        await api.ImporterAvecSuccesAsync(Csv("poste\nMagasinier\n"));
        Assert.Equal(2, CopiesDAvant().Length);
        Assert.Equal(["Comptable"], OffresDe(Path.Combine(ParDefaut, copie.Nom)));

        // Restaurée, la première rend la base d'avant le premier import.
        Assert.Equal(IssueDeLaRestauration.Restauree, await api.RestaurerAsync(api.Copie(copie.Nom)));
        Assert.Equal(1, await api.CompterLesOffresAsync());
    }

    [Fact]
    public async Task L_apercu_et_un_import_refuse_ne_font_aucune_copie()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();

        await api.ExaminerAvecSuccesAsync(Fichier);
        Assert.Equal(HttpStatusCode.BadRequest, await StatutAsync(api.ImporterAsync(Csv("lieu\nLiège\n"))));
        Assert.Equal(HttpStatusCode.BadRequest, await StatutAsync(api.ImporterAsync(Fichier, 9)));

        Assert.Empty(CopiesDAvant());
        Assert.DoesNotContain(api.Sauvegardes().Etat.Copies, gardee => gardee.Sorte == SorteDeCopie.AvantRestauration);
        Assert.Equal(0, await api.CompterLesOffresAsync());
    }

    [Fact]
    public async Task Sans_copie_possible_l_import_est_refuse_et_rien_n_est_ecrit()
    {
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest());
        await api.AttendreLaPremiereVerificationAsync();
        var entreprises = await api.EntreprisesEnBaseAsync();

        // Le dossier des sauvegardes par défaut remplacé par un fichier : la copie ne peut pas s'y écrire.
        var deCote = ParDefaut + "-de-cote";
        await DeplacerOuRetirerAsync(ParDefaut, deCote);
        await File.WriteAllBytesAsync(ParDefaut, [], TestContext.Current.CancellationToken);

        using (var reponse = await api.ImporterAsync(Fichier))
        {
            Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
            Assert.Equal(TitreDeLaCopieImpossible, (await AppelsDesCandidatures.LireAsync(reponse))["title"]!.GetValue<string>());
        }

        // Rien d'écrit : ni offre, ni entreprise, ni copie.
        Assert.Equal(0, await api.CompterLesOffresAsync());
        Assert.Equal(entreprises, await api.EntreprisesEnBaseAsync());
        Assert.Empty(CopiesDAvant(deCote));

        // Le dossier revenu, l'import se fait, précédé de sa copie.
        await DeplacerOuRetirerAsync(ParDefaut, null);
        await DeplacerOuRetirerAsync(deCote, ParDefaut);
        await api.ImporterAvecSuccesAsync(Fichier);
        Assert.Single(CopiesDAvant());
    }

    [Fact]
    public async Task L_import_n_attend_pas_une_restauration_qui_tient_les_copies_et_ne_la_retient_pas()
    {
        // La restauration s'arrête après sa copie de travail : elle tient le sémaphore des copies, porte encore ouverte.
        var etapes = new EtapesDeTest { Arret = EtapeDeLaRestauration.CopieDeTravailEcrite };
        await using var api = await LancerSurAsync(_dossier, new HorlogeDeTest(), AppelsDeLaRestauration.AvecLesEtapes(etapes));
        await api.AttendreLaPremiereVerificationAsync();
        await api.SauvegarderMaintenantAsync();
        var restauration = Task.Run(() => api.RestaurerAsync(api.Copie(api.CopieDuJour(Le18))), TestContext.Current.CancellationToken);
        await etapes.Atteinte.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // L'import, entré dans la porte, attend les copies moins longtemps que la restauration n'attendrait la porte.
        var debut = Stopwatch.GetTimestamp();
        Assert.Equal(HttpStatusCode.Conflict, await StatutAsync(api.ImporterAsync(Fichier)));
        var attente = Stopwatch.GetElapsedTime(debut);
        Assert.InRange(attente, AttenteDeLaCopie - TimeSpan.FromMilliseconds(100), RestaurationDeLaBase.DelaiDesRequetesEnCours);
        Assert.Equal(0, await api.CompterLesOffresAsync());

        // Relâchée, la restauration va au bout : l'import ne l'a pas retenue.
        etapes.Relacher();
        Assert.Equal(IssueDeLaRestauration.Restauree, await restauration);
    }

    // Les copies d'avant d'un dossier, le dossier par défaut si rien n'est dit.
    private string[] CopiesDAvant(string? dossier = null) =>
        [.. Fichiers(dossier ?? ParDefaut).Where(nom => nom.StartsWith("sollicito-avant-restauration-", StringComparison.Ordinal))];

    private static async Task<HttpStatusCode> StatutAsync(Task<HttpResponseMessage> envoi)
    {
        using var reponse = await envoi;
        return reponse.StatusCode;
    }
}
