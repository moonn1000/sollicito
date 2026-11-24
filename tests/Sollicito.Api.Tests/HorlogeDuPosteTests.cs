using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Api.Tests.Outils;

namespace Sollicito.Api.Tests;

/// <summary>
/// L'horloge que l'API sert quand rien ne la remplace : celle du poste (#80, point 2). Les tests de comportement de
/// l'API remplacent l'horloge par <see cref="HorlogeDeTest"/>, au fuseau fixé ; ceux qui gardent la vraie horloge
/// (jetons, écoute, protections, journaux…) n'en lisent rien : aucun ne verrait une API qui lirait l'heure saisie en
/// UTC au lieu de l'heure du poste.
/// </summary>
public sealed class HorlogeDuPosteTests
{
    [Fact]
    public async Task L_api_lancee_sans_horloge_de_test_sert_l_horloge_du_poste()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });

        var horloge = api.Services.GetRequiredService<TimeProvider>();

        // TimeProvider.System rend le fuseau du poste : une autre horloge, même de fuseau UTC, serait refusée ici.
        Assert.Same(TimeProvider.System, horloge);
    }

    [Fact]
    public void Sur_la_CI_le_fuseau_du_poste_n_est_pas_UTC()
    {
        // Seule la CI règle son fuseau (ci.yml) : sur un poste, il est celui de l'utilisateur, UTC compris.
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CI") == "true", "Le fuseau ne se règle que dans la CI.");

        // Sans cela, les deux tests de l'horloge passeraient aussi sur un runner en UTC, et l'étape de ci.yml pourrait
        // disparaître sans bruit.
        var saisie = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Unspecified);
        Assert.NotEqual(TimeSpan.Zero, TimeZoneInfo.Local.GetUtcOffset(saisie));
    }

    [Fact]
    public async Task Une_heure_saisie_se_lit_dans_le_fuseau_du_poste_avec_l_horloge_reelle()
    {
        await using var api = await ApiLancee.LancerAsync(_ => { });
        var saisie = new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Unspecified);
        var attendu = TimeZoneInfo.ConvertTimeToUtc(saisie, TimeZoneInfo.Local);

        var candidature = await api.EnregistrerAvecSuccesAsync("Lumière Verte SA", "Comptable", "2026-11-10T14:00");

        // 14:00 à Bruxelles donne 13:00Z ; sur un poste en UTC, 14:00Z. Une API qui lirait l'heure en UTC la rendrait
        // 14:00Z partout, et ce test, sur un poste hors UTC, la verrait.
        Assert.Equal(
            attendu.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            candidature["envoyeeLe"]!.GetValue<string>());
    }
}
