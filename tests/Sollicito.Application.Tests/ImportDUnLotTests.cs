using System.Text;
using Sollicito.Application.Candidatures;
using Sollicito.Application.Offres;
using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le geste par lot du cœur (#40, ADR-0015), sur un dépôt en mémoire : l'examen d'un lot n'écrit rien ; l'import refait
/// l'examen dans l'écriture exclusive, puis enregistre, en une seule validation, les offres à importer et les doublons
/// possibles gardés, avec leurs entreprises neuves, créées une fois ; une panne au milieu ne garde rien. Données fictives.
/// </summary>
public sealed class ImportDUnLotTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    // Un fichier qui rencontre la base, où une offre collée attend : « Comptable », chez Lumière Verte SA, et se rencontre
    // lui-même. Rang 0 (ligne 2) à importer ; 1 doublon possible de l'offre collée, même clé d'entreprise et même poste
    // normalisé ; 2 en double du rang 0, la date sous son autre forme ; 3 date refusée ; 4 à importer, entreprise neuve ;
    // 5 doublon possible du rang 4, même clé d'entreprise.
    private const string Fichier =
        "poste;entreprise;lieu;publieeLe\n"
        + "Chauffeur;Lumière Verte SA;Namur;15/11/2026\n"
        + "comptable;LUMIÈRE VERTE SA;Huy;\n"
        + "Chauffeur;Lumière Verte SA;Namur;2026-11-15\n"
        + "Magasinier;Lumière Verte SA;Liège;31/02/2026\n"
        + "Brasseur;Brasserie Lune Rousse;Liège;\n"
        + "Brasseur;Brasserie Lune Rousse SRL;Charleroi;\n";

    [Fact]
    public async Task L_examen_d_un_lot_dit_le_sort_de_chaque_offre_sans_rien_ecrire()
    {
        var (depot, collee) = await PreparerAsync();
        var import = new ImportDesOffres(depot, new HorlogeFixe(), AucunTemoin.Instance);

        var sorts = await import.ExaminerUnLotAsync(Lot(), TestContext.Current.CancellationToken);

        Assert.IsType<SortDUneOffre.AImporter>(sorts[0]);
        var doublonDeLaBase = Assert.IsType<SortDUneOffre.DoublonPossible>(sorts[1]);
        Assert.Equal([collee], doublonDeLaBase.Doublons.EnBase.Select(offre => offre.Id));
        Assert.Empty(doublonDeLaBase.Doublons.RangsDuLot);
        Assert.Equal(0, Assert.IsType<SortDUneOffre.EnDouble>(sorts[2]).RangDOrigine);
        Assert.Equal([SaisieDUneOffre.Champs.PublieeLe], Assert.IsType<SortDUneOffre.Refusee>(sorts[3]).Refus.Keys);
        Assert.IsType<SortDUneOffre.AImporter>(sorts[4]);
        var doublonDuLot = Assert.IsType<SortDUneOffre.DoublonPossible>(sorts[5]);
        Assert.Empty(doublonDuLot.Doublons.EnBase);
        Assert.Equal([4], doublonDuLot.Doublons.RangsDuLot);

        // Rien d'écrit : l'écriture exclusive est quittée sans être validée.
        Assert.Equal(1, depot.Validations);
        Assert.Single(depot.Offres);
        Assert.Single(depot.Entreprises);
    }

    [Fact]
    public async Task L_import_d_un_lot_enregistre_en_une_validation_les_offres_admises_et_les_doublons_gardes()
    {
        var (depot, collee) = await PreparerAsync();
        var temoin = new TemoinNote();
        var import = new ImportDesOffres(depot, new HorlogeFixe(), temoin);
        var lumiereVerte = Assert.Single(depot.Entreprises);

        // Le doublon possible du rang 1 est gardé, pas celui du rang 5 ; garder un rang en double ou refusé est sans effet.
        var sorts = await import.ImporterUnLotAsync(Lot(), new HashSet<int> { 1, 2, 3 }, TestContext.Current.CancellationToken);

        var premiere = Assert.IsType<SortDUneOffre.Importee>(sorts[0]);
        Assert.Null(premiere.Doublons);
        var gardee = Assert.IsType<SortDUneOffre.Importee>(sorts[1]);
        Assert.Equal([collee], gardee.Doublons!.EnBase.Select(offre => offre.Id));
        Assert.IsType<SortDUneOffre.EnDouble>(sorts[2]);
        Assert.IsType<SortDUneOffre.Refusee>(sorts[3]);
        var neuve = Assert.IsType<SortDUneOffre.Importee>(sorts[4]);
        Assert.IsType<SortDUneOffre.DoublonPossible>(sorts[5]);

        // Une seule validation de plus, pour les trois offres : l'écriture le dit au témoin, en comptes seulement.
        Assert.Equal(2, depot.Validations);
        Assert.Equal(["commencée 6", "validée 3"], temoin.Notes);
        Assert.Equal(4, depot.Offres.Count);
        Assert.Equal([premiere.Id, gardee.Id, neuve.Id], depot.Offres.Skip(1).Select(offre => offre.Id));
        Assert.All(depot.Offres.Skip(1), offre => Assert.Equal(SourceDOffre.FichierCsv, offre.Source));

        // L'entreprise de la base est reprise sans renommage ; l'entreprise neuve est créée une fois, sous le nom de la
        // première ligne importée qui la désigne.
        Assert.Equal(["Brasserie Lune Rousse", "Lumière Verte SA"], depot.Entreprises.Select(entreprise => entreprise.Nom).Order(StringComparer.Ordinal));
        Assert.Equal(new int?[] { lumiereVerte.Id, lumiereVerte.Id }, depot.Offres.Skip(1).Take(2).Select(offre => offre.EntrepriseId));
        Assert.Equal((int?)depot.Entreprises.Single(entreprise => entreprise.Nom == "Brasserie Lune Rousse").Id, depot.Offres[3].EntrepriseId);
    }

    [Fact]
    public async Task L_examen_et_l_import_d_un_meme_lot_sur_une_meme_base_disent_la_meme_chose()
    {
        var (depot, _) = await PreparerAsync();
        var import = new ImportDesOffres(depot, new HorlogeFixe(), AucunTemoin.Instance);

        var examen = await import.ExaminerUnLotAsync(Lot(), TestContext.Current.CancellationToken);
        var fait = await import.ImporterUnLotAsync(Lot(), new HashSet<int>(), TestContext.Current.CancellationToken);

        Assert.Equal(
            examen.Select(sort => sort is SortDUneOffre.AImporter ? nameof(SortDUneOffre.Importee) : sort.GetType().Name),
            fait.Select(sort => sort.GetType().Name));

        // Réimporté, le même lot n'importe plus rien : ses lignes sont déjà enregistrées.
        var refait = await import.ImporterUnLotAsync(Lot(), new HashSet<int>(), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(refait, sort => sort is SortDUneOffre.Importee);
        Assert.IsType<SortDUneOffre.DejaEnregistree>(refait[0]);
        Assert.IsType<SortDUneOffre.DejaEnregistree>(refait[4]);
    }

    [Fact]
    public async Task Une_panne_pendant_l_ajout_du_lot_ne_garde_ni_offre_ni_entreprise()
    {
        var (depot, _) = await PreparerAsync();
        var temoin = new TemoinNote();
        var import = new ImportDesOffres(depot, new HorlogeFixe(), temoin);
        depot.PanneAuLot = new IOException("Panne simulée.");

        await Assert.ThrowsAsync<IOException>(() => import.ImporterUnLotAsync(Lot(), new HashSet<int> { 1 }, TestContext.Current.CancellationToken));

        Assert.Equal(1, depot.Validations);
        Assert.Single(depot.Offres);
        Assert.Single(depot.Entreprises);
        Assert.Equal(["commencée 6"], temoin.Notes);
    }

    // Le dépôt, où l'offre collée « Comptable », chez Lumière Verte SA, est enregistrée par le même cœur ; et son identifiant.
    private static async Task<(DepotEnMemoire Depot, int Collee)> PreparerAsync()
    {
        var depot = new DepotEnMemoire();
        var collee = new ConnecteurDuTexteColle().Recevoir(new SaisieDUneOffre("Offre collée fictive.", "Lumière Verte SA", "Comptable", "Liège", null, null));
        var sorts = await new ImportDesOffres(depot, new HorlogeFixe(), AucunTemoin.Instance).ImporterUnLotAsync(collee, new HashSet<int>(), TestContext.Current.CancellationToken);
        return (depot, Assert.IsType<SortDUneOffre.Importee>(Assert.Single(sorts)).Id);
    }

    private static IReadOnlyList<OffreRecue> Lot()
    {
        var fichier = Assert.IsType<LectureDuFichier.Lu>(LectureDUnFichierCsv.Lire(Encoding.UTF8.GetBytes(Fichier))).Fichier;
        return [.. new ConnecteurDuFichierCsv().RecevoirLesLignes(fichier).Select(ligne => ligne.Offre)];
    }

    private sealed class HorlogeFixe : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Maintenant;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TemoinNote : ITemoinDeLImport
    {
        public List<string> Notes { get; } = [];

        public void EcritureCommencee(int offres) => Notes.Add($"commencée {offres}");

        public void EcritureValidee(int importees) => Notes.Add($"validée {importees}");
    }

    // Un dépôt en mémoire : une écriture garde ses ajouts à part jusqu'à sa validation, et les oublie sinon. Les
    // identifiants se donnent à l'ajout, comme le fait la base.
    private sealed class DepotEnMemoire : IDepotDesOffres
    {
        private int _prochainIdentifiant = 1;

        public List<Entreprise> Entreprises { get; } = [];

        public List<Offre> Offres { get; } = [];

        public int Validations { get; private set; }

        public Exception? PanneAuLot { get; set; }

        public Task<IEcritureDesOffres> CommencerUneEcritureAsync(CancellationToken annulation) => Task.FromResult<IEcritureDesOffres>(new Ecriture(this));

        public Task<FicheDUneOffre?> LireAsync(int id, CancellationToken annulation) => throw new NotSupportedException();

        public Task<IReadOnlyList<OffreListee>> ListerAsync(CancellationToken annulation) => throw new NotSupportedException();

        private void Numeroter(object entite) => entite.GetType().GetProperty("Id")!.SetValue(entite, _prochainIdentifiant++);

        private sealed class Ecriture(DepotEnMemoire depot) : IEcritureDesOffres
        {
            private readonly List<Entreprise> _entreprises = [];
            private readonly List<Offre> _offres = [];

            private IEnumerable<Entreprise> ToutesLesEntreprises => depot.Entreprises.Concat(_entreprises);

            private IEnumerable<Offre> ToutesLesOffres => depot.Offres.Concat(_offres);

            public Task<Entreprise?> TrouverLEntrepriseAsync(string cle, CancellationToken annulation) =>
                Task.FromResult(ToutesLesEntreprises.FirstOrDefault(entreprise => entreprise.Cle == cle));

            public Task<OffreDejaEnregistree?> TrouverParEmpreinteAsync(SourceDOffre source, string empreinte, CancellationToken annulation) =>
                Task.FromResult(ToutesLesOffres.Where(offre => offre.Source == source && offre.Empreinte == empreinte).Select(offre => new OffreDejaEnregistree(offre.Id, offre.AjouteeLe)).FirstOrDefault());

            public Task<IReadOnlyList<OffreSignalee>> ListerLesDoublonsPossiblesAsync(int entrepriseId, string posteNormalise, int? sauf, CancellationToken annulation)
            {
                var nom = ToutesLesEntreprises.Single(entreprise => entreprise.Id == entrepriseId).Nom;
                return Task.FromResult<IReadOnlyList<OffreSignalee>>(
                [
                    .. ToutesLesOffres
                        .Where(offre => offre.EntrepriseId == entrepriseId && offre.PosteNormalise == posteNormalise && offre.Id != sauf)
                        .OrderBy(offre => offre.Id)
                        .Select(offre => new OffreSignalee(offre.Id, offre.Poste, new EntrepriseConnue(entrepriseId, nom), offre.Lieu, offre.AjouteeLe, null)),
                ]);
            }

            public Task<Offre?> TrouverLOffreAsync(int id, CancellationToken annulation) => Task.FromResult(ToutesLesOffres.FirstOrDefault(offre => offre.Id == id));

            public Task AjouterAsync(Entreprise entreprise, CancellationToken annulation)
            {
                depot.Numeroter(entreprise);
                _entreprises.Add(entreprise);
                return Task.CompletedTask;
            }

            public Task AjouterAsync(Offre offre, CancellationToken annulation)
            {
                depot.Numeroter(offre);
                _offres.Add(offre);
                return Task.CompletedTask;
            }

            public Task AjouterAsync(IReadOnlyList<Offre> offres, CancellationToken annulation)
            {
                if (depot.PanneAuLot is { } panne)
                {
                    throw panne;
                }

                foreach (var offre in offres)
                {
                    depot.Numeroter(offre);
                    _offres.Add(offre);
                }

                return Task.CompletedTask;
            }

            public Task ValiderAsync(CancellationToken annulation)
            {
                depot.Entreprises.AddRange(_entreprises);
                depot.Offres.AddRange(_offres);
                _entreprises.Clear();
                _offres.Clear();
                depot.Validations++;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
