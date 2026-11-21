using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// Les restes d'un arrêt brutal (#26, ADR-0008) : le compte que le journal en donne ne dit retiré que ce qui a disparu, et
/// une copie de travail avec ses journaux ne compte que pour un. Noms fictifs.
/// </summary>
public sealed class RestesDUnArretTests : IDisposable
{
    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

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
    public async Task Une_copie_et_ses_journaux_comptent_pour_un_et_un_reste_tenu_ne_compte_pas()
    {
        var copieDeTravail = RestesDUnArret.NomDUneCopieDeTravail();
        string[] restes =
        [
            copieDeTravail,
            copieDeTravail + "-journal",
            copieDeTravail + "-wal",
            "reglages.json.0123456789abcdef0123456789abcdef.provisoire",
            "sauvegardes.json.fedcba9876543210fedcba9876543210.provisoire",
        ];
        foreach (var nom in restes)
        {
            await File.WriteAllTextAsync(Path.Combine(_dossier, nom), "fictif", TestContext.Current.CancellationToken);
        }

        // Un autre programme tient l'un des fichiers provisoires : il reste, et ne compte pas parmi les restes retirés.
        var tenu = Path.Combine(_dossier, restes[4]);
        int retires;
        using (new FileStream(tenu, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            retires = await RestesDUnArret.RetirerAsync(new DossierDeLaBase(_dossier));
        }

        Assert.Equal(2, retires);
        Assert.True(File.Exists(tenu));
        Assert.Equal([Path.GetFileName(tenu)], Directory.EnumerateFiles(_dossier).Select(Path.GetFileName));
    }
}
