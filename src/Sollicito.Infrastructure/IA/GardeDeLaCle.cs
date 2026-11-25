using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sollicito.Application.IA;
using Sollicito.Infrastructure.Persistance;
using Sollicito.Infrastructure.Sauvegardes;

namespace Sollicito.Infrastructure.IA;

/// <summary>
/// La garde de la clé d'IA (#43 ; ADR-0001, D9 et D10 ; ADR-0016). La clé se chiffre par Data Protection, dont le trousseau
/// est protégé par DPAPI en portée utilisateur, jamais machine. Le trousseau vit dans <c>Trousseau</c>, la clé chiffrée
/// dans <c>cle-ia.json</c>, tous deux dans le dossier de la base, hors d'elle : ni ses copies ni un export ne les emportent.
/// <list type="bullet">
/// <item>Data Protection se construit ici, au premier usage, sur ce seul dossier : jamais au lancement, jamais par les
/// services de l'API, jamais sur son dossier par défaut, sous le profil Windows. Tant qu'aucune clé n'est enregistrée, le
/// trousseau n'existe pas. Il n'écrit aucun journal : les siens citeraient le chemin du trousseau, donc le nom du
/// compte.</item>
/// <item>« Une clé, une adresse » (ADR-0001, D7) : la charge chiffrée porte le fournisseur de la clé. Relue pour un autre
/// fournisseur, elle est illisible, et ne sert jamais.</item>
/// <item>Une clé illisible (trousseau absent, abîmé ou d'un autre compte, charge abîmée, autre fournisseur, fichier tenu)
/// le dit, avec le type de l'erreur : aucune exception ne sort de la lecture.</item>
/// </list>
/// L'appelant écrit et efface sous le verrou des réglages (<see cref="FichierDesReglages.SousLeVerrouAsync{T}"/>).
/// </summary>
public sealed class GardeDeLaCle : ISourceDeLaCle, IDisposable
{
    /// <summary>La fin (purpose) de Data Protection propre à la clé d'IA : aucune autre charge ne s'y déchiffre.</summary>
    public const string Fin = "Sollicito.IA.CleDUnFournisseur.v1";

    /// <summary>Le nom de l'application pour Data Protection.</summary>
    public const string NomDeLApplication = "Sollicito";

    /// <summary>Le plus gros fichier de clé lu : une clé de 512 caractères, chiffrée, en tient bien moins.</summary>
    public const int TailleMaximale = 16 * 1024;

    private const string ProprieteDeLaCharge = "charge";

    private const string ProprieteDuFournisseur = "fournisseur";

    private const string ProprieteDeLaCle = "cle";

    private readonly DossierDeLaBase _dossier;

    private readonly Lazy<Protection> _protection;

    /// <summary>La garde de la clé du dossier de la base, celui des services : un dossier temporaire dans les tests.</summary>
    public GardeDeLaCle(DossierDeLaBase dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        _dossier = dossier;
        _protection = new Lazy<Protection>(CreerLaProtection);
    }

    /// <summary>Vrai si un fichier de clé existe, lisible ou non. Rien d'autre n'est lu.</summary>
    public bool EstPresente => File.Exists(_dossier.FichierDeLaCle);

    /// <summary>
    /// Relit la clé enregistrée pour <paramref name="fournisseur"/>, qui doit en prendre une : absente, lisible, ou
    /// illisible avec sa cause. Sans fichier de clé, Data Protection n'est pas même construit.
    /// </summary>
    public LectureDeLaCle Lire(FournisseurDIA fournisseur)
    {
        if (!fournisseur.PrendUneCle())
        {
            throw new ArgumentOutOfRangeException(nameof(fournisseur), fournisseur, "Ce fournisseur d'IA ne prend aucune clé.");
        }

        if (!EstPresente)
        {
            return LectureDeLaCle.Absente;
        }

        byte[]? clair = null;
        try
        {
            if (LireLaCharge() is not { } charge)
            {
                return LectureDeLaCle.Illisible("ChargeMalFormee");
            }

            clair = _protection.Value.Protecteur.Unprotect(charge);
            return LireLeClair(clair, fournisseur);
        }
        catch (Exception erreur) when (erreur is not OperationCanceledException)
        {
            return LectureDeLaCle.Illisible(erreur.GetType().Name);
        }
        finally
        {
            if (clair is not null)
            {
                CryptographicOperations.ZeroMemory(clair);
            }
        }
    }

