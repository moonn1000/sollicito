/** Le seul en-tête où l'API lit le jeton (ADR-0001, D9) : jamais un cookie, jamais l'adresse. */
export const EN_TETE_DU_JETON = "Sollicito-Jeton";

/** Réponse de `GET /api/etat`. */
export interface EtatDeLApplication {
  statut: string;
}

/** Le statut d'une candidature, tel que l'API l'écrit (modèle de domaine, « Statuts d'une candidature »). */
export type StatutDeCandidature =
  | "Envoyee"
  | "Entretien"
  | "Proposition"
  | "Acceptee"
  | "Refusee"
  | "Retiree"
  | "SansReponse";

/** Une entreprise déjà enregistrée : son nom affiché est celui de la première saisie. */
export interface EntrepriseConnue {
  id: number;
  nom: string;
}

/** Une candidature, telle que l'API la rend. */
export interface Candidature {
  id: number;
  entreprise: EntrepriseConnue;
  poste: string;
  /** L'instant de l'envoi, en UTC : `2026-03-27T22:30:00Z`. */
  envoyeeLe: string;
  statut: StatutDeCandidature;
}

/** Une candidature telle que l'écran de saisie l'envoie, champ par champ, sans rien convertir. */
export interface SaisieDUneCandidature {
  entreprise: string;
  poste: string;
  /** La date et l'heure telles que l'utilisateur les a tapées, sans décalage : `2026-03-27T23:30`. */
  envoyeeLe: string;
}

/** Un champ de la saisie. */
export type ChampDeLaSaisie = keyof SaisieDUneCandidature;

/** Les messages de refus de l'API, champ par champ. */
export type RefusDeLaSaisie = Partial<Record<ChampDeLaSaisie, string[]>>;

/** Ce que rend l'enregistrement d'une candidature. */
export type ResultatDeLEnregistrement =
  | { enregistree: true; candidature: Candidature }
  | { enregistree: false; refus: RefusDeLaSaisie };

/**
 * Ce que rend la modification d'une candidature : sa fiche à jour ; les refus de ses champs ; ou l'entreprise dont le
 * nom saisi corrigerait le nom affiché, le nombre de candidatures concernées, celle qu'on modifie comprise, et celui des
 * offres qui la désignent (#38) : la modification attend alors l'accord de l'utilisateur (ADR-0007).
 */
export type ResultatDeLaModification =
  | { issue: "modifiee"; fiche: FicheDUneCandidature }
  | { issue: "refusee"; refus: RefusDeLaSaisie }
  | { issue: "nomACorriger"; entreprise: EntrepriseConnue; candidatures: number; offres: number };

/** Le type d'une étape de l'historique, tel que l'API l'écrit (modèle de domaine, « Types énumérés »). */
export type TypeDEtape = "Appel" | "Entretien" | "Echange" | "Autre";

/** Un changement de statut : un passage, ou la correction d'un changement précédent, qu'elle désigne. */
export interface ChangementDeStatutLu {
  sorte: "changementDeStatut";
  id: number;
  /** L'instant du changement, en UTC, donné par l'horloge. */
  survenuLe: string;
  ancien: StatutDeCandidature;
  nouveau: StatutDeCandidature;
  /** Pour une correction, l'identifiant du changement corrigé ; rien pour un passage. */
  corrige: number | null;
  /** Vrai si une correction l'a corrigé depuis : il reste dans l'historique, marqué comme tel. */
  estCorrige: boolean;
}

/** Une étape, qui ne change pas le statut : un appel, un entretien, un échange ; notée, ou la correction d'une autre. */
export interface EtapeLue {
  sorte: "etape";
  id: number;
  /** L'instant de l'étape, en UTC, donné par l'utilisateur. */
  survenuLe: string;
  type: TypeDEtape;
  note: string | null;
  /** Pour une correction (#25), l'identifiant de l'étape corrigée ; rien pour une étape notée. */
  corrige: number | null;
  /** Vrai si une correction l'a corrigée depuis : elle reste dans l'historique, marquée comme telle. */
  estCorrige: boolean;
}

/** L'état d'une relance, tel que l'API l'écrit (#14) : à faire, faite ou abandonnée. */
export type EtatDeRelance = "AFaire" | "Faite" | "Abandonnee";

/** Un contact d'une entreprise (#14) : un nom et une adresse électronique. */
export interface ContactConnu {
  id: number;
  nom: string;
  email: string;
}

/** Une relance d'une candidature (#14). */
export interface RelanceLue {
  id: number;
  /** Le jour de l'échéance, sans heure ni fuseau : `2026-04-03`. */
  echeance: string;
  etat: EtatDeRelance;
  /** Son destinataire, ou rien : le brouillon s'ouvre alors sans destinataire. */
  destinataire: ContactConnu | null;
}

/** La clôture d'une relance, faite ou abandonnée, dans l'historique (#14). */
export interface ClotureDeRelanceLue {
  sorte: "clotureDeRelance";
  id: number;
  /** L'instant de la clôture, en UTC, donné par l'horloge. */
  survenuLe: string;
  /** L'identifiant de la relance close. */
  relance: number;
  etat: EtatDeRelance;
  echeance: string;
  destinataire: ContactConnu | null;
}

/** Un événement de l'historique : sa sorte dit lequel. */
export type EvenementDeLHistorique = ChangementDeStatutLu | EtapeLue | ClotureDeRelanceLue;

/** Ce que ferait la correction du dernier changement de statut. */
export interface CorrectionPossible {
  /** L'identifiant du changement qu'elle corrigerait. */
  changement: number;
  /** Le statut présent. */
  de: StatutDeCandidature;
  /** Le statut où elle ramènerait la candidature. */
  retourA: StatutDeCandidature;
}

/** La fiche d'une candidature : son statut, ce qu'on peut en faire, et son historique. */
export interface FicheDUneCandidature extends Candidature {
  /** Les statuts où elle peut passer, dans l'ordre du modèle de domaine ; aucun si elle est close. */
  passagesPermis: StatutDeCandidature[];
  /** La correction du dernier changement, si un changement est à corriger. */
  correction: CorrectionPossible | null;
  /** Ses événements, du plus ancien au plus récent. */
  historique: EvenementDeLHistorique[];
  /** Ses relances, dans l'ordre de leur planification : au plus une à faire (#14). */
  relances: RelanceLue[];
  /** L'identifiant de l'offre d'où elle vient (#38), ou rien. */
  offre: number | null;
}

/** Un contact tel que la fiche l'envoie, champ par champ (#14). */
export interface SaisieDUnContact {
  nom: string;
  email: string;
}

/** Un champ de la saisie d'un contact. */
export type ChampDuContact = keyof SaisieDUnContact;

/** Les messages de refus de l'API, champ par champ. */
export type RefusDuContact = Partial<Record<ChampDuContact, string[]>>;

/** Ce que rend l'ajout ou la modification d'un contact : le contact enregistré, ou les refus de ses champs. */
export type ResultatDUnContact = { issue: "enregistre"; contact: ContactConnu } | { issue: "refuse"; refus: RefusDuContact };

/** Une relance telle que la fiche l'envoie (#14) : un délai en jours depuis l'envoi, et un destinataire ou personne. */
export interface SaisieDUneRelance {
  delai: number;
  destinataire: number | null;
}

/** Un champ de la saisie d'une relance. */
export type ChampDeLaRelance = keyof SaisieDUneRelance;

/** Les messages de refus de l'API, champ par champ. */
export type RefusDeLaRelance = Partial<Record<ChampDeLaRelance, string[]>>;

