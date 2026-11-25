using System.Net;
using System.Security.Authentication;
using Sollicito.Application.IA;
using Sollicito.Infrastructure.IA;
using Sollicito.Infrastructure.IA.EnLigne;
using static Sollicito.Infrastructure.Tests.IA.OutilsDesFournisseurs;

namespace Sollicito.Infrastructure.Tests.IA;

/// <summary>
/// Le déroulé d'une demande en ligne (#48, ADR-0017) : la clé lue au moment de l'appel, pour ce seul fournisseur ; une
/// demande à la fois ; une durée qui couvre la lecture du corps ; aucune nouvelle tentative ; aucune exception, sauf
/// l'annulation par l'appelant ; et un journal qui dit ce qui s'est passé sans la clé, la demande ni la réponse.
/// </summary>
public sealed class AppelsEnLigneTests
{
    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Sans_cle_lisible_aucune_requete_ne_part(FournisseurDIA fournisseur)
    {
        var faux = new FauxGestionnaire();
        var absente = new SourceDeTest(_ => LectureDeLaCle.Absente);
        var illisible = new SourceDeTest(_ => LectureDeLaCle.Illisible("CryptographicException"));

        var sansCle = await Adaptateur(fournisseur, faux, absente).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        var cleIllisible = await Adaptateur(fournisseur, faux, illisible).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(RaisonDIndisponibilite.CleAbsente, Assert.IsType<ReponseDeLIA.Indisponible>(sansCle).Raison);
        Assert.Equal(RaisonDIndisponibilite.CleIllisible, Assert.IsType<ReponseDeLIA.Indisponible>(cleIllisible).Raison);
        Assert.Empty(faux.Recues);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task La_cle_se_lit_a_chaque_appel_pour_ce_seul_fournisseur(FournisseurDIA fournisseur)
    {
        var valeurs = new Queue<string>([CleFictive(fournisseur), CleFictive(fournisseur).Replace("0001", "0002", StringComparison.Ordinal)]);
        var cles = new SourceDeTest(demande => LectureDeLaCle.Lisible(Cle(demande, valeurs.Dequeue())));
        var faux = new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseReussie(fournisseur)));
        var adaptateur = Adaptateur(fournisseur, faux, cles);

