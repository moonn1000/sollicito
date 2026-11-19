using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Sollicito.Api.Interface;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Des fichiers d'interface fictifs, dans un dossier temporaire : une page, un script, et un fichier rangé sous
/// <c>api/</c>, qui ne doit jamais être servi. Ils remplacent l'interface construite, que la fenêtre embarque.
/// </summary>
public sealed class InterfaceDeTest : IDisposable
{
    public const string Page = "<!doctype html><title>Interface de test</title>";
    public const string Script = "console.log(\"interface de test\");";
    public const string FichierSousApi = """{"fichier":"rangé sous api"}""";

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-interface-").FullName;

    public InterfaceDeTest()
    {
        File.WriteAllText(Path.Combine(_dossier, "index.html"), Page);
        Directory.CreateDirectory(Path.Combine(_dossier, "assets"));
        File.WriteAllText(Path.Combine(_dossier, "assets", "app.js"), Script);
        Directory.CreateDirectory(Path.Combine(_dossier, "api"));
        File.WriteAllText(Path.Combine(_dossier, "api", "fichier.json"), FichierSousApi);
        Fichiers = new PhysicalFileProvider(_dossier);
    }

    /// <summary>Le fournisseur des fichiers.</summary>
    public PhysicalFileProvider Fichiers { get; }

    /// <summary>Fait servir ces fichiers par l'API lancée, comme la fenêtre y fait servir l'interface.</summary>
    public void Servir(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton(new FichiersDeLInterface(Fichiers)));

    public void Dispose()
    {
        Fichiers.Dispose();
        Directory.Delete(_dossier, recursive: true);
    }
}

/// <summary>L'API de <c>Program</c>, lancée avec des fichiers d'interface fictifs.</summary>
public sealed class ApiAvecInterface : IAsyncLifetime
{
    private readonly InterfaceDeTest _interface = new();

    public ApiLancee Api { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Api = await ApiLancee.LancerAsync(_interface.Servir);

    public async ValueTask DisposeAsync()
    {
        if (Api is not null)
        {
            await Api.DisposeAsync();
        }

        _interface.Dispose();
    }
}
