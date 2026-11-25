using System.Net;
using System.Text;
using Sollicito.Application.IA;
using Sollicito.Infrastructure.IA.EnLigne;
using static Sollicito.Infrastructure.Tests.IA.OutilsDesFournisseurs;

namespace Sollicito.Infrastructure.Tests.IA;

/// <summary>
/// Ce que les adaptateurs en ligne font d'une réponse (#48, ADR-0017) : une erreur classée par son seul statut, sans lire
/// son corps ; une réponse 200 lue en JSON strict, traitée comme non fiable, rendue en texte borné ou en issue fermée.
/// </summary>
public sealed class ReponsesEnLigneTests
{
    public static TheoryData<FournisseurDIA, int, SorteDEchec> Statuts
    {
        get
        {
            var donnees = new TheoryData<FournisseurDIA, int, SorteDEchec>();
            (int, SorteDEchec)[] table =
            [
                (301, SorteDEchec.ReponseIllisible),
                (302, SorteDEchec.ReponseIllisible),
                (303, SorteDEchec.ReponseIllisible),
                (307, SorteDEchec.ReponseIllisible),
                (308, SorteDEchec.ReponseIllisible),
                (400, SorteDEchec.RefusDuFournisseur),
                (401, SorteDEchec.CleRefusee),
                (402, SorteDEchec.QuotaAtteint),
                (403, SorteDEchec.AccesRefuse),
                (404, SorteDEchec.ModeleIndisponible),
                (408, SorteDEchec.FournisseurEnPanne),
                (413, SorteDEchec.RefusDuFournisseur),
                (418, SorteDEchec.ReponseIllisible),
                (422, SorteDEchec.RefusDuFournisseur),
                (429, SorteDEchec.QuotaAtteint),
                (451, SorteDEchec.ReponseIllisible),
                (500, SorteDEchec.FournisseurEnPanne),
                (502, SorteDEchec.FournisseurEnPanne),
                (503, SorteDEchec.FournisseurEnPanne),
                (504, SorteDEchec.FournisseurEnPanne),
                (529, SorteDEchec.FournisseurEnPanne),
                (599, SorteDEchec.ReponseIllisible),
            ];
            foreach (var fournisseur in new[] { FournisseurDIA.Claude, FournisseurDIA.OpenAI })
            {
                foreach (var (statut, sorte) in table)
                {
                    donnees.Add(fournisseur, statut, sorte);
                }
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(Statuts))]
    public async Task Une_erreur_se_classe_par_son_seul_statut_sans_lire_son_corps(FournisseurDIA fournisseur, int statut, SorteDEchec attendue)
    {
        var corps = new CorpsQuiNeSeLitPas();
        var faux = new FauxGestionnaire(() =>
        {
            var reponse = new HttpResponseMessage((HttpStatusCode)statut) { Content = new StreamContent(corps) };
            reponse.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            if (statut is >= 300 and < 400)
            {
                reponse.Headers.Location = new Uri("https://exemple.invalid/ailleurs");
            }

            return reponse;
        });

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(attendue, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
        Assert.False(corps.Lu, "Le corps d'une erreur a été lu.");
        // Une redirection n'est jamais suivie : une seule requête, vers l'adresse fixée.
        Assert.Single(faux.Recues);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude, 201)]
    [InlineData(FournisseurDIA.OpenAI, 204)]
    public async Task Un_autre_succes_que_200_est_illisible(FournisseurDIA fournisseur, int statut)
    {
        var faux = new FauxGestionnaire(() => Json((HttpStatusCode)statut, ReponseReussie(fournisseur)));

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.ReponseIllisible, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
    }

    [Theory]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Bonjour"}],"stop_reason":"end_turn"}""", "Bonjour")]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Bon"},{"type":"text","text":"jour"}],"stop_reason":"stop_sequence"}""", "Bonjour")]
    [InlineData("""{"type":"message","content":[{"type":"thinking","thinking":""},{"type":"text","text":"Bonjour"}],"stop_reason":"end_turn","inconnu":{"a":[1,2]}}""", "Bonjour")]
    public async Task Claude_rend_son_texte(string corps, string texte)
    {
        var rendue = await RepondreAsync(FournisseurDIA.Claude, corps);

        Assert.Equal(texte, Assert.IsType<ReponseDeLIA.Texte>(rendue).Contenu);
    }

    [Theory]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Bonj"}],"stop_reason":"max_tokens"}""", SorteDEchec.ReponseTropLongue)]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Bonj"}],"stop_reason":"model_context_window_exceeded"}""", SorteDEchec.ReponseTropLongue)]
    [InlineData("""{"type":"message","content":[],"stop_reason":"refusal"}""", SorteDEchec.RefusDuFournisseur)]
    [InlineData("""{"type":"message","content":[{"type":"tool_use","id":"t","name":"n","input":{}}],"stop_reason":"tool_use"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[],"stop_reason":"pause_turn"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Bonjour"}],"stop_reason":"inconnue"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Bonjour"}]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"error","content":[{"type":"text","text":"Bonjour"}],"stop_reason":"end_turn"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":{"type":"text","text":"Bonjour"},"stop_reason":"end_turn"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[{"type":"text","text":42}],"stop_reason":"end_turn"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[{"text":"Bonjour"}],"stop_reason":"end_turn"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[{"type":"text","text":""}],"stop_reason":"end_turn"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"type":"message","content":[{"type":"text","text":"Une réponse bien trop longue pour la borne."}],"stop_reason":"end_turn"}""", SorteDEchec.ReponseTropLongue)]
    [InlineData("""["message"]""", SorteDEchec.ReponseIllisible)]
    public async Task Claude_rend_une_issue_fermee_pour_toute_autre_reponse(string corps, SorteDEchec attendue)
    {
        var rendue = await RepondreAsync(FournisseurDIA.Claude, corps, caracteres: 20);

        Assert.Equal(attendue, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
    }

    [Theory]
    [InlineData("""{"status":"completed","output":[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Bonjour"}]}]}""", "Bonjour")]
    [InlineData("""{"status":"completed","output":[{"type":"reasoning","summary":[]},{"type":"message","content":[{"type":"output_text","text":"Bon"},{"type":"output_text","text":"jour"}]}],"inconnu":true}""", "Bonjour")]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"Bon"}]},{"type":"message","content":[{"type":"output_text","text":"jour"}]}]}""", "Bonjour")]
    public async Task OpenAI_rend_son_texte(string corps, string texte)
    {
        var rendue = await RepondreAsync(FournisseurDIA.OpenAI, corps);

        Assert.Equal(texte, Assert.IsType<ReponseDeLIA.Texte>(rendue).Contenu);
    }

    [Theory]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}""", SorteDEchec.ReponseTropLongue)]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"content_filter"},"output":[]}""", SorteDEchec.RefusDuFournisseur)]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"steered"},"output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"incomplete","output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"failed","error":{"code":"server_error","message":"x"},"output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"queued","output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"in_progress","output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"cancelled","output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"completed"}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"completed","output":[]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"Non."}]}]}""", SorteDEchec.RefusDuFournisseur)]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":7}]}]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":"Bonjour"}]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"completed","output":[{"content":[]}]}""", SorteDEchec.ReponseIllisible)]
    [InlineData("""{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"Une réponse bien trop longue pour la borne."}]}]}""", SorteDEchec.ReponseTropLongue)]
    public async Task OpenAI_rend_une_issue_fermee_pour_toute_autre_reponse(string corps, SorteDEchec attendue)
    {
        var rendue = await RepondreAsync(FournisseurDIA.OpenAI, corps, caracteres: 20);

        Assert.Equal(attendue, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
    }

    public static TheoryData<FournisseurDIA, string> JsonQuiNeSeLitPas
    {
        get
        {
            var donnees = new TheoryData<FournisseurDIA, string>();
            foreach (var fournisseur in new[] { FournisseurDIA.Claude, FournisseurDIA.OpenAI })
            {
                var reussie = ReponseReussie(fournisseur);
                donnees.Add(fournisseur, reussie[..^1] + ",\"model\":\"repete\"}");
                donnees.Add(fournisseur, reussie[..^1] + ",}");
                donnees.Add(fournisseur, reussie[..^1] + "/* commentaire */}");
                donnees.Add(fournisseur, reussie[..20]);
                donnees.Add(fournisseur, "");
                donnees.Add(fournisseur, new string('[', 40) + new string(']', 40));
                donnees.Add(fournisseur, reussie + " en trop");
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(JsonQuiNeSeLitPas))]
    public async Task Un_JSON_mal_forme_ou_qui_repete_une_propriete_est_illisible(FournisseurDIA fournisseur, string corps)
    {
        var rendue = await RepondreAsync(fournisseur, corps);

        Assert.Equal(SorteDEchec.ReponseIllisible, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
    }

    // Un échappement de substitut sans son pareil (un haut ou un bas seul) est du JSON valide, mais ne se lit pas en texte :
    // la lecture lève InvalidOperationException, que le port ne laisse jamais sortir (revue de #48). L'antislash vient de
    // (char)92 : aucun échappement unicode n'est écrit dans ces cas.
    public static TheoryData<FournisseurDIA, string> JsonQuiPorteUnSubstitutIsole
    {
        get
        {
            var antislash = ((char)92).ToString();
            var donnees = new TheoryData<FournisseurDIA, string>();
            foreach (var fournisseur in new[] { FournisseurDIA.Claude, FournisseurDIA.OpenAI })
            {
                var reussie = ReponseReussie(fournisseur);
                foreach (var texte in new[]
                {
                    antislash + "ud800",
                    antislash + "udc00",
                    antislash + "ud800" + antislash + "u0041",
                    "a" + antislash + "ud83d",
                })
                {
                    donnees.Add(fournisseur, reussie.Replace("\"OK\"", "\"" + texte + "\"", StringComparison.Ordinal));
                }

                donnees.Add(fournisseur, reussie[..^1] + ",\"x" + antislash + "ud800\":1}");
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(JsonQuiPorteUnSubstitutIsole))]
    public async Task Un_substitut_isole_est_illisible_sans_rien_lever_et_la_porte_se_rend(FournisseurDIA fournisseur, string corps)
    {
        var journal = new JournalDeTest();
        var adaptateur = Adaptateur(fournisseur, new FauxGestionnaire(() => Json(HttpStatusCode.OK, corps)), journal: journal);

        var premiere = await adaptateur.DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        var seconde = await adaptateur.DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.ReponseIllisible, Assert.IsType<ReponseDeLIA.Echec>(premiere).Sorte);
        Assert.Equal(SorteDEchec.ReponseIllisible, Assert.IsType<ReponseDeLIA.Echec>(seconde).Sorte);
        Assert.Equal(
            2,
            journal.Entrees.Count(entree => entree.Contains("ReponseIllisible", StringComparison.Ordinal) && entree.Contains("(statut 200)", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude, "application/json; charset=utf-8")]
    [InlineData(FournisseurDIA.OpenAI, "Application/JSON; charset=UTF-8")]
    [InlineData(FournisseurDIA.OpenAI, "application/json")]
    public async Task Le_type_application_json_se_compare_sans_ses_parametres(FournisseurDIA fournisseur, string type)
    {
        var faux = new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseReussie(fournisseur), type));

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal("OK", Assert.IsType<ReponseDeLIA.Texte>(rendue).Contenu);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude, "text/plain")]
    [InlineData(FournisseurDIA.OpenAI, "text/html; charset=utf-8")]
    [InlineData(FournisseurDIA.OpenAI, "application/json-seq")]
    public async Task Un_autre_type_de_contenu_est_illisible(FournisseurDIA fournisseur, string type)
    {
        var faux = new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseReussie(fournisseur), type));

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.ReponseIllisible, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Un_corps_sans_type_ou_code_en_gzip_est_illisible(FournisseurDIA fournisseur)
    {
        var sansType = new FauxGestionnaire(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(ReponseReussie(fournisseur))) });
        var enGzip = new FauxGestionnaire(() =>
        {
            var reponse = Json(HttpStatusCode.OK, ReponseReussie(fournisseur));
            reponse.Content.Headers.ContentEncoding.Add("gzip");
            return reponse;
        });

        foreach (var faux in new[] { sansType, enGzip })
        {
            var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
            Assert.Equal(SorteDEchec.ReponseIllisible, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
        }
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Un_corps_annonce_trop_grand_n_est_pas_lu(FournisseurDIA fournisseur)
    {
        var corps = new CorpsQuiNeSeLitPas();
        var faux = new FauxGestionnaire(() =>
        {
            var reponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(corps) };
            reponse.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            reponse.Content.Headers.ContentLength = FournisseurEnLigne.OctetsMaximumDeLaReponse + 1;
            return reponse;
        });

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.ReponseTropLongue, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
        Assert.False(corps.Lu);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Un_corps_sans_fin_s_arrete_a_sa_borne(FournisseurDIA fournisseur)
    {
        var flux = new FluxSansFin();
        var faux = new FauxGestionnaire(() =>
        {
            var reponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(flux) };
            reponse.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            return reponse;
        });

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.ReponseTropLongue, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
        Assert.InRange(flux.Lus, FournisseurEnLigne.OctetsMaximumDeLaReponse + 1, FournisseurEnLigne.OctetsMaximumDeLaReponse + 1 + (128 * 1024));
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Une_reponse_juste_sous_la_borne_se_lit(FournisseurDIA fournisseur)
    {
        // Un texte de 20 000 caractères, chacun échappé en six octets : 120 000 octets, sous la borne de 256 Kio.
        var texte = new string('<', DemandeALIA.TailleMaximaleDeLaReponse);
        var faux = new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseReussie(fournisseur, texte).Replace("<", "\\u003c", StringComparison.Ordinal)));

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(DemandeALIA.TailleMaximaleDeLaReponse), TestContext.Current.CancellationToken);

        Assert.Equal(texte, Assert.IsType<ReponseDeLIA.Texte>(rendue).Contenu);
    }

    private static async Task<ReponseDeLIA> RepondreAsync(FournisseurDIA fournisseur, string corps, int caracteres = 200)
    {
        var faux = new FauxGestionnaire(() => Json(HttpStatusCode.OK, corps));
        return await Adaptateur(fournisseur, faux).DemanderAsync(Demande(caracteres), TestContext.Current.CancellationToken);
    }
}

/// <summary>Un corps qui note s'il a été lu, et qui ne rend rien.</summary>
internal sealed class CorpsQuiNeSeLitPas : Stream
{
    public bool Lu { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Lu = true;
        throw new InvalidOperationException("Le corps d'une erreur ne se lit jamais.");
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Lu = true;
        throw new InvalidOperationException("Le corps d'une erreur ne se lit jamais.");
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Un corps qui ne finit jamais, et qui compte les octets rendus.</summary>
internal sealed class FluxSansFin : Stream
{
    public long Lus { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Array.Fill(buffer, (byte)'a', offset, count);
        Lus += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