/**
 * Ce que rend une relance planifiée ou changée : la fiche à jour ; les refus de ses champs ; ou le motif d'un refus que
 * l'état de la candidature explique (close, ou qui a déjà sa relance à faire).
 */
export type ResultatDeLaRelance =
  | { issue: "faite"; fiche: FicheDUneCandidature }
  | { issue: "refusee"; refus: RefusDeLaRelance }
  | { issue: "nonPermise"; motif: string };

/** Une relance à faire que signale le tableau de bord (#14). */
export interface RelanceSignalee {
  id: number;
  echeance: string;
  candidature: { id: number; poste: string; entreprise: EntrepriseConnue };
  destinataire: ContactConnu | null;
}

/** Les relances que signale le tableau de bord, calculées par l'API en date locale. */
export interface RelancesDuTableauDeBord {
  /** Le jour local de la lecture : `2026-11-18`. */
  aujourdhui: string;
  /** Les relances à faire dont l'échéance tombe aujourd'hui ou avant. */
  echues: RelanceSignalee[];
  /** Celles dont l'échéance tombe dans les sept jours qui suivent. */
  deLaSemaine: RelanceSignalee[];
}

/** Le délai par défaut des relances, réglé dans reglages.json (#14, ADR-0011). */
export interface ReglageDuDelai {
  /** Le délai qui s'applique, en jours : celui du réglage, ou les 7 jours d'usine. */
  delaiParDefaut: number;
  /** Faux si le délai enregistré n'a pas pu être relu. */
  delaiLisible: boolean;
  /** Faux si le fichier des réglages n'a pas pu être relu en entier : le délai ne s'y écrit pas. */
  fichierLisible: boolean;
}

/** Le fournisseur d'IA (#43, ADR-0016), par son nom exact : aucun, Claude ou OpenAI par la clé de l'utilisateur, ou un modèle local. */
export type FournisseurDIA = "Aucun" | "Claude" | "OpenAI" | "Ollama";

/** La clé du fournisseur réglé, sans jamais la clé elle-même : sans objet, aucune, enregistrée, ou illisible sur ce poste. */
export type EtatDeLaCle = "SansObjet" | "Aucune" | "Enregistree" | "Illisible";

/** L'état de l'IA (#43), tel que l'API le rend : jamais la clé. */
export interface EtatDeLIA {
  /** Le fournisseur qui s'applique : celui du réglage, ou aucun s'il ne se lit pas. */
  fournisseur: FournisseurDIA;
  /** Faux tant qu'aucun choix n'est enregistré : la carte du tableau de bord le propose. */
  choisi: boolean;
  /** Faux si le choix de l'IA n'a pas pu être relu : aucune IA s'applique alors. */
  reglageLisible: boolean;
  /** Faux si le fichier des réglages n'a pas pu être relu en entier : rien ne s'y écrit. */
  fichierLisible: boolean;
  /** La clé du fournisseur qui s'applique. */
  cle: EtatDeLaCle;
}

/** Ce que rend un choix de fournisseur, ou le retrait de la clé : l'état à jour, ou le motif d'un refus. */
export type ResultatDeLIA = { issue: "fait"; etat: EtatDeLIA } | { issue: "refuse"; motif: string };

/**
 * Ce que rend l'essai de la clé (#48) : l'essai est parti, réussi ou non, avec le texte à montrer ; ou il a été refusé
 * sans requête, avec son motif (aucune clé prête, une demande déjà en cours). Jamais la réponse du fournisseur.
 */
export type ResultatDeLEssai = { issue: "fait"; reussi: boolean; message: string } | { issue: "refuse"; motif: string };

/** Les champs d'une offre que l'IA propose (#46) : chacun un texte, ou rien s'il n'a pas été trouvé ou a été écarté. */
export interface ChampsProposes {
  entreprise: string | null;
  poste: string | null;
  lieu: string | null;
  /** `AAAA-MM-JJ`. */
  publieeLe: string | null;
  adresse: string | null;
}

/**
 * Ce que rend l'extraction d'une offre collée (#46) : les champs proposés ; l'échec et son texte ; le refus du texte,
 * avant toute demande ; ou le motif d'un refus sans requête (aucune IA prête, une demande en cours). Jamais le texte de
 * l'IA, ni celui du fournisseur.
 */
export type ResultatDeLExtraction =
  | { issue: "proposee"; champs: ChampsProposes }
  | { issue: "echec"; message: string }
  | { issue: "texteRefuse"; refus: string[] }
  | { issue: "refuse"; motif: string };

/** Ce que rend l'enregistrement d'une clé : l'état à jour, le refus du champ, ou le motif d'un refus. */
export type ResultatDeLaCle =
  | { issue: "enregistree"; etat: EtatDeLIA }
  | { issue: "refusee"; refus: string[] }
  | { issue: "refuse"; motif: string };

/** Ce que rend un changement du délai par défaut : le réglage à jour, le refus du champ, ou le motif d'un refus. */
export type ResultatDuReglage =
  | { issue: "change"; reglage: ReglageDuDelai }
  | { issue: "refuse"; refus: string[] }
  | { issue: "impossible"; motif: string };

/** Ce que rend un changement de statut ou une correction : la fiche à jour, ou le motif d'un refus. */
export type ResultatDUneAction = { faite: true; fiche: FicheDUneCandidature } | { faite: false; motif: string };

/** Une étape telle que la fiche l'envoie, champ par champ, sans rien convertir. */
export interface SaisieDUneEtape {
  type: TypeDEtape;
  /** La date et l'heure telles que l'utilisateur les a tapées, sans décalage : `2026-11-20T10:00`. */
  survenuLe: string;
  note: string;
}

/** Un champ de la saisie d'une étape. */
export type ChampDeLEtape = keyof SaisieDUneEtape;

/** Les messages de refus de l'API, champ par champ. */
export type RefusDeLEtape = Partial<Record<ChampDeLEtape, string[]>>;

/** Ce que rend une étape notée : la fiche à jour, ou les refus de ses champs. */
export type ResultatDeLEtape = { notee: true; fiche: FicheDUneCandidature } | { notee: false; refus: RefusDeLEtape };

/**
 * Ce que rend la correction d'une étape : la fiche à jour ; les refus de ses champs ; ou le motif d'un refus que l'état
 * de l'étape explique (elle n'est plus la version en vigueur).
 */
export type ResultatDeLaCorrectionDEtape =
  | { issue: "faite"; fiche: FicheDUneCandidature }
  | { issue: "refusee"; refus: RefusDeLEtape }
  | { issue: "perimee"; motif: string };

/**
 * La sorte d'une copie de la base : celle d'un jour d'utilisation, de ce poste ou d'une autre installation qui partage le
 * dossier (#28), celle d'avant une mise à jour (#26), ou celle d'avant une restauration (#28) ou un import (#40).
 */
export type SorteDeCopie = "DuJour" | "AvantMiseAJour" | "DuJourDUneAutreInstallation" | "AvantRestauration";

/** Le dossier d'une copie : celui des copies, ou celui par défaut quand un autre est choisi. */
export type LieuDUneCopie = "DossierDesCopies" | "DossierParDefaut";

/** Une copie gardée de la base, que l'utilisateur peut restaurer (#28). */
export interface CopieGardee {
  /** Son nom de fichier, sans le dossier. */
  nom: string;
  sorte: SorteDeCopie;
  /** L'instant où elle a été écrite, en UTC. */
  faiteLe: string;
  lieu: LieuDUneCopie;
  /** Son identifiant, seize caractères hexadécimaux, que la page envoie à l'hôte pour la restaurer : jamais un chemin. */
  id: string;
}