        await adaptateur.DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        await adaptateur.DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal([fournisseur, fournisseur], cles.Demandes);
        var enTete = fournisseur == FournisseurDIA.Claude ? "x-api-key" : "Authorization";
        Assert.EndsWith("0001", Assert.Single(faux.Recues[0].Valeurs(enTete)), StringComparison.Ordinal);
        Assert.EndsWith("0002", Assert.Single(faux.Recues[1].Valeurs(enTete)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_seule_demande_a_la_fois_pour_tous_les_fournisseurs_sans_requete_pour_la_seconde()
    {
        var porte = new PorteDesDemandes();
        var liberer = new TaskCompletionSource();
        var arrivee = new TaskCompletionSource();
        var lente = new FauxGestionnaire(async (_, annulation) =>
        {
            arrivee.TrySetResult();
            await liberer.Task.WaitAsync(annulation);
            return Json(HttpStatusCode.OK, ReponseDeClaude());
        });
        var autre = new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseDOpenAI()));
        var claude = Adaptateur(FournisseurDIA.Claude, lente, porte: porte);
        var openAI = Adaptateur(FournisseurDIA.OpenAI, autre, porte: porte);

        var premiere = claude.DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        await arrivee.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var seconde = await openAI.DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        var troisieme = await claude.DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        liberer.SetResult();

        Assert.Equal(SorteDEchec.UneDemandeEnCours, Assert.IsType<ReponseDeLIA.Echec>(seconde).Sorte);
        Assert.Equal(SorteDEchec.UneDemandeEnCours, Assert.IsType<ReponseDeLIA.Echec>(troisieme).Sorte);
        Assert.IsType<ReponseDeLIA.Texte>(await premiere);
        Assert.Single(lente.Recues);
        Assert.Empty(autre.Recues);
        // La porte est rendue : la demande suivante part.
        Assert.IsType<ReponseDeLIA.Texte>(await openAI.DemanderAsync(Demande(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task La_porte_est_rendue_apres_un_echec_et_une_annulation(FournisseurDIA fournisseur)
    {
        var porte = new PorteDesDemandes();
        var enPanne = new FauxGestionnaire(() => throw new HttpRequestException("panne"));
        await Adaptateur(fournisseur, enPanne, porte: porte).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        using var annulation = new CancellationTokenSource();
        var attente = new FauxGestionnaire(async (_, jeton) =>
        {
            await annulation.CancelAsync();
            await Task.Delay(Timeout.Infinite, jeton);
            return Json(HttpStatusCode.OK, "{}");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Adaptateur(fournisseur, attente, porte: porte).DemanderAsync(Demande(), annulation.Token));

        Assert.True(porte.Entrer());
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Un_fournisseur_qui_se_tait_depasse_le_delai_sans_nouvelle_tentative(FournisseurDIA fournisseur)
    {
        var faux = new FauxGestionnaire(async (_, annulation) =>
        {
            await Task.Delay(Timeout.Infinite, annulation);
            return Json(HttpStatusCode.OK, "{}");
        });

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(duree: TimeSpan.FromMilliseconds(300)), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.DelaiDepasse, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
        Assert.Single(faux.Recues);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Le_delai_couvre_la_lecture_du_corps(FournisseurDIA fournisseur)
    {
        var faux = new FauxGestionnaire(() =>
        {
            var reponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CorpsQuiSeTait()) };
            reponse.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            return reponse;
        });

        var rendue = await Adaptateur(fournisseur, faux).DemanderAsync(Demande(duree: TimeSpan.FromMilliseconds(300)), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.DelaiDepasse, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task L_annulation_par_l_appelant_leve_son_exception_pendant_l_appel(FournisseurDIA fournisseur)
    {
        using var annulation = new CancellationTokenSource();
        var faux = new FauxGestionnaire(async (_, jeton) =>
        {
            await annulation.CancelAsync();
            await Task.Delay(Timeout.Infinite, jeton);
            return Json(HttpStatusCode.OK, "{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Adaptateur(fournisseur, faux).DemanderAsync(Demande(), annulation.Token));
    }

    public static TheoryData<FournisseurDIA, int> PannesDuTransport
    {
        get
        {
            var donnees = new TheoryData<FournisseurDIA, int>();
            foreach (var fournisseur in new[] { FournisseurDIA.Claude, FournisseurDIA.OpenAI })
            {
                for (var panne = 0; panne < 4; panne++)
                {
                    donnees.Add(fournisseur, panne);
                }
            }

            return donnees;
        }
    }

    [Theory]
    [MemberData(nameof(PannesDuTransport))]
    public async Task Une_panne_du_transport_rend_injoignable_sans_exception_ni_nouvelle_tentative(FournisseurDIA fournisseur, int panne)
    {
        Exception erreur = panne switch
        {
            0 => new HttpRequestException("Nom introuvable : api.exemple " + PartieReconnaissable),
            1 => new HttpRequestException("TLS", new AuthenticationException("Certificat refusé " + PartieReconnaissable)),
            2 => new IOException("Connexion coupée " + PartieReconnaissable),
            _ => new InvalidOperationException("Autre " + PartieReconnaissable),
        };
        var journal = new JournalDeTest();
        var faux = new FauxGestionnaire(() => throw erreur);

        var rendue = await Adaptateur(fournisseur, faux, journal: journal).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        Assert.Equal(SorteDEchec.Injoignable, Assert.IsType<ReponseDeLIA.Echec>(rendue).Sorte);
        Assert.Single(faux.Recues);
        var entree = Assert.Single(journal.Entrees);
        Assert.Contains(erreur.GetType().Name, entree, StringComparison.Ordinal);
        Assert.DoesNotContain(PartieReconnaissable, entree, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FournisseurDIA.Claude)]
    [InlineData(FournisseurDIA.OpenAI)]
    public async Task Le_journal_dit_le_fournisseur_l_issue_le_statut_et_la_duree_jamais_la_cle_la_demande_ni_la_reponse(FournisseurDIA fournisseur)
    {
        var journal = new JournalDeTest();
        var porte = new PorteDesDemandes();
        var reussie = new FauxGestionnaire(() => Json(HttpStatusCode.OK, ReponseReussie(fournisseur, "SOLLICITO-REPONSE-48")));
        var refusee = new FauxGestionnaire(() => Erreur(HttpStatusCode.Unauthorized));
        var coupee = new FauxGestionnaire(() => throw new HttpRequestException(PartieReconnaissable));

        await Adaptateur(fournisseur, reussie, porte: porte, journal: journal).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        await Adaptateur(fournisseur, refusee, porte: porte, journal: journal).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        await Adaptateur(fournisseur, coupee, porte: porte, journal: journal).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        await Adaptateur(fournisseur, reussie, new SourceDeTest(_ => LectureDeLaCle.Absente), porte, journal).DemanderAsync(Demande(), TestContext.Current.CancellationToken);
        porte.Entrer();
        await Adaptateur(fournisseur, reussie, porte: porte, journal: journal).DemanderAsync(Demande(), TestContext.Current.CancellationToken);

        var entrees = journal.Entrees;
        Assert.Equal(5, entrees.Count);
        Assert.StartsWith("Information 91 : ", entrees[0], StringComparison.Ordinal);
        Assert.Contains($"{fournisseur} a répondu (statut 200)", entrees[0], StringComparison.Ordinal);
        Assert.StartsWith("Warning 92 : ", entrees[1], StringComparison.Ordinal);
        Assert.Contains("échec CleRefusee (statut 401)", entrees[1], StringComparison.Ordinal);
        Assert.StartsWith("Warning 93 : ", entrees[2], StringComparison.Ordinal);
        Assert.Contains("HttpRequestException", entrees[2], StringComparison.Ordinal);
        Assert.StartsWith("Information 95 : ", entrees[3], StringComparison.Ordinal);
        Assert.Contains("CleAbsente", entrees[3], StringComparison.Ordinal);
        Assert.StartsWith("Information 94 : ", entrees[4], StringComparison.Ordinal);
        foreach (var interdit in new[] { PartieReconnaissable, MessageDuFournisseur, "SOLLICITO-CONSIGNE-48", "SOLLICITO-DONNEE-48", "SOLLICITO-REPONSE-48", "api.anthropic.com", "api.openai.com" })
        {
            Assert.All(entrees, entree => Assert.DoesNotContain(interdit, entree, StringComparison.Ordinal));
        }
    }

    // Un corps dont les en-têtes sont arrivés, et qui ne rend jamais ses octets.
    private sealed class CorpsQuiSeTait : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
