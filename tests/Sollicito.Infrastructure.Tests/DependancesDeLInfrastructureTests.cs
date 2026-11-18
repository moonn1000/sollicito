using Sollicito.Tests.Commun;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// L'infrastructure implémente ce que demande l'application, sans dépendre de l'API (ADR-0001, D4).
/// </summary>
public sealed class DependancesDeLInfrastructureTests
{
    [Fact]
    public void L_infrastructure_s_appuie_sur_l_application_sans_dependre_de_l_api()
    {
        var dependances = GrapheDesDependances.DependancesDe("Sollicito.Infrastructure");

        Assert.Contains("Sollicito.Application", dependances);
        Assert.DoesNotContain("Sollicito.Api", dependances);
    }
}
