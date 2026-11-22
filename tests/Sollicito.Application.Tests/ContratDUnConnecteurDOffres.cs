using Sollicito.Application.Offres;
using Sollicito.Domain.Offres;

namespace Sollicito.Application.Tests;

/// <summary>
/// Le banc de tests de contrat des connecteurs d'offres (ADR-0001, D6 et D11 ; #34, absorbée par #36) : chaque
/// adaptateur en hérite, et le passe sur des entrées fictives. Un connecteur ne valide rien, c'est l'affaire du cœur ;
/// il donne sa source à chaque offre, annonce les parties de son empreinte, et passe ses champs tels qu'il les a reçus :
/// ni retouchés, ni lus comme du HTML. L'import CSV (#40) en héritera à son tour.
/// </summary>
/// <typeparam name="TEntree">Ce que la source donne au connecteur.</typeparam>
public abstract class ContratDUnConnecteurDOffres<TEntree>
{
    /// <summary>Des champs piégés : balises, script, entités, accents, blancs de bord, sauts de ligne, ponctuation.</summary>
    public static readonly ChampsRecus Pieges = new(
        "  <h1>Offre</h1>\r\n<script>alert('fictif')</script>\n&amp; « Œuvres » \t",
        " <img src=x onerror=alert(1)> SA ",
        "Développeur <b>.NET</b> (H/F)",
        "Liège & Namur",
        "2026-11-18",
        "javascript:alert('fictif')");

    /// <summary>Le connecteur essayé.</summary>
    protected abstract IConnecteurDOffres<TEntree> Connecteur { get; }

    /// <summary>La source qu'il doit donner.</summary>
    protected abstract SourceDOffre SourceAttendue { get; }

    /// <summary>Une entrée de la source qui porte ces champs, pour une seule offre.</summary>
    protected abstract TEntree EntreeQuiPorte(ChampsRecus champs);

    /// <summary>Les parties de l'empreinte que le connecteur doit annoncer pour une offre de ces champs.</summary>
    protected abstract IReadOnlyList<string> PartiesAttendues(ChampsRecus champs);

    /// <summary>Une entrée vide : rien de saisi, ou un fichier sans ligne.</summary>
    protected abstract TEntree EntreeVide();

    [Fact]
    public void Chaque_offre_recue_porte_la_source_du_connecteur()
    {
        var offres = Connecteur.Recevoir(EntreeQuiPorte(Pieges));

        Assert.Equal(SourceAttendue, Connecteur.Source);
        Assert.NotEmpty(offres);
        Assert.All(offres, offre => Assert.Equal(SourceAttendue, offre.Source));
    }

    [Fact]
    public void Les_champs_passent_tels_quels_ni_retouches_ni_lus_comme_du_html()
    {
        var offre = Assert.Single(Connecteur.Recevoir(EntreeQuiPorte(Pieges)));

        Assert.Equal(Pieges, offre.Champs);
    }

    [Fact]
    public void Les_parties_de_l_empreinte_sont_celles_du_format_de_la_source()
    {
        var offre = Assert.Single(Connecteur.Recevoir(EntreeQuiPorte(Pieges)));

        Assert.Equal(PartiesAttendues(Pieges), offre.PartiesDeLEmpreinte);
        Assert.NotEmpty(offre.PartiesDeLEmpreinte);
        // Le cœur calcule l'empreinte sur ces parties : elle doit s'y calculer, sans lever.
        Assert.True(EmpreinteDUneOffre.APourForme(EmpreinteDUneOffre.De(offre.PartiesDeLEmpreinte)));
    }

    [Fact]
    public void Une_entree_vide_ne_leve_rien_et_ne_fait_aucune_offre_valide_d_office()
    {
        var offres = Connecteur.Recevoir(EntreeVide());

        Assert.All(offres, offre => Assert.Equal(SourceAttendue, offre.Source));
    }
}

/// <summary>Le premier connecteur, l'offre collée (#36), au banc de contrat.</summary>
public sealed class ConnecteurDuTexteColleTests : ContratDUnConnecteurDOffres<SaisieDUneOffre>
{
    protected override IConnecteurDOffres<SaisieDUneOffre> Connecteur { get; } = new ConnecteurDuTexteColle();

    protected override SourceDOffre SourceAttendue => SourceDOffre.TexteColle;

    protected override SaisieDUneOffre EntreeQuiPorte(ChampsRecus champs) =>
        new(champs.Texte, champs.Entreprise, champs.Poste, champs.Lieu, champs.PublieeLe, champs.Adresse);

    // Une seule partie : le texte collé, tel quel.
    protected override IReadOnlyList<string> PartiesAttendues(ChampsRecus champs) => [champs.Texte!];

    protected override SaisieDUneOffre EntreeVide() => new(null, null, null, null, null, null);

    [Fact]
    public void L_accord_garder_les_deux_n_entre_pas_dans_l_offre_recue()
    {
        var offre = Assert.Single(Connecteur.Recevoir(new SaisieDUneOffre("Offre fictive.", null, "Comptable", null, null, null, GarderLesDeux: true)));

        Assert.Equal(new ChampsRecus("Offre fictive.", null, "Comptable", null, null, null), offre.Champs);
    }

    [Fact]
    public void Un_texte_absent_donne_une_partie_vide_que_le_coeur_refusera()
    {
        var offre = Assert.Single(Connecteur.Recevoir(EntreeVide()));

        Assert.Equal([""], offre.PartiesDeLEmpreinte);
        Assert.Null(offre.Champs.Texte);
    }
}
