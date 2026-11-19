using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// L'horloge des tests : un instant fixé, et le fuseau de Windows en Belgique au lieu de celui du poste qui lance les
/// tests (les machines de la CI sont en UTC).
/// </summary>
public sealed class HorlogeDeTest(DateTimeOffset maintenant) : TimeProvider
{
    /// <summary>Le 18/11/2026 à 14:30, heure de Bruxelles.</summary>
    public static readonly DateTimeOffset Reference = new(2026, 11, 18, 13, 30, 0, TimeSpan.Zero);

    /// <summary>L'horloge arrêtée sur <see cref="Reference"/>.</summary>
    public HorlogeDeTest()
        : this(Reference)
    {
    }

    /// <summary>« Romance Standard Time », l'équivalent Windows d'Europe/Brussels.</summary>
    public static TimeZoneInfo Bruxelles { get; } = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    public override DateTimeOffset GetUtcNow() => maintenant;

    public override TimeZoneInfo LocalTimeZone => Bruxelles;

    /// <summary>Fait servir cette horloge par l'API lancée.</summary>
    public void Utiliser(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(this));
}