/** Ce qui empêche les copies : un dossier qui manque, ou une copie qui ne peut pas s'y écrire. */
export type MotifDEchec = "DossierIntrouvable" | "CopieImpossible";

/** L'état des sauvegardes, tel que l'API le tient en mémoire (#26). */
export interface EtatDesSauvegardes {
  /** Le dossier des copies du jour. */
  dossier: string;
  /** Vrai si c'est le dossier par défaut, sur le disque de la base. */
  parDefaut: boolean;
  /** Le dossier par défaut, où vont aussi les copies d'avant une mise à jour. */
  dossierParDefaut: string;
  /** L'instant de la dernière copie du jour, en UTC ; rien s'il n'y en a aucune. */
  derniereCopie: string | null;
  /** Les copies gardées : celles du jour, puis celles d'avant une mise à jour. */
  copies: CopieGardee[];
  /** Vrai si le réglage du dossier n'a pas pu être relu : le dossier par défaut sert alors. */
  reglageIllisible: boolean;
  /** Les copies manquées depuis la dernière réussie, et depuis quand ; rien si tout va bien. */
  echec: { motif: MotifDEchec; depuis: string } | null;
  /** Faux tant que la vérification du lancement n'est pas faite. */
  verifie: boolean;
}

/** Ce que rend le retour au dossier par défaut : l'état à jour, ou le motif du refus (l'ancien dossier reste). */
export type ResultatDuRetourAuDossierParDefaut = { issue: "fait"; etat: EtatDesSauvegardes } | { issue: "refuse"; motif: string };

const CHAMPS: readonly ChampDeLaSaisie[] = ["entreprise", "poste", "envoyeeLe"];

const CHAMPS_DE_L_ETAPE: readonly ChampDeLEtape[] = ["type", "survenuLe", "note"];

const CHAMPS_DU_CONTACT: readonly ChampDuContact[] = ["nom", "email"];

const CHAMPS_DE_LA_RELANCE: readonly ChampDeLaRelance[] = ["delai", "destinataire"];

/** La source d'une offre : le connecteur qui l'a fait entrer, le texte collé (#36) ou le fichier CSV (#40). */
export type SourceDOffre = "TexteColle" | "FichierCsv";

/** La candidature d'une offre (#38) : de quoi la reconnaître, et l'ouvrir. */
export interface CandidatureDeLOffre {
  id: number;
  statut: StatutDeCandidature;
  /** L'instant de son envoi, en UTC. */
  envoyeeLe: string;
}

/**
 * Une offre signalée comme doublon possible d'une autre (règle 3) : de quoi la reconnaître, et l'ouvrir ; et sa
 * candidature, s'il y en a une : un doublon possible déjà postulé se montre (#38).
 */
export interface OffreSignalee {
  id: number;
  poste: string;
  entreprise: EntrepriseConnue;
  lieu: string | null;
  /** L'instant de son import, en UTC. */
  ajouteeLe: string;
  candidature: CandidatureDeLOffre | null;
}

/** Une offre telle que la liste des offres la montre (#38) : ses champs, sans son texte, et sa candidature. */
export interface OffreListee {
  id: number;
  source: SourceDOffre;
  /** Son entreprise, ou rien pour une offre anonyme. */
  entreprise: EntrepriseConnue | null;
  poste: string;
  lieu: string | null;
  /** Le jour de sa publication, sans heure ni fuseau : `2026-11-18`. */
  publieeLe: string | null;
  /** L'instant de l'import, en UTC. */
  ajouteeLe: string;
  candidature: CandidatureDeLOffre | null;
}

/**
 * Ce que rend « Postuler » (#38) : la candidature enregistrée ; les refus de la date d'envoi ; l'offre qui a déjà sa
 * candidature, que l'API désigne ; l'offre sans entreprise ; ou l'offre dont le poste ne se reprend pas. Une offre
 * inconnue lève une `ErreurDeLApi` 404.
 */
export type ResultatDeLaPostulation =
  | { issue: "enregistree"; candidature: Candidature }
  | { issue: "refusee"; refus: string[] }
  | { issue: "dejaPostulee"; candidature: number }
  | { issue: "entrepriseManquante" }
  | { issue: "posteARevoir" };

/** La fiche d'une offre (#36), telle que l'API la rend : ses doublons possibles sont relus à chaque lecture. */
export interface FicheDUneOffre {
  id: number;
  source: SourceDOffre;
  /** Son entreprise, ou rien pour une offre anonyme. */
  entreprise: EntrepriseConnue | null;
  poste: string;
  lieu: string | null;
  /** Le jour de sa publication, sans heure ni fuseau : `2026-11-18`. */
  publieeLe: string | null;
  /** L'adresse de l'annonce, telle que saisie. */
  adresse: string | null;
  /** Le texte collé, tel quel. */
  texte: string | null;
  /** L'instant de l'import, en UTC. */
  ajouteeLe: string;
  doublonsPossibles: OffreSignalee[];
  /** Sa candidature, faite par « Postuler » (#38), ou rien. */
  candidature: CandidatureDeLOffre | null;
}

/** Une offre collée telle que l'écran l'envoie, champ par champ, sans rien convertir. */
export interface SaisieDUneOffre {
  texte: string;
  entreprise: string;
  poste: string;
  lieu: string;
  /** La date telle qu'un champ `type=date` l'écrit : `2026-11-18`, ou vide. */
  publieeLe: string;
  adresse: string;
}

/** Un champ de l'offre. */
export type ChampDeLOffre = keyof SaisieDUneOffre;

/** Les messages de refus de l'API, champ par champ. */
export type RefusDeLOffre = Partial<Record<ChampDeLOffre, string[]>>;

/** La correction d'une offre : ses champs, jamais son texte (ADR-0013). */
export type CorrectionDUneOffre = Omit<SaisieDUneOffre, "texte">;

/**
 * Ce que rend l'enregistrement ou la correction d'une offre : sa fiche ; les refus de ses champs ; l'offre déjà
 * enregistrée que le même texte retrouve (règle 2) ; ou les offres de même entreprise et de même poste, doublons
 * possibles, que l'utilisateur garde ou non (règle 3).
 */
export type ResultatDeLOffre =
  | { issue: "enregistree"; fiche: FicheDUneOffre }
  | { issue: "refusee"; refus: RefusDeLOffre }
  | { issue: "dejaEnregistree"; offre: { id: number; ajouteeLe: string } }
  | { issue: "doublonPossible"; doublons: OffreSignalee[] };

/**
 * Le sort d'une ligne d'un fichier CSV (#40) : à l'aperçu, elle s'importera, ou attend l'accord (doublon possible) ; à
 * l'import, elle est importée, ou un doublon possible non gardé est écarté ; à l'un comme à l'autre, déjà enregistrée,
 * en double dans le fichier, ou invalide.
 */
export type SortDUneLigne = "aImporter" | "importee" | "dejaEnregistree" | "enDouble" | "doublonPossible" | "invalide";

/**
 * Une ligne d'offre d'un fichier, telle que l'API en rend compte. Une ligne invalide ne porte que son numéro et ses
 * motifs, jamais son contenu ; une ligne admise, son poste, son entreprise et son lieu tels que l'API les a lus.
 */
