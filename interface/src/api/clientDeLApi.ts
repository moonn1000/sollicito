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
 * nom saisi corrigerait le nom affiché, et le nombre de candidatures concernées, celle qu'on modifie comprise : la
 * modification attend alors l'accord de l'utilisateur (ADR-0007).
 */
export type ResultatDeLaModification =
  | { issue: "modifiee"; fiche: FicheDUneCandidature }
  | { issue: "refusee"; refus: RefusDeLaSaisie }
  | { issue: "nomACorriger"; entreprise: EntrepriseConnue; candidatures: number };

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

/** Un événement de l'historique : sa sorte dit lequel. */
export type EvenementDeLHistorique = ChangementDeStatutLu | EtapeLue;

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
}

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

const CHAMPS: readonly ChampDeLaSaisie[] = ["entreprise", "poste", "envoyeeLe"];

const CHAMPS_DE_L_ETAPE: readonly ChampDeLEtape[] = ["type", "survenuLe", "note"];

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
  /** Les entreprises déjà enregistrées, que l'écran de saisie propose. */
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

  // Une écriture en JSON, ou sans corps : un envoi (POST), sauf mention d'une autre méthode.
  const ecrire = (chemin: string, corps?: unknown, method: "POST" | "PUT" | "DELETE" = "POST") =>
    appeler(
      chemin,
      corps === undefined ? {} : { "Content-Type": "application/json" },
      corps === undefined ? { method } : { method, body: JSON.stringify(corps) },
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
  };
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

// L'entreprise dont le nom serait corrigé, et le nombre de candidatures concernées ; rien si le problème ne les porte pas.
function lireLeNomACorriger(probleme: unknown): { entreprise: EntrepriseConnue; candidatures: number } | undefined {
  if (typeof probleme !== "object" || probleme === null) {
    return undefined;
  }

  const { entreprise, candidatures } = probleme as { entreprise?: unknown; candidatures?: unknown };
  if (typeof entreprise !== "object" || entreprise === null || !Number.isSafeInteger(candidatures) || (candidatures as number) < 1) {
    return undefined;
  }

  const { id, nom } = entreprise as { id?: unknown; nom?: unknown };
  return Number.isSafeInteger(id) && typeof nom === "string" ? { entreprise: { id: id as number, nom }, candidatures: candidatures as number } : undefined;
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
