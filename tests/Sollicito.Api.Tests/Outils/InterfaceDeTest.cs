using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Sollicito.Api.Interface;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Une interface fictive (<c>Outils/Interface</c>) : une page, un script, et un fichier rangé sous <c>api/</c>, qui
/// ne doit jamais être servi. Elle remplace l'interface construite, que la fenêtre embarque. Le fournisseur embarqué
/// est celui de l'exe ; le fournisseur sur le disque lit les chemins autrement (un antislash y sépare deux dossiers).
/// </summary>
public sealed class InterfaceDeTest : IDisposable
{
    public const string Page = "<!doctype html><title>Interface de test</title>";
    public const string Script = "console.log(\"interface de test\");";
    public const string FichierSousApi = """{"fichier":"rangé sous api"}""";

    /// <summary>L'interface fictive, embarquée dans l'assembly des tests ou lue sur le disque.</summary>
    public InterfaceDeTest(bool embarquee = false)
    {
        Fichiers = embarquee
            ? new ManifestEmbeddedFileProvider(typeof(InterfaceDeTest).Assembly, "Outils/Interface")
            : new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "Outils", "Interface"));
    }

    /// <summary>Le fournisseur des fichiers.</summary>
    public IFileProvider Fichiers { get; }

    /// <summary>Fait servir ces fichiers par l'API lancée, comme la fenêtre y fait servir l'interface.</summary>
    public void Servir(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton(new FichiersDeLInterface(Fichiers)));

    public void Dispose() => (Fichiers as IDisposable)?.Dispose();
}

/// <summary>L'API de <c>Program</c>, lancée avec l'interface fictive lue sur le disque.</summary>
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

/// <summary>L'API de <c>Program</c>, lancée avec l'interface fictive embarquée, comme dans l'exe de la fenêtre.</summary>
public sealed class ApiAvecInterfaceEmbarquee : IAsyncLifetime
{
    private readonly InterfaceDeTest _interface = new(embarquee: true);

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