export interface LigneDuCompteRendu {
  /** Son numéro, celui d'un tableur : l'en-tête est la ligne 1. */
  ligne: number;
  sort: SortDUneLigne;
  poste: string | null;
  entreprise: string | null;
  lieu: string | null;
  /** L'identifiant de l'offre importée. */
  offre: number | null;
  /** L'offre déjà enregistrée, de même empreinte (règle 2). */
  trouvee: { id: number; ajouteeLe: string } | null;
  /** La ligne d'avant du fichier, de même empreinte (règle 2). */
  ligneDOrigine: number | null;
  /** Les offres en base de même entreprise et de même poste (règle 3). */
  doublons: OffreSignalee[] | null;
  /** Les lignes d'avant du fichier, de même entreprise et de même poste (règle 3). */
  lignesDuFichier: number[] | null;
  /** Les motifs d'une ligne invalide, par colonne, ou sous « ligne ». */
  refus: Record<string, string[]> | null;
}

/** Les comptes d'un fichier (#40). */
export interface ComptesDuFichier {
  /** Les lignes d'offres, en-tête et lignes vides non comprises. */
  lignes: number;
  lignesVides: number;
  aImporter: number;
  importees: number;
  dejaEnregistrees: number;
  enDouble: number;
  /** À l'aperçu, en attente de l'accord ; à l'import, écartés. */
  doublonsPossibles: number;
  invalides: number;
}

/** Le compte rendu d'un fichier, à l'aperçu comme à l'import : chaque ligne, dans l'ordre du fichier, puis les comptes. */
export interface CompteRenduDUnFichier {
  lignes: LigneDuCompteRendu[];
  comptes: ComptesDuFichier;
}

/**
 * Ce que rendent l'aperçu et l'import d'un fichier CSV (#40) : le compte rendu, le motif du refus du fichier entier, ou, à
 * l'import seulement, la copie de la base avant lui qui n'a pas pu se faire (ADR-0015) : rien n'est importé.
 */
export type ResultatDuFichier =
  | { issue: "lu"; compteRendu: CompteRenduDUnFichier }
  | { issue: "refuse"; motif: string }
  | { issue: "copieImpossible" };

/** La taille la plus grande d'un fichier CSV d'offres, en octets : 2 Mo (#40). */
export const TAILLE_MAXIMALE_DU_FICHIER = 2 * 1024 * 1024;

/** Le refus d'un fichier trop gros, celui de l'API (#40). */
export const FICHIER_TROP_GROS = "Le fichier dépasse 2 Mo : découpez-le en plusieurs fichiers.";

/** Une réponse de l'API en erreur : jeton refusé, origine refusée, route inconnue… */
export class ErreurDeLApi extends Error {
  readonly statut: number;

  constructor(statut: number) {
    super(`L'API locale a répondu ${statut}.`);
    this.name = "ErreurDeLApi";
    this.statut = statut;
  }
}

/** Vrai pour une candidature qui n'existe pas, ou plus : l'API a répondu 404. */
export function estIntrouvable(erreur: unknown): boolean {
  return erreur instanceof ErreurDeLApi && erreur.statut === 404;
}

/** Les appels de l'interface à l'API locale. */
export interface ClientDeLApi {
  lireEtat(): Promise<EtatDeLApplication>;
  /** Les candidatures, les plus récentes d'abord. */
  listerLesCandidatures(): Promise<Candidature[]>;
  /**
   * Les entreprises qu'une candidature ou une offre désigne encore, que les écrans de saisie et de modification
   * proposent, des candidatures comme des offres (ADR-0013).
   */
  listerLesEntreprises(): Promise<EntrepriseConnue[]>;
  /** Enregistre une candidature, ou rend les refus de ses champs. */
  enregistrerUneCandidature(saisie: SaisieDUneCandidature): Promise<ResultatDeLEnregistrement>;
  /** La fiche d'une candidature ; une `ErreurDeLApi` 404 si elle n'existe pas. */
  lireLaFiche(candidature: number): Promise<FicheDUneCandidature>;
  /** Fait passer la candidature à un nouveau statut, ou rend le motif du refus. */
  changerLeStatut(candidature: number, statut: StatutDeCandidature): Promise<ResultatDUneAction>;
  /** Corrige le dernier changement de statut, désigné par son identifiant, ou rend le motif du refus. */
  corrigerLeDernierChangement(candidature: number, changement: number): Promise<ResultatDUneAction>;
  /** Ajoute une étape à l'historique, ou rend les refus de ses champs. */
  noterUneEtape(candidature: number, saisie: SaisieDUneEtape): Promise<ResultatDeLEtape>;
  /** Corrige une étape, désignée par son identifiant : sa correction s'ajoute à l'historique (#25). */
  corrigerUneEtape(candidature: number, etape: number, saisie: SaisieDUneEtape): Promise<ResultatDeLaCorrectionDEtape>;
  /**
   * Corrige l'entreprise, le poste et la date d'envoi d'une candidature, ou rend les refus de ses champs ; une
   * `ErreurDeLApi` 404 si elle n'existe pas. Avec `corrigerLeNom`, un nom de même clé, écrit autrement, corrige le
   * nom affiché de l'entreprise ; sans lui, la modification attend cet accord.
   */
  modifierUneCandidature(candidature: number, saisie: SaisieDUneCandidature, corrigerLeNom?: boolean): Promise<ResultatDeLaModification>;
  /** Supprime une candidature et tout son historique, définitivement ; une `ErreurDeLApi` 404 si elle n'existe pas. */
  supprimerUneCandidature(candidature: number): Promise<void>;
  /** L'état des sauvegardes, lu en mémoire par l'API, sans toucher au dossier (#26). */
  lireLesSauvegardes(): Promise<EtatDesSauvegardes>;
  /** Refait aussitôt la copie du jour ; l'état rendu dit si elle a réussi. */
  sauvegarderMaintenant(): Promise<EtatDesSauvegardes>;
  /** Revient au dossier par défaut, après une copie qui doit y réussir ; sinon l'ancien dossier reste. */
  revenirAuDossierParDefaut(): Promise<ResultatDuRetourAuDossierParDefaut>;
  /** Les contacts d'une entreprise, dans l'ordre de leur ajout (#14). */
  listerLesContacts(entreprise: number): Promise<ContactConnu[]>;
  /** Ajoute un contact à une entreprise, ou rend les refus de ses champs. */
  ajouterUnContact(entreprise: number, saisie: SaisieDUnContact): Promise<ResultatDUnContact>;
  /** Corrige le nom et l'adresse d'un contact, ou rend les refus de ses champs ; une `ErreurDeLApi` 404 s'il n'existe pas. */
  modifierUnContact(contact: number, saisie: SaisieDUnContact): Promise<ResultatDUnContact>;
  /** Supprime un contact, définitivement : ses relances restent, sans destinataire. */
  supprimerUnContact(contact: number): Promise<void>;
  /** Planifie une relance de la candidature, ou rend les refus de ses champs, ou le motif d'un refus. */
  planifierUneRelance(candidature: number, saisie: SaisieDUneRelance): Promise<ResultatDeLaRelance>;
  /** Change le délai et le destinataire de la relance à faire. */
  changerLaRelance(candidature: number, relance: number, saisie: SaisieDUneRelance): Promise<ResultatDeLaRelance>;
  /** Marque faite la relance à faire, pour de bon, ou rend le motif du refus. */
  marquerLaRelanceFaite(candidature: number, relance: number): Promise<ResultatDUneAction>;
  /** Abandonne la relance à faire, pour de bon, ou rend le motif du refus. */
  abandonnerLaRelance(candidature: number, relance: number): Promise<ResultatDUneAction>;
  /** Les relances échues et celles de la semaine, que signale le tableau de bord. */
  listerLesRelances(): Promise<RelancesDuTableauDeBord>;
  /** Le délai par défaut des relances. */
  lireLeReglageDesRelances(): Promise<ReglageDuDelai>;
  /** Change le délai par défaut des relances, ou rend le refus du champ, ou le motif d'un refus. */
  changerLeDelaiParDefaut(jours: number): Promise<ResultatDuReglage>;
  /** L'état de l'IA (#43) : le fournisseur, s'il a été choisi, ce qui ne se lisait pas, et la clé, sans jamais la clé. */
  lireLEtatDeLIA(): Promise<EtatDeLIA>;
  /** Choisit le fournisseur d'IA ; un autre que celui du réglage efface la clé enregistrée (une clé, une adresse). */
  choisirLeFournisseurDIA(fournisseur: FournisseurDIA): Promise<ResultatDeLIA>;
  /**
   * Enregistre la clé du fournisseur réglé, à la place de toute autre. La clé part une fois, dans le corps ; aucune
   * réponse ne la rend.
   */
  enregistrerLaCleDIA(cle: string): Promise<ResultatDeLaCle>;
  /** Retire la clé enregistrée. */
  retirerLaCleDIA(): Promise<ResultatDeLIA>;
  /**
   * Teste la clé (#48) : une courte question fixe au fournisseur réglé, sans aucune donnée de l'utilisateur, sur son seul
   * geste. Rend le résultat et son texte, ou le motif d'un refus sans requête.
   */
  testerLaCleDIA(): Promise<ResultatDeLEssai>;
  /**
   * Demande à l'IA réglée de proposer les champs d'une offre collée (#46), sur le seul geste de l'utilisateur : son texte
   * part comme une donnée à analyser, et rien ne s'enregistre. `annulation` interrompt la demande, quand l'écran se ferme.
   */
  extraireUneOffre(texte: string, annulation?: AbortSignal): Promise<ResultatDeLExtraction>;
  /**
   * Colle une offre (#36) : sa fiche, ou les refus de ses champs, ou l'offre déjà enregistrée du même texte, ou ses
   * doublons possibles. Avec `garderLesDeux`, un doublon possible s'enregistre quand même.
   */
  collerUneOffre(saisie: SaisieDUneOffre, garderLesDeux?: boolean): Promise<ResultatDeLOffre>;
  /** La fiche d'une offre ; une `ErreurDeLApi` 404 si elle n'existe pas. */
  lireUneOffre(offre: number): Promise<FicheDUneOffre>;
  /** Corrige les champs d'une offre, jamais son texte ; une `ErreurDeLApi` 404 si elle n'existe pas. */
  corrigerUneOffre(offre: number, correction: CorrectionDUneOffre, garderLesDeux?: boolean): Promise<ResultatDeLOffre>;
  /**
   * Toutes les offres, sans leur texte, les plus récemment ajoutées d'abord, avec leur candidature (#38). La liste se
   * lit sans paramètre : l'interface la filtre elle-même, et rien de la recherche ne quitte la page.
   */
  listerLesOffres(): Promise<OffreListee[]>;
  /**
   * Postule à une offre (#38) : la candidature reprend son entreprise et son poste, à la date et l'heure d'envoi
   * saisies, sans décalage. Une `ErreurDeLApi` 404 si l'offre n'existe pas.
   */
  postuler(offre: number, envoyeeLe: string): Promise<ResultatDeLaPostulation>;
  /**
   * L'aperçu de l'import d'un fichier CSV d'offres (#40) : ce que l'import ferait de chaque ligne, sans rien écrire.
   * L'API ne reçoit que les octets du fichier, en base64 : ni son chemin, ni son nom.
   */
  examinerUnFichierCsv(contenu: ArrayBuffer): Promise<ResultatDuFichier>;
  /**
   * Importe un fichier CSV d'offres (#40), en une seule transaction : l'API refait l'examen, et ne garde un doublon
   * possible que si sa ligne est dans `garder`.
   */
  importerUnFichierCsv(contenu: ArrayBuffer, garder: readonly number[]): Promise<ResultatDuFichier>;
}

