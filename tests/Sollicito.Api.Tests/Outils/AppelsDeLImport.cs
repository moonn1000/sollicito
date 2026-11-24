using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Les appels de l'import d'un fichier CSV d'offres (#40) : l'aperçu et l'import, le fichier en base64 dans le corps,
/// comme l'écran l'envoie. Toutes les données sont fictives.
/// </summary>
public static class AppelsDeLImport
{
    /// <summary>La route de l'aperçu.</summary>
    public const string RouteDeLApercu = "/api/offres/import/apercu";

    /// <summary>La route de l'import.</summary>
    public const string RouteDeLImport = "/api/offres/import";

    /// <summary>Les octets d'un fichier, en UTF-8 sans BOM.</summary>
    public static byte[] Csv(string texte) => Encoding.UTF8.GetBytes(texte);

    /// <summary>Le corps de l'aperçu ou de l'import ; sans lignes gardées, le champ n'y paraît pas.</summary>
    public static string Corps(byte[] contenu, int[]? garder = null)
    {
        var corps = new JsonObject { ["contenu"] = Convert.ToBase64String(contenu) };
        if (garder is not null)
        {
            corps["garder"] = new JsonArray([.. garder.Select(numero => (JsonNode?)numero)]);
        }

        return corps.ToJsonString();
    }

    /// <summary>Demande l'aperçu d'un fichier.</summary>
    public static Task<HttpResponseMessage> ExaminerAsync(this ApiLancee api, byte[] contenu) =>
        api.EcrireAsync(HttpMethod.Post, RouteDeLApercu, Corps(contenu));

    /// <summary>Importe un fichier, en gardant les doublons possibles de ces lignes.</summary>
    public static Task<HttpResponseMessage> ImporterAsync(this ApiLancee api, byte[] contenu, params int[] garder) =>
        api.EcrireAsync(HttpMethod.Post, RouteDeLImport, Corps(contenu, garder));

    /// <summary>L'aperçu d'un fichier, qui doit se faire.</summary>
    public static async Task<JsonObject> ExaminerAvecSuccesAsync(this ApiLancee api, byte[] contenu)
    {
        using var reponse = await api.ExaminerAsync(contenu);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>L'import d'un fichier, qui doit se faire.</summary>
    public static async Task<JsonObject> ImporterAvecSuccesAsync(this ApiLancee api, byte[] contenu, params int[] garder)
    {
        using var reponse = await api.ImporterAsync(contenu, garder);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Le sort de chaque ligne d'un compte rendu, par numéro de ligne.</summary>
    public static Dictionary<int, string> Sorts(JsonObject compteRendu) =>
        compteRendu["lignes"]!.AsArray().ToDictionary(ligne => ligne!["ligne"]!.GetValue<int>(), ligne => ligne!["sort"]!.GetValue<string>());

    /// <summary>La ligne de ce numéro d'un compte rendu.</summary>
    public static JsonObject Ligne(JsonObject compteRendu, int numero) =>
        compteRendu["lignes"]!.AsArray().Single(ligne => ligne!["ligne"]!.GetValue<int>() == numero)!.AsObject();

    /// <summary>Le nombre d'offres de la base, lu sans passer par les routes.</summary>
    public static async Task<int> CompterLesOffresAsync(this ApiLancee api)
    {
        await using var portee = api.Services.CreateAsyncScope();
        return await portee.ServiceProvider.GetRequiredService<BaseDeSollicito>().Offres.CountAsync(TestContext.Current.CancellationToken);
    }
}