    /// <summary>
    /// Enregistre <paramref name="cle"/> pour <paramref name="fournisseur"/>, à la place de toute autre, d'un bloc : un
    /// fichier provisoire, puis un renommage. Une erreur de chiffrement ou d'écriture se lève, et la clé d'avant reste.
    /// </summary>
    public async Task EnregistrerAsync(FournisseurDIA fournisseur, CleDIA cle)
    {
        ArgumentNullException.ThrowIfNull(cle);
        if (!fournisseur.PrendUneCle())
        {
            throw new ArgumentOutOfRangeException(nameof(fournisseur), fournisseur, "Ce fournisseur d'IA ne prend aucune clé.");
        }

        var clair = EcrireLeClair(fournisseur, cle);
        byte[] charge;
        try
        {
            charge = _protection.Value.Protecteur.Protect(clair);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clair);
        }

        var texte = JsonSerializer.Serialize(new Dictionary<string, string> { [ProprieteDeLaCharge] = Convert.ToBase64String(charge) });
        var provisoire = $"{_dossier.FichierDeLaCle}.{Guid.NewGuid():N}.provisoire";
        try
        {
            // Poussé sur le disque avant le renommage : un arrêt brutal ne laisse jamais une clé à moitié écrite.
            await using (var flux = new FileStream(provisoire, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await flux.WriteAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(texte));
                flux.Flush(flushToDisk: true);
            }

            await CopiesVerifiees.ReessayerAsync(() => File.Move(provisoire, _dossier.FichierDeLaCle, overwrite: true));
        }
        finally
        {
            File.Delete(provisoire);
        }
    }

    /// <summary>
    /// Efface la clé enregistrée, en reprenant tant qu'un autre programme tient son fichier un instant. Vrai si aucune
    /// clé ne reste ; sans clé, vrai aussi. Le trousseau reste : il ne protège plus rien.
    /// </summary>
    public async Task<bool> EffacerAsync()
    {
        try
        {
            await CopiesVerifiees.ReessayerAsync(() => File.Delete(_dossier.FichierDeLaCle));
        }
        catch (Exception erreur) when (erreur is IOException or UnauthorizedAccessException)
        {
            // La clé reste : l'appelant le dit.
        }

        return !EstPresente;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_protection.IsValueCreated)
        {
            _protection.Value.Services.Dispose();
        }
    }

    // Pour les tests : les options de gestion des clés que Data Protection applique, sans rien lire ni écrire du trousseau.
    internal KeyManagementOptions OptionsDesCles() =>
        _protection.Value.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

    // La charge chiffrée lue dans le fichier, ou null s'il n'a pas sa forme ; une erreur d'entrée-sortie se lève.
    private byte[]? LireLaCharge()
    {
        var fichier = new FileInfo(_dossier.FichierDeLaCle);
        if (fichier.Length > TailleMaximale)
        {
            return null;
        }

        using var document = JsonDocument.Parse(LireEnReprenant(fichier.FullName));
        var racine = document.RootElement;
        if (racine.ValueKind != JsonValueKind.Object
            || racine.EnumerateObject().Count() != 1
            || !racine.TryGetProperty(ProprieteDeLaCharge, out var charge)
            || charge.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Convert.FromBase64String(charge.GetString()!);
    }

    // Les octets du fichier, en reprenant quelques fois tant qu'un autre programme, ou l'enregistrement d'une clé en cours,
    // le tient un instant ; au-delà, l'erreur se lève, et la clé est dite illisible.
    private static byte[] LireEnReprenant(string chemin)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                return File.ReadAllBytes(chemin);
            }
            catch (IOException) when (essai < 10)
            {
                Thread.Sleep(50);
            }
        }
    }

    // La clé déchiffrée, si elle est bien celle du fournisseur attendu et garde sa forme.
    private static LectureDeLaCle LireLeClair(byte[] clair, FournisseurDIA attendu)
    {
        using var document = JsonDocument.Parse(clair);
        var racine = document.RootElement;
        if (racine.ValueKind != JsonValueKind.Object
            || racine.EnumerateObject().Count() != 2
            || !racine.TryGetProperty(ProprieteDuFournisseur, out var nom)
            || nom.ValueKind != JsonValueKind.String
            || !racine.TryGetProperty(ProprieteDeLaCle, out var valeur)
            || valeur.ValueKind != JsonValueKind.String
            || !FournisseursDIA.EssayerDeLire(nom.GetString(), out var fournisseur))
        {
            return LectureDeLaCle.Illisible("ChargeMalFormee");
        }

        if (fournisseur != attendu)
        {
            return LectureDeLaCle.Illisible("AutreFournisseur");
        }

        return FormeDUneCle.Verifier(fournisseur, valeur.GetString(), out var cle) is null && cle is not null
            ? LectureDeLaCle.Lisible(cle)
            : LectureDeLaCle.Illisible("FormeRefusee");
    }

    // Le clair à chiffrer : le fournisseur et la clé, en JSON.
    private static byte[] EcrireLeClair(FournisseurDIA fournisseur, CleDIA cle)
    {
        var tampon = new ArrayBufferWriter<byte>();
        using (var ecrivain = new Utf8JsonWriter(tampon))
        {
            ecrivain.WriteStartObject();
            ecrivain.WriteString(ProprieteDuFournisseur, fournisseur.Nom());
            ecrivain.WriteString(ProprieteDeLaCle, cle.Valeur());
            ecrivain.WriteEndObject();
        }

        var clair = tampon.WrittenSpan.ToArray();
        tampon.Clear();
        return clair;
    }

    // Data Protection, sur le seul trousseau de ce dossier, ses clés protégées par DPAPI en portée utilisateur.
    private Protection CreerLaProtection()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("La clé d'IA se chiffre par DPAPI, sous Windows seulement.");
        }

        var services = new ServiceCollection();
        // Aucun fournisseur de journaux : ce que Data Protection écrirait ne va nulle part.
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName(NomDeLApplication)
            .ProtectKeysWithDpapi(protectToLocalMachine: false)
            .AddKeyManagementOptions(options => options.XmlRepository = new DepotDuTrousseau(_dossier.Trousseau));
        var fournisseurDeServices = services.BuildServiceProvider();
        return new Protection(fournisseurDeServices, fournisseurDeServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Fin));
    }

    private sealed record Protection(ServiceProvider Services, IDataProtector Protecteur);
}