/**
 * Le client de l'API locale. Le jeton reste dans sa fermeture, en mémoire : ni stockage du navigateur, ni
 * cookie, ni adresse. Chaque appel le porte dans son en-tête dédié, vers la même origine que la page ; le
 * navigateur y ajoute lui-même l'en-tête `Origin` d'un enregistrement.
 */
export function creerClientDeLApi(jeton: string, envoyer: typeof fetch = fetch): ClientDeLApi {
  const appeler = (chemin: string, enTetes: Record<string, string> = {}, init: RequestInit = {}) =>
    envoyer(chemin, {
      ...init,
      headers: { ...enTetes, [EN_TETE_DU_JETON]: jeton },
      cache: "no-store",
      credentials: "omit",
      redirect: "error",
    });

  const lire = async (chemin: string): Promise<unknown> => {
    const reponse = await appeler(chemin);
    if (!reponse.ok) {
      throw new ErreurDeLApi(reponse.status);
    }

    return reponse.json();
  };

  // Une écriture en JSON, ou sans corps : un envoi (POST), sauf mention d'une autre méthode. `signal` l'interrompt.
  const ecrire = (chemin: string, corps?: unknown, method: "POST" | "PUT" | "DELETE" = "POST", signal?: AbortSignal) =>
    appeler(
      chemin,
      corps === undefined ? {} : { "Content-Type": "application/json" },
      corps === undefined ? { method, signal } : { method, body: JSON.stringify(corps), signal },
    );

  // La fiche à jour (200), ou le motif d'un refus que l'état de la candidature explique (409).
  const agir = async (chemin: string, corps?: unknown): Promise<ResultatDUneAction> => {
    const reponse = await ecrire(chemin, corps);
    if (reponse.status === 200) {
      return { faite: true, fiche: (await reponse.json()) as FicheDUneCandidature };
    }

    if (reponse.status === 409) {
      const motif = lireLeMotif(await reponse.json().catch(() => undefined));
      if (motif !== undefined) {
        return { faite: false, motif };
      }
    }

    throw new ErreurDeLApi(reponse.status);
  };

  return {
    lireEtat: async () => (await lire("/api/etat")) as EtatDeLApplication,
    listerLesCandidatures: async () => (await lire("/api/candidatures")) as Candidature[],
    listerLesEntreprises: async () => (await lire("/api/entreprises")) as EntrepriseConnue[],
    enregistrerUneCandidature: async (saisie) => {
      const reponse = await ecrire("/api/candidatures", saisie);
      if (reponse.status === 201) {
        return { enregistree: true, candidature: (await reponse.json()) as Candidature };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS);
        // Un refus qui ne désigne aucun champ connu ne dirait rien à l'écran : c'est une erreur.
        if (refus !== undefined && Object.keys(refus).length > 0) {
          return { enregistree: false, refus };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    lireLaFiche: async (candidature) => (await lire(adresseDeLaFiche(candidature))) as FicheDUneCandidature,
    changerLeStatut: async (candidature, statut) => agir(`${adresseDeLaFiche(candidature)}/statut`, { statut }),
    corrigerLeDernierChangement: async (candidature, changement) =>
      agir(`${adresseDeLaFiche(candidature)}/changements/${identifiant(changement)}/correction`),
    noterUneEtape: async (candidature, saisie) => {
      const reponse = await ecrire(`${adresseDeLaFiche(candidature)}/etapes`, saisie);
      if (reponse.status === 200) {
        return { notee: true, fiche: (await reponse.json()) as FicheDUneCandidature };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS_DE_L_ETAPE);
        if (refus !== undefined && Object.keys(refus).length > 0) {
          return { notee: false, refus };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    corrigerUneEtape: async (candidature, etape, saisie) => {
      const reponse = await ecrire(`${adresseDeLaFiche(candidature)}/etapes/${identifiant(etape)}/correction`, saisie);
      if (reponse.status === 200) {
        return { issue: "faite", fiche: (await reponse.json()) as FicheDUneCandidature };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS_DE_L_ETAPE);
        if (refus !== undefined && Object.keys(refus).length > 0) {
          return { issue: "refusee", refus };
        }
      }

      if (reponse.status === 409) {
        const motif = lireLeMotif(await reponse.json().catch(() => undefined));
        if (motif !== undefined) {
          return { issue: "perimee", motif };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    modifierUneCandidature: async (candidature, saisie, corrigerLeNom = false) => {
      const corps = corrigerLeNom ? { ...saisie, corrigerLeNomDeLEntreprise: true } : saisie;
      const reponse = await ecrire(adresseDeLaFiche(candidature), corps, "PUT");
      if (reponse.status === 200) {
        return { issue: "modifiee", fiche: (await reponse.json()) as FicheDUneCandidature };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS);
        if (refus !== undefined && Object.keys(refus).length > 0) {
          return { issue: "refusee", refus };
        }
      }

      if (reponse.status === 409) {
        const aCorriger = lireLeNomACorriger(await reponse.json().catch(() => undefined));
        if (aCorriger !== undefined) {
          return { issue: "nomACorriger", ...aCorriger };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    supprimerUneCandidature: async (candidature) => {
      const reponse = await ecrire(adresseDeLaFiche(candidature), undefined, "DELETE");
      if (reponse.status !== 204) {
        throw new ErreurDeLApi(reponse.status);
      }
    },
    lireLesSauvegardes: async () => (await lire("/api/sauvegardes")) as EtatDesSauvegardes,
    sauvegarderMaintenant: async () => {
      const reponse = await ecrire("/api/sauvegardes");
      if (reponse.status !== 200) {
        throw new ErreurDeLApi(reponse.status);
      }

      return (await reponse.json()) as EtatDesSauvegardes;
    },
    revenirAuDossierParDefaut: async () => {
      const reponse = await ecrire("/api/sauvegardes/dossier-par-defaut");
      if (reponse.status === 200) {
        return { issue: "fait", etat: (await reponse.json()) as EtatDesSauvegardes };
      }

      if (reponse.status === 409) {
        const motif = lireLeMotif(await reponse.json().catch(() => undefined));
        if (motif !== undefined) {
          return { issue: "refuse", motif };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    listerLesContacts: async (entreprise) => (await lire(adresseDesContacts(entreprise))) as ContactConnu[],
    ajouterUnContact: async (entreprise, saisie) => enregistrerLeContact(await ecrire(adresseDesContacts(entreprise), saisie), 201),
    modifierUnContact: async (contact, saisie) =>
      enregistrerLeContact(await ecrire(`/api/contacts/${identifiant(contact)}`, saisie, "PUT"), 200),
    supprimerUnContact: async (contact) => {
      const reponse = await ecrire(`/api/contacts/${identifiant(contact)}`, undefined, "DELETE");
      if (reponse.status !== 204) {
        throw new ErreurDeLApi(reponse.status);
      }
    },
    planifierUneRelance: async (candidature, saisie) =>
      relancer(await ecrire(`${adresseDeLaFiche(candidature)}/relances`, saisie)),
    changerLaRelance: async (candidature, relance, saisie) =>
      relancer(await ecrire(`${adresseDeLaFiche(candidature)}/relances/${identifiant(relance)}`, saisie, "PUT")),
    marquerLaRelanceFaite: async (candidature, relance) =>
      agir(`${adresseDeLaFiche(candidature)}/relances/${identifiant(relance)}/faite`),
    abandonnerLaRelance: async (candidature, relance) =>
      agir(`${adresseDeLaFiche(candidature)}/relances/${identifiant(relance)}/abandon`),
    listerLesRelances: async () => (await lire("/api/relances")) as RelancesDuTableauDeBord,
    lireLeReglageDesRelances: async () => (await lire("/api/reglages/relances")) as ReglageDuDelai,
    changerLeDelaiParDefaut: async (jours) => {
      const reponse = await ecrire("/api/reglages/relances", { delaiParDefaut: jours }, "PUT");
      if (reponse.status === 200) {
        return { issue: "change", reglage: (await reponse.json()) as ReglageDuDelai };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), ["delaiParDefaut"] as const);
        if (refus?.delaiParDefaut !== undefined) {
          return { issue: "refuse", refus: refus.delaiParDefaut };
        }
      }

      if (reponse.status === 409) {
        const motif = lireLeMotif(await reponse.json().catch(() => undefined));
        if (motif !== undefined) {
          return { issue: "impossible", motif };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    lireLEtatDeLIA: async () => (await lire("/api/reglages/ia")) as EtatDeLIA,
    choisirLeFournisseurDIA: async (fournisseur) => reglerLIA(await ecrire("/api/reglages/ia", { fournisseur }, "PUT")),
    enregistrerLaCleDIA: async (cle) => {
      const reponse = await ecrire("/api/reglages/ia/cle", { cle }, "PUT");
      if (reponse.status === 200) {
        return { issue: "enregistree", etat: (await reponse.json()) as EtatDeLIA };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), ["cle"] as const);
        if (refus?.cle !== undefined) {
          return { issue: "refusee", refus: refus.cle };
        }
      }

      if (reponse.status === 409) {
        const motif = lireLeMotif(await reponse.json().catch(() => undefined));
        if (motif !== undefined) {
          return { issue: "refuse", motif };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    retirerLaCleDIA: async () => reglerLIA(await ecrire("/api/reglages/ia/cle", undefined, "DELETE")),
    testerLaCleDIA: async () => {
      // Sans corps : la route de l'essai refuse tout corps.
      const reponse = await ecrire("/api/reglages/ia/essai");
      if (reponse.status === 200) {
        const resultat = lireLeResultatDeLEssai(await reponse.json().catch(() => undefined));
        if (resultat !== undefined) {
          return { issue: "fait", ...resultat };
        }
      }

      if (reponse.status === 409) {
        const motif = lireLeMotif(await reponse.json().catch(() => undefined));
        if (motif !== undefined) {
          return { issue: "refuse", motif };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    extraireUneOffre: async (texte, annulation) => {
      const reponse = await ecrire("/api/offres/extraction", { texte }, "POST", annulation);
      if (reponse.status === 200) {
        const resultat = lireLeResultatDeLExtraction(await reponse.json().catch(() => undefined));
        if (resultat !== undefined) {
          return resultat;
        }
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined), ["texte"] as const);
        if (refus?.texte !== undefined) {
          return { issue: "texteRefuse", refus: refus.texte };
        }
      }

      if (reponse.status === 409) {
        const motif = lireLeMotif(await reponse.json().catch(() => undefined));
        if (motif !== undefined) {
          return { issue: "refuse", motif };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
    collerUneOffre: async (saisie, garderLesDeux = false) =>
      enregistrerLOffre(await ecrire("/api/offres", garderLesDeux ? { ...saisie, garderLesDeux: true } : saisie), 201),
    lireUneOffre: async (offre) => (await lire(adresseDeLOffre(offre))) as FicheDUneOffre,
    corrigerUneOffre: async (offre, correction, garderLesDeux = false) =>
      enregistrerLOffre(
        await ecrire(adresseDeLOffre(offre), garderLesDeux ? { ...correction, garderLesDeux: true } : correction, "PUT"),
        200,
      ),
    listerLesOffres: async () => (await lire("/api/offres")) as OffreListee[],
    postuler: async (offre, envoyeeLe) => postulerALOffre(await ecrire(`${adresseDeLOffre(offre)}/candidature`, { envoyeeLe })),
    examinerUnFichierCsv: async (contenu) => lireLeCompteRendu(await ecrire("/api/offres/import/apercu", { contenu: enBase64(contenu) })),
    importerUnFichierCsv: async (contenu, garder) =>
      lireLeCompteRendu(await ecrire("/api/offres/import", { contenu: enBase64(contenu), garder: [...garder] })),
  };
}

// Le compte rendu (200), le refus du fichier (400, sur « contenu »), un corps trop gros (413), refusé avant d'être lu :
// le fichier dépasse 2 Mo, ou, à l'import, la copie de la base avant lui qui n'a pas pu se faire (409). Toute autre
// réponse lève une erreur.
async function lireLeCompteRendu(reponse: Response): Promise<ResultatDuFichier> {
  if (reponse.status === 200) {
    return { issue: "lu", compteRendu: (await reponse.json()) as CompteRenduDUnFichier };
  }

  if (reponse.status === 400) {
    const motif = lireLesRefus(await reponse.json().catch(() => undefined), ["contenu"] as const)?.contenu?.[0];
    if (motif !== undefined) {
      return { issue: "refuse", motif };
    }
  }

  if (reponse.status === 413) {
    return { issue: "refuse", motif: FICHIER_TROP_GROS };
  }

  if (reponse.status === 409) {
    return { issue: "copieImpossible" };
  }

  throw new ErreurDeLApi(reponse.status);
}

// Les octets en base64, par tranches : un seul appel à String.fromCharCode dépasserait la pile sur 2 Mo.
function enBase64(contenu: ArrayBuffer): string {
  const octets = new Uint8Array(contenu);
  let binaire = "";
  for (let debut = 0; debut < octets.length; debut += 0x8000) {
    binaire += String.fromCharCode(...octets.subarray(debut, debut + 0x8000));
  }

  return btoa(binaire);
}

/** Le refus d'un texte d'offre trop long, celui de l'API (ADR-0013). */
export const TEXTE_TROP_LONG = "Le texte de l'offre ne doit pas dépasser 50 000 caractères.";

const CHAMPS_DE_L_OFFRE: readonly ChampDeLOffre[] = ["texte", "entreprise", "poste", "lieu", "publieeLe", "adresse"];

// La candidature enregistrée (201), les refus de la date (400), ou un refus que l'état de l'offre explique (409) ; une
// offre inconnue (404) lève une erreur, comme toute autre réponse.
async function postulerALOffre(reponse: Response): Promise<ResultatDeLaPostulation> {
  if (reponse.status === 201) {
    return { issue: "enregistree", candidature: (await reponse.json()) as Candidature };
  }

  if (reponse.status === 400) {
    const refus = lireLesRefus(await reponse.json().catch(() => undefined), ["envoyeeLe"] as const);
    if (refus?.envoyeeLe !== undefined) {
      return { issue: "refusee", refus: refus.envoyeeLe };
    }
  }

  if (reponse.status === 409) {
    const probleme: unknown = await reponse.json().catch(() => undefined);
    const { motif, candidature } = typeof probleme === "object" && probleme !== null ? (probleme as { motif?: unknown; candidature?: unknown }) : {};
    if (motif === "dejaPostulee" && Number.isSafeInteger(candidature) && (candidature as number) > 0) {
      return { issue: "dejaPostulee", candidature: candidature as number };
    }

    if (motif === "entrepriseManquante" || motif === "posteARevoir") {
      return { issue: motif };
    }
  }

  throw new ErreurDeLApi(reponse.status);
}

// La candidature d'une offre, rien, ou undefined si la valeur n'en a pas la forme.
function lireUneCandidatureDeLOffre(valeur: unknown): CandidatureDeLOffre | null | undefined {
  if (valeur === null || valeur === undefined) {
    return null;
  }

  if (typeof valeur !== "object") {
    return undefined;
  }

  const { id, statut, envoyeeLe } = valeur as Record<string, unknown>;
  return Number.isSafeInteger(id) && typeof statut === "string" && typeof envoyeeLe === "string"
    ? { id: id as number, statut: statut as StatutDeCandidature, envoyeeLe }
    : undefined;
}

// La fiche (201 à l'enregistrement, 200 à la correction), les refus des champs (400), ou l'offre déjà enregistrée et les
// doublons possibles (409), que l'API lit en base.
async function enregistrerLOffre(reponse: Response, attendu: 200 | 201): Promise<ResultatDeLOffre> {
  if (reponse.status === attendu) {
    return { issue: "enregistree", fiche: (await reponse.json()) as FicheDUneOffre };
  }

  if (reponse.status === 400) {
    const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS_DE_L_OFFRE);
    if (refus !== undefined && Object.keys(refus).length > 0) {
      return { issue: "refusee", refus };
    }
  }

  // Un corps de plus de 1 Mio est refusé avant d'être lu (413) : son texte dépasse de loin la borne du champ.
  if (reponse.status === 413) {
    return { issue: "refusee", refus: { texte: [TEXTE_TROP_LONG] } };
  }

  if (reponse.status === 409) {
    const conflit = lireLeConflitDeLOffre(await reponse.json().catch(() => undefined));
    if (conflit !== undefined) {
      return conflit;
    }
  }

  throw new ErreurDeLApi(reponse.status);
}

// L'offre déjà enregistrée (règle 2) ou les doublons possibles (règle 3) ; rien si le problème ne les porte pas.
function lireLeConflitDeLOffre(probleme: unknown): ResultatDeLOffre | undefined {
  if (typeof probleme !== "object" || probleme === null) {
    return undefined;
  }

  const { motif, offre, doublons } = probleme as { motif?: unknown; offre?: unknown; doublons?: unknown };
  if (motif === "dejaEnregistree" && typeof offre === "object" && offre !== null) {
    const { id, ajouteeLe } = offre as { id?: unknown; ajouteeLe?: unknown };
    return Number.isSafeInteger(id) && typeof ajouteeLe === "string" ? { issue: "dejaEnregistree", offre: { id: id as number, ajouteeLe } } : undefined;
  }

  if (motif === "doublonPossible" && Array.isArray(doublons) && doublons.length > 0) {
    const lus = doublons.map(lireUneOffreSignalee);
    return lus.every((lue): lue is OffreSignalee => lue !== undefined) ? { issue: "doublonPossible", doublons: lus } : undefined;
  }

  return undefined;
}

function lireUneOffreSignalee(valeur: unknown): OffreSignalee | undefined {
  if (typeof valeur !== "object" || valeur === null) {
    return undefined;
  }

  const { id, poste, entreprise, lieu, ajouteeLe, candidature } = valeur as Record<string, unknown>;
  if (!Number.isSafeInteger(id) || typeof poste !== "string" || typeof ajouteeLe !== "string" || (lieu !== null && typeof lieu !== "string")) {
    return undefined;
  }

  if (typeof entreprise !== "object" || entreprise === null) {
    return undefined;
  }

  const sienne = lireUneCandidatureDeLOffre(candidature);
  const { id: idDeLEntreprise, nom } = entreprise as { id?: unknown; nom?: unknown };
  return Number.isSafeInteger(idDeLEntreprise) && typeof nom === "string" && sienne !== undefined
    ? { id: id as number, poste, entreprise: { id: idDeLEntreprise as number, nom }, lieu: lieu as string | null, ajouteeLe, candidature: sienne }
    : undefined;
}

// L'adresse de la fiche d'une offre : seul un identifiant de la base y entre, jamais un texte saisi.
function adresseDeLOffre(offre: number): string {
  return `/api/offres/${identifiant(offre)}`;
}

// Le contact enregistré (201 à l'ajout, 200 à la modification), ou les refus de ses champs (400).
async function enregistrerLeContact(reponse: Response, attendu: 200 | 201): Promise<ResultatDUnContact> {
  if (reponse.status === attendu) {
    return { issue: "enregistre", contact: (await reponse.json()) as ContactConnu };
  }

  if (reponse.status === 400) {
    const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS_DU_CONTACT);
    if (refus !== undefined && Object.keys(refus).length > 0) {
      return { issue: "refuse", refus };
    }
  }

  throw new ErreurDeLApi(reponse.status);
}

// Le résultat d'un essai parti : réussi ou non, et son texte ; rien d'autre n'est lu de la réponse.
function lireLeResultatDeLEssai(corps: unknown): { reussi: boolean; message: string } | undefined {
  if (typeof corps !== "object" || corps === null) {
    return undefined;
  }

  const { reussi, message } = corps as { reussi?: unknown; message?: unknown };
  return typeof reussi === "boolean" && typeof message === "string" && message.length > 0 ? { reussi, message } : undefined;
}

// Le résultat d'une extraction partie : les cinq champs proposés, chacun un texte ou rien ; ou l'échec et son texte.
// Rien d'autre n'est lu de la réponse, et une forme inattendue ne propose rien.
function lireLeResultatDeLExtraction(corps: unknown): ResultatDeLExtraction | undefined {
  if (typeof corps !== "object" || corps === null) {
    return undefined;
  }

  const { issue, champs, message } = corps as { issue?: unknown; champs?: unknown; message?: unknown };
  if (issue === "Echec") {
    return typeof message === "string" && message.length > 0 ? { issue: "echec", message } : undefined;
  }

  if (issue !== "Proposee" || typeof champs !== "object" || champs === null) {
    return undefined;
  }

  const lus: Partial<Record<keyof ChampsProposes, string | null>> = {};
  for (const champ of CHAMPS_PROPOSES) {
    const valeur = (champs as Record<string, unknown>)[champ];
    if (valeur !== null && typeof valeur !== "string") {
      return undefined;
    }

    lus[champ] = valeur;
  }

  return { issue: "proposee", champs: lus as ChampsProposes };
}

// Les champs d'une proposition, dans l'ordre du formulaire.
const CHAMPS_PROPOSES = ["entreprise", "poste", "lieu", "publieeLe", "adresse"] as const satisfies readonly (keyof ChampsProposes)[];

// L'état de l'IA à jour (200), ou le motif d'un refus (409) : réglages illisibles, clé qui ne s'efface pas.
async function reglerLIA(reponse: Response): Promise<ResultatDeLIA> {
  if (reponse.status === 200) {
    return { issue: "fait", etat: (await reponse.json()) as EtatDeLIA };
  }

  if (reponse.status === 409) {
    const motif = lireLeMotif(await reponse.json().catch(() => undefined));
    if (motif !== undefined) {
      return { issue: "refuse", motif };
    }
  }

  throw new ErreurDeLApi(reponse.status);
}

// La fiche à jour (200), les refus des champs (400), ou le motif d'un refus que l'état de la candidature explique (409).
async function relancer(reponse: Response): Promise<ResultatDeLaRelance> {
  if (reponse.status === 200) {
    return { issue: "faite", fiche: (await reponse.json()) as FicheDUneCandidature };
  }

  if (reponse.status === 400) {
    const refus = lireLesRefus(await reponse.json().catch(() => undefined), CHAMPS_DE_LA_RELANCE);
    if (refus !== undefined && Object.keys(refus).length > 0) {
      return { issue: "refusee", refus };
    }
  }

  if (reponse.status === 409) {
    const motif = lireLeMotif(await reponse.json().catch(() => undefined));
    if (motif !== undefined) {
      return { issue: "nonPermise", motif };
    }
  }

  throw new ErreurDeLApi(reponse.status);
}

// L'adresse des contacts d'une entreprise : seul son identifiant y entre.
function adresseDesContacts(entreprise: number): string {
  return `/api/entreprises/${identifiant(entreprise)}/contacts`;
}

// L'adresse de la fiche : seul un identifiant de la base y entre, jamais un texte saisi.
function adresseDeLaFiche(candidature: number): string {
  return `/api/candidatures/${identifiant(candidature)}`;
}

function identifiant(valeur: number): string {
  if (!Number.isSafeInteger(valeur) || valeur <= 0) {
    throw new RangeError("Un identifiant est un entier positif.");
  }

  return String(valeur);
}

// Le motif d'un refus (`detail`, sinon `title`) ; rien si le problème n'en a pas.
function lireLeMotif(probleme: unknown): string | undefined {
  if (typeof probleme !== "object" || probleme === null) {
    return undefined;
  }

  const { detail, title } = probleme as { detail?: unknown; title?: unknown };
  return typeof detail === "string" && detail.length > 0 ? detail : typeof title === "string" ? title : undefined;
}

// L'entreprise dont le nom serait corrigé, et le nombre de candidatures et d'offres concernées (#38) ; rien si le
// problème ne les porte pas.
function lireLeNomACorriger(probleme: unknown): { entreprise: EntrepriseConnue; candidatures: number; offres: number } | undefined {
  if (typeof probleme !== "object" || probleme === null) {
    return undefined;
  }

  const { entreprise, candidatures, offres } = probleme as { entreprise?: unknown; candidatures?: unknown; offres?: unknown };
  if (
    typeof entreprise !== "object" ||
    entreprise === null ||
    !Number.isSafeInteger(candidatures) ||
    (candidatures as number) < 1 ||
    !Number.isSafeInteger(offres) ||
    (offres as number) < 0
  ) {
    return undefined;
  }

  const { id, nom } = entreprise as { id?: unknown; nom?: unknown };
  return Number.isSafeInteger(id) && typeof nom === "string"
    ? { entreprise: { id: id as number, nom }, candidatures: candidatures as number, offres: offres as number }
    : undefined;
}

// Les refus d'un problème de validation (`errors`), pour les seuls champs attendus ; rien si le problème n'en a pas.
function lireLesRefus<Champ extends string>(
  probleme: unknown,
  champs: readonly Champ[],
): Partial<Record<Champ, string[]>> | undefined {
  if (typeof probleme !== "object" || probleme === null || !("errors" in probleme)) {
    return undefined;
  }

  const { errors } = probleme as { errors: unknown };
  if (typeof errors !== "object" || errors === null) {
    return undefined;
  }

  const refus: Partial<Record<Champ, string[]>> = {};
  for (const champ of champs) {
    const messages = (errors as Record<string, unknown>)[champ];
    const textes = Array.isArray(messages)
      ? messages.filter((message): message is string => typeof message === "string")
      : [];
    if (textes.length > 0) {
      refus[champ] = textes;
    }
  }

  return refus;
}
