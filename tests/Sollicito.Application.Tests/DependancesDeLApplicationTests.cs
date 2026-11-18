using Sollicito.Tests.Commun;

namespace Sollicito.Application.Tests;

/// <summary>
/// L'application s'appuie sur le domaine, jamais sur les couches extérieures (ADR-0001, D4).
/// </summary>
public sealed class DependancesDeLApplicationTests
{
    [Fact]
    public void L_application_s_appuie_sur_le_domaine_sans_dependre_de_l_infrastructure_ni_de_l_api()
    {
        var dependances = GrapheDesDependances.DependancesDe("Sollicito.Application");

        Assert.Contains("Sollicito.Domain", dependances);
        Assert.DoesNotContain("Sollicito.Infrastructure", dependances);
        Assert.DoesNotContain("Sollicito.Api", dependances);
    }
}
