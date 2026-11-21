namespace Sollicito.Application.Sauvegardes;

/// <summary>
/// L'état des sauvegardes, tel que la section « Sauvegardes » et l'alerte du tableau de bord le lisent (#26). Il se tient
/// en mémoire, mis à jour à chaque copie : le lire ne touche pas au dossier, qu'un support débranché pourrait bloquer.
/// </summary>
/// <param name="Dossier">Le dossier des copies du jour.</param>
/// <param name="ParDefaut">Vrai si c'est le dossier par défaut, sur le disque de la base.</param>
/// <param name="DossierParDefaut">Le dossier par défaut, où vont aussi les copies d'avant une mise à jour.</param>
/// <param name="DerniereCopie">L'instant de la dernière copie du jour réussie, en UTC ; <c>null</c> s'il n'y en a aucune.</param>
/// <param name="Copies">Les copies gardées : celles du jour, puis celles d'avant une mise à jour.</param>
/// <param name="ReglageIllisible">Vrai si le réglage du dossier n'a pas pu être relu : le dossier par défaut sert alors.</param>
/// <param name="Echec">La dernière copie manquée, depuis la dernière réussie ; <c>null</c> si tout va bien.</param>
/// <param name="Verifie">Faux tant que la première vérification du lancement n'est pas faite.</param>
public sealed record EtatDesSauvegardes(
    string Dossier,
    bool ParDefaut,
    string DossierParDefaut,
    DateTime? DerniereCopie,
    IReadOnlyList<CopieGardee> Copies,
    bool ReglageIllisible,
    EchecDUneCopie? Echec,
    bool Verifie);

/// <summary>Une copie gardée, que l'utilisateur peut restaurer (#28).</summary>
/// <param name="Nom">Son nom de fichier, sans le dossier.</param>
/// <param name="Sorte">Copie du jour, de ce poste ou d'une autre installation, ou d'avant une mise à jour ou une restauration.</param>
/// <param name="FaiteLe">L'instant où elle a été écrite, en UTC.</param>
/// <param name="Lieu">Le dossier où elle vit : celui des copies, ou celui par défaut.</param>
/// <param name="Id">
/// Son identifiant, que la page renvoie pour la restaurer : tiré du lieu, du nom et de l'instant, sans aucun chemin
/// (<see cref="IdentifiantsDesCopies"/>).
/// </param>
public sealed record CopieGardee(string Nom, SorteDeCopie Sorte, DateTime FaiteLe, LieuDUneCopie Lieu, string Id);

/// <summary>Les sortes de copies.</summary>
public enum SorteDeCopie
{
    /// <summary>La copie d'un jour d'utilisation, gardée parmi les sept dernières.</summary>
    DuJour,

    /// <summary>La copie faite avant une mise à jour de la base, gardée à part.</summary>
    AvantMiseAJour,

    /// <summary>
    /// La copie d'un jour d'une autre installation qui partage le dossier : un autre poste, ou celui-ci avant une
    /// réinstallation (#28). Elle se restaure, mais n'entre ni dans la rotation ni dans la dernière copie.
    /// </summary>
    DuJourDUneAutreInstallation,

    /// <summary>La copie de la base faite avant une restauration, gardée à part : le retour en arrière (#28).</summary>
    AvantRestauration,
}

/// <summary>Les deux dossiers où vivent les copies.</summary>
public enum LieuDUneCopie
{
    /// <summary>Le dossier des copies du jour, choisi ou par défaut.</summary>
    DossierDesCopies,

    /// <summary>Le dossier par défaut, quand un autre dossier est choisi : les copies d'avant, et celles d'avant le choix.</summary>
    DossierParDefaut,
}

/// <summary>Des copies manquées, depuis la dernière réussie.</summary>
/// <param name="Motif">Ce qui a empêché la dernière.</param>
/// <param name="Depuis">L'instant du premier essai manqué de la série, en UTC.</param>
public sealed record EchecDUneCopie(MotifDEchec Motif, DateTime Depuis);

/// <summary>Ce qui empêche une copie.</summary>
public enum MotifDEchec
{
    /// <summary>Le dossier choisi manque : clé retirée, partage débranché, dossier effacé.</summary>
    DossierIntrouvable,

    /// <summary>Le dossier est là, mais la copie n'a pas pu s'y écrire ni s'y vérifier.</summary>
    CopieImpossible,
}

/// <summary>Ce que rend le choix d'un dossier : il devient celui des sauvegardes, ou l'ancien reste.</summary>
public enum IssueDuChoix
{
    /// <summary>Une copie s'y est faite : c'est désormais le dossier des sauvegardes.</summary>
    Choisi,

    /// <summary>La copie n'a pas pu s'y faire : l'ancien dossier reste.</summary>
    Refuse,
}
