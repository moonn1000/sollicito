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

const CHAMPS: readonly ChampDeLaSaisie[] = ["entreprise", "poste", "envoyeeLe"];

/** Une réponse de l'API en erreur : jeton refusé, origine refusée, route inconnue… */
export class ErreurDeLApi extends Error {
  readonly statut: number;

  constructor(statut: number) {
    super(`L'API locale a répondu ${statut}.`);
    this.name = "ErreurDeLApi";
    this.statut = statut;
  }
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

  return {
    lireEtat: async () => (await lire("/api/etat")) as EtatDeLApplication,
    listerLesCandidatures: async () => (await lire("/api/candidatures")) as Candidature[],
    listerLesEntreprises: async () => (await lire("/api/entreprises")) as EntrepriseConnue[],
    enregistrerUneCandidature: async (saisie) => {
      const reponse = await appeler(
        "/api/candidatures",
        { "Content-Type": "application/json" },
        { method: "POST", body: JSON.stringify(saisie) },
      );
      if (reponse.status === 201) {
        return { enregistree: true, candidature: (await reponse.json()) as Candidature };
      }

      if (reponse.status === 400) {
        const refus = lireLesRefus(await reponse.json().catch(() => undefined));
        if (refus !== undefined) {
          return { enregistree: false, refus };
        }
      }

      throw new ErreurDeLApi(reponse.status);
    },
  };
}

// Les refus d'un problème de validation (`errors`), pour les seuls champs de la saisie ; rien si le problème n'en a pas.
function lireLesRefus(probleme: unknown): RefusDeLaSaisie | undefined {
  if (typeof probleme !== "object" || probleme === null || !("errors" in probleme)) {
    return undefined;
  }

  const { errors } = probleme as { errors: unknown };
  if (typeof errors !== "object" || errors === null) {
    return undefined;
  }

  const refus: RefusDeLaSaisie = {};
  for (const champ of CHAMPS) {
    const messages = (errors as Record<string, unknown>)[champ];
    if (Array.isArray(messages)) {
      refus[champ] = messages.filter((message): message is string => typeof message === "string");
    }
  }

  return refus;
}
