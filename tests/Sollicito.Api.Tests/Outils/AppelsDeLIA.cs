using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Sollicito.Api.Tests.Outils;

/// <summary>Les appels du réglage de l'IA (#43), et la recherche d'une clé fictive dans les fichiers.</summary>
public static class AppelsDeLIA
{
    /// <summary>Une clé de Claude fictive, reconnaissable.</summary>
    public const string CleDeClaude = "sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0001";

    /// <summary>Une autre clé de Claude fictive.</summary>
    public const string AutreCleDeClaude = "sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0002";

    /// <summary>Une clé d'OpenAI fictive, reconnaissable.</summary>
    public const string CleDOpenAI = "sk-proj-SOLLICITO-ESSAI-43-cle-fictive-d-openai-0001";

    /// <summary>La partie reconnaissable de toutes les clés fictives.</summary>
    public const string PartieReconnaissable = "SOLLICITO-ESSAI-43";

    /// <summary>L'adresse du réglage de l'IA.</summary>
    public const string Adresse = "/api/reglages/ia";

    /// <summary>L'adresse de la clé.</summary>
    public const string AdresseDeLaCle = "/api/reglages/ia/cle";

    /// <summary>L'état de l'IA, lu comme la section « IA » le lit.</summary>
    public static async Task<JsonObject> EtatDeLIAAsync(this ApiLancee api)
    {
        using var reponse = await api.EnvoyerAsync(HttpMethod.Get, Adresse, jeton: true);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Choisit un fournisseur, par son nom.</summary>
    public static Task<HttpResponseMessage> ChoisirLIAAsync(this ApiLancee api, string? fournisseur) =>
        api.EcrireAsync(HttpMethod.Put, Adresse, new JsonObject { ["fournisseur"] = fournisseur }.ToJsonString());

    /// <summary>Choisit un fournisseur, et rend l'état qui suit.</summary>
    public static async Task<JsonObject> ChoisirLIAAvecSuccesAsync(this ApiLancee api, string fournisseur)
    {
        using var reponse = await api.ChoisirLIAAsync(fournisseur);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Enregistre une clé pour le fournisseur réglé.</summary>
    public static Task<HttpResponseMessage> EnregistrerLaCleAsync(this ApiLancee api, string? cle) =>
        api.EcrireAsync(HttpMethod.Put, AdresseDeLaCle, new JsonObject { ["cle"] = cle }.ToJsonString());

    /// <summary>Enregistre une clé, et rend l'état qui suit.</summary>
    public static async Task<JsonObject> EnregistrerLaCleAvecSuccesAsync(this ApiLancee api, string cle)
    {
        using var reponse = await api.EnregistrerLaCleAsync(cle);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Retire la clé enregistrée.</summary>
    public static Task<HttpResponseMessage> RetirerLaCleAsync(this ApiLancee api) =>
        api.EcrireAsync(HttpMethod.Delete, AdresseDeLaCle, corps: null);

    /// <summary>L'adresse de l'essai de la clé (#48).</summary>
    public const string AdresseDeLEssai = "/api/reglages/ia/essai";

    /// <summary>Teste la clé, sans corps, comme la section « IA » le fait.</summary>
    public static Task<HttpResponseMessage> EssayerLaCleAsync(this ApiLancee api) =>
        api.EcrireAsync(HttpMethod.Post, AdresseDeLEssai, corps: null);

    /// <summary>Teste la clé, et rend le résultat d'un essai parti (200).</summary>
    public static async Task<JsonObject> EssayerLaCleAvecResultatAsync(this ApiLancee api)
    {
        using var reponse = await api.EssayerLaCleAsync();
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return (await AppelsDesCandidatures.LireAsync(reponse)).AsObject();
    }

    /// <summary>Le résultat attendu d'un essai, écrit comme l'API le rend.</summary>
    public static string ResultatDeLEssai(bool reussi, string? echec, string message) =>
        new JsonObject { ["reussi"] = reussi, ["echec"] = echec, ["message"] = message }.ToJsonString();

    /// <summary>L'état attendu, écrit comme l'API le rend.</summary>
    public static string Etat(string fournisseur, bool choisi, string cle, bool reglageLisible = true, bool fichierLisible = true) =>
        new JsonObject
        {
            ["fournisseur"] = fournisseur,
            ["choisi"] = choisi,
            ["reglageLisible"] = reglageLisible,
            ["fichierLisible"] = fichierLisible,
            ["cle"] = cle,
        }.ToJsonString();

    /// <summary>
    /// Aucun fichier du dossier, sous-dossiers compris, ne contient la clé fictive, ni sa partie reconnaissable, en UTF-8 ni
    /// en UTF-16. Le fichier de la clé non plus : il ne la porte que chiffrée.
    /// </summary>
    public static void AucunFichierNeContientDeCle(string dossier)
    {
        var formes = new[] { Encoding.UTF8.GetBytes(PartieReconnaissable), Encoding.Unicode.GetBytes(PartieReconnaissable) };
        foreach (var fichier in Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories))
        {
            // Le verrou du dossier, vide, que l'API lancée tient ouvert sans partage.
            if (Path.GetFileName(fichier) == "sollicito.verrou")
            {
                continue;
            }

            byte[] octets;
            try
            {
                using var flux = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var copie = new MemoryStream();
                flux.CopyTo(copie);
                octets = copie.ToArray();
            }
            catch (FileNotFoundException)
            {
                // Un fichier provisoire que l'API renomme pendant le parcours (la vérification du lancement écrit ainsi la date
                // de la dernière copie) : il n'est plus là, et son contenu est dans le fichier qui le remplace.
                continue;
            }

            foreach (var forme in formes)
            {
                Assert.True(octets.AsSpan().IndexOf(forme) < 0, $"{Path.GetRelativePath(dossier, fichier)} contient la clé.");
            }
        }
    }
}