/// <summary>Une clé d'IA relue : absente, lisible, ou illisible avec sa cause, jamais son contenu.</summary>
public sealed class LectureDeLaCle
{
    private LectureDeLaCle(EtatDeLaCle etat, CleDIA? cle, string? cause)
    {
        Etat = etat;
        Cle = cle;
        Cause = cause;
    }

    /// <summary>Aucune clé n'est enregistrée.</summary>
    public static LectureDeLaCle Absente { get; } = new(EtatDeLaCle.Aucune, null, null);

    /// <summary>L'état de la clé, tel que l'écran le dit.</summary>
    public EtatDeLaCle Etat { get; }

    /// <summary>La clé, si elle se relit.</summary>
    public CleDIA? Cle { get; }

    /// <summary>Pourquoi une clé ne se relit pas : le type d'une erreur, ou la sorte d'un écart ; jamais son contenu.</summary>
    public string? Cause { get; }

    /// <summary>Une clé enregistrée qui se relit.</summary>
    public static LectureDeLaCle Lisible(CleDIA cle) => new(EtatDeLaCle.Enregistree, cle ?? throw new ArgumentNullException(nameof(cle)), null);

    /// <summary>Une clé enregistrée qui ne se relit pas, et pourquoi.</summary>
    public static LectureDeLaCle Illisible(string cause) => new(EtatDeLaCle.Illisible, null, cause);
}
