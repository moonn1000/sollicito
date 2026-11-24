using System.Text;
using Sollicito.Application.Sauvegardes;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// Le fichier des réglages, <c>reglages.json</c> (ADR-0008) : une lecture ratée ne fait pas oublier les autres réglages
/// (#88, point 7), et sa taille est bornée (#88, point 13). Chemins et valeurs fictifs.
/// </summary>
public sealed class FichierDesReglagesTests : IDisposable
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private DossierDeLaBase Base => new(_dossier);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dossier, recursive: true);
        }
        catch (IOException)
        {
            // Le dossier temporaire reste : le système le videra.
        }
    }

    [Fact]
    public async Task Un_fichier_tenu_un_instant_par_un_autre_programme_ne_fait_pas_oublier_les_autres_reglages()
    {
        var fichier = new FichierDesReglages(Base);
        await File.WriteAllTextAsync(Base.FichierDesReglages, """{"delaiDesRelances": 14}""", TestContext.Current.CancellationToken);
        var choisi = Path.Combine(_dossier, "Copies");
        Task ecriture;

        using (new FileStream(Base.FichierDesReglages, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // L'écriture commence sous le verrou du fichier : sa première lecture se heurte à lui. Un antivirus qui le tient
            // un instant ne le rend pas « illisible » : l'écriture attend, puis lit.
            ecriture = fichier.EcrireLeDossierAsync(choisi);
            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        await ecriture;
        var lus = fichier.Lire();
        Assert.Equal(choisi, lus.Reglages.DossierDesSauvegardes);
        Assert.Equal(14, lus.Reglages.DelaiDesRelances);
        Assert.True(lus.FichierLisible);
    }

    [Fact]
    public async Task Un_fichier_tenu_et_jamais_rendu_ne_recoit_pas_le_delai_des_relances()
    {
        var fichier = new FichierDesReglages(Base);
        await File.WriteAllTextAsync(Base.FichierDesReglages, """{"dossierDesSauvegardes": "E:\\Copies"}""", TestContext.Current.CancellationToken);

        bool ecrit;
        using (new FileStream(Base.FichierDesReglages, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ecrit = await fichier.EcrireLeDelaiDesRelancesAsync(30);
        }

        // Un fichier présent qui ne se lit pas n'est jamais réécrit par ce réglage : il perdrait le dossier choisi.
        Assert.False(ecrit);
        Assert.Equal("E:\\Copies", fichier.Lire().Reglages.DossierDesSauvegardes);
    }

    [Fact]
    public async Task Un_fichier_de_plus_de_64_kio_ne_se_lit_pas_mais_un_fichier_de_64_kio_se_lit()
    {
        var fichier = new FichierDesReglages(Base);

        await File.WriteAllTextAsync(Base.FichierDesReglages, AvecUneTaille(ReglagesDeLApplication.TailleMaximale), TestContext.Current.CancellationToken);
        var auMaximum = fichier.Lire();
        await File.WriteAllTextAsync(Base.FichierDesReglages, AvecUneTaille(ReglagesDeLApplication.TailleMaximale + 1), TestContext.Current.CancellationToken);
        var audessus = fichier.Lire();

        Assert.True(auMaximum.FichierLisible);
        Assert.Equal(14, auMaximum.Reglages.DelaiDesRelances);
        // Trop gros : lu comme un fichier illisible, les réglages par défaut s'appliquent, et l'alerte le dit.
        Assert.False(audessus.FichierLisible);
        Assert.False(audessus.Lisible);
        Assert.Null(audessus.Reglages.DelaiDesRelances);
    }

    // Un objet JSON d'exactement cette taille en octets, un réglage valide en tête et un remplissage ensuite.
    private static string AvecUneTaille(int octets)
    {
        const string Debut = """{"delaiDesRelances":14,"remplissage":"x""";
        const string Fin = "\"}";
        var remplissage = new string('x', octets - Encoding.UTF8.GetByteCount(Debut) - Encoding.UTF8.GetByteCount(Fin));
        var texte = Debut + remplissage + Fin;
        Assert.Equal(octets, Encoding.UTF8.GetByteCount(texte));
        return texte;
    }
}
