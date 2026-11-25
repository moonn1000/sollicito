using System.Net.Http.Headers;

namespace Sollicito.Infrastructure.IA.EnLigne;

/// <summary>
/// L'envoi borné d'une requête à un fournisseur en ligne (#48, ADR-0017) : une seule tentative, une durée qui couvre la
/// connexion, les en-têtes et la lecture du corps, et un corps lu au plus jusqu'à sa borne. Le corps d'une réponse en
/// erreur n'est jamais lu : seul son statut compte. Aucune exception ne sort, sauf l'annulation par l'appelant.
/// </summary>
internal static class EnvoiBorne
{
    /// <summary>
    /// Envoie <paramref name="requete"/> une fois, et rend ce qui est revenu : un statut et son corps borné, un statut en
    /// erreur sans corps, ou un échec du transport. <paramref name="annulation"/> annulé lève l'exception de l'annulation.
    /// </summary>
    public static async Task<ReponseBrute> EnvoyerAsync(
        TransportDeLIA transport,
        HttpRequestMessage requete,
        int octetsMaximum,
        TimeSpan duree,
        CancellationToken annulation)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(requete);
        ArgumentOutOfRangeException.ThrowIfLessThan(octetsMaximum, 1);
        annulation.ThrowIfCancellationRequested();

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(duree);
        try
        {
            using var reponse = await transport.EnvoyerAsync(requete, delai.Token);
            var statut = (int)reponse.StatusCode;
            if (statut is < 200 or > 299)
            {
                // Le corps d'une erreur n'est jamais lu : il peut reprendre la demande, ou une partie de la clé.
                return ReponseBrute.SansCorps(statut);
            }

            var contenu = reponse.Content;
            if (contenu.Headers.ContentLength > octetsMaximum)
            {
                return ReponseBrute.EnEchec(EchecDuTransport.TropGrande, statut);
            }

            var octets = await LireAuPlusAsync(contenu, octetsMaximum, delai.Token);
            return octets is null
                ? ReponseBrute.EnEchec(EchecDuTransport.TropGrande, statut)
                : ReponseBrute.AvecCorps(statut, TypeDuContenu(contenu.Headers.ContentType), contenu.Headers.ContentEncoding.Count > 0, octets);
        }
        catch (OperationCanceledException) when (annulation.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ReponseBrute.EnEchec(EchecDuTransport.DelaiDepasse, null);
        }
        catch (Exception erreur)
        {
            // Nom introuvable, connexion ou TLS refusés, proxy en échec, connexion coupée pendant la lecture : seul le type de
            // l'erreur reste, jamais son message, qui cite l'hôte.
            return ReponseBrute.EnEchec(EchecDuTransport.Injoignable, null, erreur.GetType().Name);
        }
    }

    // Le type du contenu, sans ses paramètres (charset compris), en minuscules ; vide s'il manque.
    private static string TypeDuContenu(MediaTypeHeaderValue? type) => type?.MediaType?.ToLowerInvariant() ?? "";

    // Le corps, ou null s'il dépasse la borne : la lecture s'arrête au premier octet de trop.
    private static async Task<byte[]?> LireAuPlusAsync(HttpContent contenu, int octetsMaximum, CancellationToken annulation)
    {
        await using var flux = await contenu.ReadAsStreamAsync(annulation);
        var tampon = new byte[octetsMaximum + 1];
        var lus = 0;
        while (lus < tampon.Length)
        {
            var n = await flux.ReadAsync(tampon.AsMemory(lus), annulation);
            if (n == 0)
            {
                return tampon.AsSpan(0, lus).ToArray();
            }

            lus += n;
        }

        return null;
    }
}

/// <summary>Ce que le transport n'a pas pu rendre.</summary>
internal enum EchecDuTransport
{
    /// <summary>Le fournisseur n'a pas pu être joint, ou la connexion s'est coupée.</summary>
    Injoignable,

    /// <summary>La durée de la demande est dépassée.</summary>
    DelaiDepasse,

    /// <summary>Le corps dépassait sa borne : il n'est pas rendu.</summary>
    TropGrande,
}

/// <summary>Ce qui est revenu d'un envoi : un statut, et un corps borné s'il a été lu ; ou un échec du transport.</summary>
internal sealed class ReponseBrute
{
    private ReponseBrute(int? statut, EchecDuTransport? echec, string typeDuContenu, bool encode, byte[]? corps, string? typeDErreur)
    {
        Statut = statut;
        Echec = echec;
        TypeDuContenu = typeDuContenu;
        Encode = encode;
        Corps = corps;
        TypeDErreur = typeDErreur;
    }

    /// <summary>Le statut HTTP reçu, s'il y en a un.</summary>
    public int? Statut { get; }

    /// <summary>L'échec du transport, s'il y en a un.</summary>
    public EchecDuTransport? Echec { get; }

    /// <summary>Le type du contenu, sans ses paramètres.</summary>
    public string TypeDuContenu { get; }

    /// <summary>Vrai si le corps porte un codage de contenu (gzip…) : il n'est pas décodé, donc pas lisible.</summary>
    public bool Encode { get; }

    /// <summary>Le corps lu, d'une réponse 2xx seulement.</summary>
    public byte[]? Corps { get; }

    /// <summary>Le type de l'erreur du transport, jamais son message.</summary>
    public string? TypeDErreur { get; }

    public static ReponseBrute SansCorps(int statut) => new(statut, null, "", false, null, null);

    public static ReponseBrute AvecCorps(int statut, string typeDuContenu, bool encode, byte[] corps) =>
        new(statut, null, typeDuContenu, encode, corps, null);

    public static ReponseBrute EnEchec(EchecDuTransport echec, int? statut, string? typeDErreur = null) =>
        new(statut, echec, "", false, null, typeDErreur);

    /// <summary>Un texte qui ne montre pas le corps.</summary>
    public override string ToString() => $"Réponse brute ({Statut?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "sans statut"})";
}
