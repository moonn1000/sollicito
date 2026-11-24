using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sollicito.Application.IA;
using Sollicito.Infrastructure.IA;
using Sollicito.Infrastructure.Persistance;

namespace Sollicito.Infrastructure.Tests;

/// <summary>
/// La garde de la clé d'IA (#43, ADR-0016), sur un vrai trousseau de Data Protection, protégé par DPAPI, dans un dossier
/// temporaire : jamais sous le profil réel. Les clés sont fictives et reconnaissables.
/// </summary>
public sealed partial class GardeDeLaCleTests : IDisposable
{
    private const string CleDeClaude = "sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0001";

    private const string AutreCleDeClaude = "sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive-de-claude-0002";

    private readonly string _dossier = Directory.CreateTempSubdirectory("sollicito-tests-").FullName;

    private DossierDeLaBase Base => new(_dossier);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dossier, recursive: true);
        }
        catch (IOException)
        {
            // Le dossier temporaire reste : le système le videra.
        }
    }

    [Fact]
    public async Task Une_cle_enregistree_se_relit_et_le_trousseau_nait_sous_le_dossier_de_la_base()
    {
        using (var garde = new GardeDeLaCle(Base))
        {
            await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));

            var lecture = garde.Lire(FournisseurDIA.Claude);
            Assert.Equal(EtatDeLaCle.Enregistree, lecture.Etat);
            Assert.Equal(CleDeClaude, lecture.Cle!.Valeur());
        }

        Assert.True(File.Exists(Path.Combine(_dossier, "cle-ia.json")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_dossier, "Trousseau"), "key-*.xml"));
        // Relue par une nouvelle garde, comme au lancement suivant.
        using var relancee = new GardeDeLaCle(Base);
        Assert.Equal(CleDeClaude, relancee.Lire(FournisseurDIA.Claude).Cle!.Valeur());
    }

    [Fact]
    public async Task Aucun_fichier_ne_porte_la_cle_en_clair()
    {
        using var garde = new GardeDeLaCle(Base);
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));

        AucunFichierNeContient(_dossier, CleDeClaude);
    }

    [Fact]
    public async Task Les_cles_du_trousseau_sont_protegees_par_DPAPI_en_portee_utilisateur()
    {
        using var garde = new GardeDeLaCle(Base);
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));

        foreach (var fichier in Directory.GetFiles(Path.Combine(_dossier, "Trousseau"), "*.xml"))
        {
            var cle = XElement.Load(fichier);
            var secret = cle.Descendants(XName.Get("encryptedSecret", "http://schemas.asp.net/2015/03/dataProtection")).Single();
            Assert.Contains("DpapiXmlDecryptor", (string?)secret.Attribute("decryptorType"), StringComparison.Ordinal);
            Assert.Empty(cle.Descendants("masterKey"));
            // L'enveloppe de DPAPI : version, fournisseur, version et identifiant de la clé maîtresse, puis ses drapeaux.
            // CRYPTPROTECT_LOCAL_MACHINE (0x4) dirait la portée machine.
            var enveloppe = Convert.FromBase64String(secret.Descendants("value").Single().Value);
            var drapeaux = BinaryPrimitives.ReadUInt32LittleEndian(enveloppe.AsSpan(40, 4));
            Assert.Equal(0u, drapeaux & 0x4u);
        }
    }

    [Fact]
    public void Data_Protection_n_a_que_le_trousseau_du_dossier_et_DPAPI_pour_ses_cles()
    {
        using var garde = new GardeDeLaCle(Base);

        var options = garde.OptionsDesCles();

        // Jamais le dossier par défaut de Data Protection, sous le profil Windows : seul le trousseau de ce dossier.
        var depot = Assert.IsType<DepotDuTrousseau>(options.XmlRepository);
        Assert.Equal(Path.Combine(_dossier, "Trousseau"), depot.Dossier);
        Assert.Equal("Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlEncryptor", options.XmlEncryptor?.GetType().FullName);
        // Construire Data Protection n'écrit rien.
        Assert.Empty(Directory.GetFileSystemEntries(_dossier));
    }

    [Fact]
    public void Sans_cle_rien_ne_s_ecrit_ni_le_trousseau_ni_le_fichier()
    {
        using var garde = new GardeDeLaCle(Base);

        Assert.Equal(EtatDeLaCle.Aucune, garde.Lire(FournisseurDIA.Claude).Etat);
        Assert.Equal(EtatDeLaCle.Aucune, garde.Lire(FournisseurDIA.OpenAI).Etat);
        Assert.False(garde.EstPresente);
        Assert.Empty(Directory.GetFileSystemEntries(_dossier));
    }

    [Fact]
    public async Task Une_cle_remplacee_ne_laisse_que_la_nouvelle()
    {
        using var garde = new GardeDeLaCle(Base);
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, AutreCleDeClaude));

        Assert.Equal(AutreCleDeClaude, garde.Lire(FournisseurDIA.Claude).Cle!.Valeur());
        Assert.Empty(Directory.GetFiles(_dossier, "*.provisoire"));
    }

    [Fact]
    public async Task Effacer_retire_la_cle_et_laisse_le_trousseau()
    {
        using var garde = new GardeDeLaCle(Base);
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));

        Assert.True(await garde.EffacerAsync());

        Assert.False(garde.EstPresente);
        Assert.Equal(EtatDeLaCle.Aucune, garde.Lire(FournisseurDIA.Claude).Etat);
        Assert.True(Directory.Exists(Path.Combine(_dossier, "Trousseau")));
        // Sans clé, effacer réussit aussi.
        Assert.True(await garde.EffacerAsync());
    }

    [Fact]
    public async Task Relue_pour_un_autre_fournisseur_la_cle_est_illisible_et_ne_sert_pas()
    {
        using var garde = new GardeDeLaCle(Base);
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));

        var lecture = garde.Lire(FournisseurDIA.OpenAI);

        Assert.Equal(EtatDeLaCle.Illisible, lecture.Etat);
        Assert.Null(lecture.Cle);
        Assert.Equal("AutreFournisseur", lecture.Cause);
    }

    [Fact]
    public async Task Un_trousseau_efface_rend_la_cle_illisible_puis_une_nouvelle_s_enregistre()
    {
        using (var garde = new GardeDeLaCle(Base))
        {
            await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));
        }

        Directory.Delete(Path.Combine(_dossier, "Trousseau"), recursive: true);

        await IllisiblePuisRemplacableAsync();
    }

    [Fact]
    public async Task Un_trousseau_abime_rend_la_cle_illisible_puis_une_nouvelle_s_enregistre()
    {
        using (var garde = new GardeDeLaCle(Base))
        {
            await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));
        }

        foreach (var fichier in Directory.GetFiles(Path.Combine(_dossier, "Trousseau"), "*.xml"))
        {
            await File.WriteAllTextAsync(fichier, "<key pas du xml", TestContext.Current.CancellationToken);
        }

        await IllisiblePuisRemplacableAsync();
    }

    // Un seul compte Windows ne peut pas produire le trousseau d'un autre. Son enveloppe DPAPI altérée, que ce compte
    // n'ouvre pas, suit le même chemin : DPAPI refuse de la déchiffrer.
    [Fact]
    public async Task Un_trousseau_que_ce_compte_ne_dechiffre_pas_rend_la_cle_illisible_puis_une_nouvelle_s_enregistre()
    {
        using (var garde = new GardeDeLaCle(Base))
        {
            await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));
        }

        foreach (var fichier in Directory.GetFiles(Path.Combine(_dossier, "Trousseau"), "*.xml"))
        {
            var cle = XElement.Load(fichier);
            var valeur = cle.Descendants("value").Single();
            var enveloppe = Convert.FromBase64String(valeur.Value);
            for (var octet = enveloppe.Length - 24; octet < enveloppe.Length; octet++)
            {
                enveloppe[octet] ^= 0x5A;
            }

            valeur.Value = Convert.ToBase64String(enveloppe);
            cle.Save(fichier);
        }

        await IllisiblePuisRemplacableAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"charge": 3}""")]
    [InlineData("""{"charge": "pas du base64 !"}""")]
    [InlineData("""{"charge": "AAAA"}""")]
    [InlineData("""{"charge": "AAAA", "autre": 1}""")]
    public void Une_charge_abimee_rend_la_cle_illisible_sans_lever(string contenu)
    {
        File.WriteAllText(Path.Combine(_dossier, "cle-ia.json"), contenu);
        using var garde = new GardeDeLaCle(Base);

        var lecture = garde.Lire(FournisseurDIA.Claude);

        Assert.Equal(EtatDeLaCle.Illisible, lecture.Etat);
        Assert.Null(lecture.Cle);
        Assert.False(string.IsNullOrEmpty(lecture.Cause));
    }

    [Fact]
    public void Un_fichier_de_cle_trop_gros_n_est_pas_lu()
    {
        File.WriteAllText(Path.Combine(_dossier, "cle-ia.json"), new string(' ', GardeDeLaCle.TailleMaximale + 1));
        using var garde = new GardeDeLaCle(Base);

        Assert.Equal(EtatDeLaCle.Illisible, garde.Lire(FournisseurDIA.Claude).Etat);
        Assert.False(Directory.Exists(Path.Combine(_dossier, "Trousseau")));
    }

    [Fact]
    public async Task Un_fichier_de_cle_tenu_par_un_autre_programme_ne_s_efface_pas_et_ne_se_lit_pas()
    {
        using var garde = new GardeDeLaCle(Base);
        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));

        using (new FileStream(Path.Combine(_dossier, "cle-ia.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(EtatDeLaCle.Illisible, garde.Lire(FournisseurDIA.Claude).Etat);
            Assert.False(await garde.EffacerAsync());
        }

        Assert.True(garde.EstPresente);
        Assert.Equal(CleDeClaude, garde.Lire(FournisseurDIA.Claude).Cle!.Valeur());
    }

    [Fact]
    public void Un_fournisseur_sans_cle_n_a_rien_a_garder()
    {
        using var garde = new GardeDeLaCle(Base);

        Assert.Throws<ArgumentOutOfRangeException>(() => garde.Lire(FournisseurDIA.Aucun));
        Assert.Throws<ArgumentOutOfRangeException>(() => garde.Lire(FournisseurDIA.Ollama));
    }

    [Fact]
    public async Task Les_restes_d_une_cle_ou_d_une_cle_du_trousseau_a_moitie_ecrite_se_retirent_au_lancement()
    {
        using (var garde = new GardeDeLaCle(Base))
        {
            await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, CleDeClaude));
        }

        var provisoireDeLaCle = Path.Combine(_dossier, $"cle-ia.json.{Guid.NewGuid():N}.provisoire");
        var provisoireDuTrousseau = Path.Combine(_dossier, "Trousseau", $"{Guid.NewGuid():N}.provisoire");
        var etranger = Path.Combine(_dossier, "Trousseau", "notes.provisoire");
        await File.WriteAllTextAsync(provisoireDeLaCle, "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(provisoireDuTrousseau, "<key/>", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(etranger, "à garder", TestContext.Current.CancellationToken);

        Assert.Equal(2, await RestesDUnArret.RetirerAsync(Base));

        Assert.False(File.Exists(provisoireDeLaCle));
        Assert.False(File.Exists(provisoireDuTrousseau));
        Assert.True(File.Exists(etranger));
        using var relancee = new GardeDeLaCle(Base);
        Assert.Equal(CleDeClaude, relancee.Lire(FournisseurDIA.Claude).Cle!.Valeur());
    }

    // La clé d'avant ne se relit plus, sans lever ; une nouvelle s'enregistre et se relit, sur un trousseau renouvelé.
    private async Task IllisiblePuisRemplacableAsync()
    {
        using var garde = new GardeDeLaCle(Base);

        var lecture = garde.Lire(FournisseurDIA.Claude);
        Assert.Equal(EtatDeLaCle.Illisible, lecture.Etat);
        Assert.Null(lecture.Cle);

        await garde.EnregistrerAsync(FournisseurDIA.Claude, Cle(FournisseurDIA.Claude, AutreCleDeClaude));
        Assert.Equal(AutreCleDeClaude, garde.Lire(FournisseurDIA.Claude).Cle!.Valeur());
        using var relancee = new GardeDeLaCle(Base);
        Assert.Equal(AutreCleDeClaude, relancee.Lire(FournisseurDIA.Claude).Cle!.Valeur());
    }

    private static CleDIA Cle(FournisseurDIA fournisseur, string valeur)
    {
        Assert.Null(FormeDUneCle.Verifier(fournisseur, valeur, out var cle));
        return cle!;
    }

    // Aucun fichier du dossier, sous-dossiers compris, ne contient la valeur, en UTF-8 ni en UTF-16, ni même sa partie
    // reconnaissable.
    internal static void AucunFichierNeContient(string dossier, string valeur)
    {
        var formes = new[]
        {
            Encoding.UTF8.GetBytes(valeur),
            Encoding.Unicode.GetBytes(valeur),
            Encoding.UTF8.GetBytes(PartieReconnaissable().Match(valeur).Value),
            Encoding.Unicode.GetBytes(PartieReconnaissable().Match(valeur).Value),
        };
        foreach (var fichier in Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories))
        {
            var octets = File.ReadAllBytes(fichier);
            foreach (var forme in formes)
            {
                Assert.True(octets.AsSpan().IndexOf(forme) < 0, $"{Path.GetFileName(fichier)} contient la clé.");
            }
        }
    }

    [GeneratedRegex("SOLLICITO-ESSAI-43-[a-z-]+", RegexOptions.CultureInvariant)]
    private static partial Regex PartieReconnaissable();
}
