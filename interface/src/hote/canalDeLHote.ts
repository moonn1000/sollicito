/**
 * Le canal interne de la fenêtre hôte (ADR-0003, « Canal du jeton ») : `window.chrome.webview`, que WebView2
 * donne à la page. La page y signale qu'elle est prête ; l'hôte contrôle la source du message, puis lui remet
 * le jeton du lancement. Le jeton ne passe ni par une route, ni par un fichier, ni par la ligne de commande.
 */
export interface CanalDeLHote {
  postMessage(message: unknown): void;
  addEventListener(type: "message", ecouteur: (evenement: MessageEvent) => void): void;
  removeEventListener(type: "message", ecouteur: (evenement: MessageEvent) => void): void;
}

declare global {
  interface Window {
    /** Donné par WebView2 dans la fenêtre de Sollicito ; absent dans un navigateur ou un test. */
    chrome?: { webview?: CanalDeLHote };
  }
}

/** Le canal de la fenêtre, ou `undefined` si la page ne s'affiche pas dans la fenêtre de Sollicito. */
export function canalDeLaFenetre(): CanalDeLHote | undefined {
  return window.chrome?.webview;
}

/** Délai au-delà duquel la page renonce à recevoir le jeton. */
export const DELAI_DU_JETON_EN_MS = 10_000;

// 32 octets en base64url, sans remplissage : la forme du jeton de lancement.
const FORME_DU_JETON = /^[A-Za-z0-9_-]{43}$/;

/**
 * Signale à l'hôte que la page est prête, puis rend le jeton qu'il remet. Un message d'une autre forme est
 * ignoré. Le jeton n'est rendu qu'à l'appelant, qui le garde en mémoire : aucun stockage du navigateur.
 */
export function demanderLeJeton(canal: CanalDeLHote, delaiEnMs = DELAI_DU_JETON_EN_MS): Promise<string> {
  return new Promise((resoudre, rejeter) => {
    let minuterie: ReturnType<typeof setTimeout> | undefined;
    const ecouter = (evenement: MessageEvent) => {
      const jeton = lireLeJeton(evenement.data);
      if (jeton !== undefined) {
        clearTimeout(minuterie);
        canal.removeEventListener("message", ecouter);
        resoudre(jeton);
      }
    };

    minuterie = setTimeout(() => {
      canal.removeEventListener("message", ecouter);
      rejeter(new Error("La fenêtre n'a pas remis le jeton de l'API locale."));
    }, delaiEnMs);
    canal.addEventListener("message", ecouter);
    canal.postMessage({ type: "prete" });
  });
}

/** L'issue du choix du dossier des sauvegardes, telle que l'hôte la rend (#26). */
export type IssueDuChoixDuDossier = "choisi" | "annule" | "refuse";

const ISSUES_DU_CHOIX: readonly IssueDuChoixDuDossier[] = ["choisi", "annule", "refuse"];

/**
 * Demande à l'hôte d'ouvrir le dialogue de dossier de Windows, puis rend l'issue du choix (#26, ADR-0008). Le message
 * ne porte aucun chemin, et la réponse non plus : seul le dialogue en donne un, et la page relit l'état par l'API. Pas
 * de délai : l'utilisateur prend le temps qu'il veut dans le dialogue. Un message d'une autre forme est ignoré.
 */
export function demanderLeChoixDuDossier(canal: CanalDeLHote): Promise<IssueDuChoixDuDossier> {
  return new Promise((resoudre) => {
    const ecouter = (evenement: MessageEvent) => {
      const issue = lireLIssueDuChoix(evenement.data);
      if (issue !== undefined) {
        canal.removeEventListener("message", ecouter);
        resoudre(issue);
      }
    };

    canal.addEventListener("message", ecouter);
    canal.postMessage({ type: "choisirLeDossierDesSauvegardes" });
  });
}

/**
 * L'issue de l'ouverture d'un brouillon de relance (#14), telle que l'hôte la rend : ouvert dans la messagerie, refusé
 * (relance inconnue ou close), ou impossible (aucune messagerie sur le poste). « sansReponse » vient de la page : l'hôte
 * n'a pas répondu à temps.
 */
export type IssueDuBrouillon = "ouvert" | "refuse" | "impossible" | "sansReponse";

const ISSUES_DU_BROUILLON: readonly IssueDuBrouillon[] = ["ouvert", "refuse", "impossible"];

/** Délai au-delà duquel la page cesse d'attendre l'issue d'un brouillon. */
export const DELAI_DU_BROUILLON_EN_MS = 15_000;

/**
 * Demande à l'hôte d'ouvrir le brouillon d'une relance dans la messagerie de l'utilisateur, puis rend l'issue (#14,
 * ADR-0011). Le message ne porte que l'identifiant de la relance : l'hôte lit l'adresse et le texte en base, construit
 * lui-même le lien `mailto` et le lance ; la page n'ouvre jamais rien. La réponse ne porte que l'issue. Une réponse d'une
 * autre forme, ou pour une autre relance, est ignorée.
 */
export function demanderLeBrouillon(
  canal: CanalDeLHote,
  relance: number,
  delaiEnMs = DELAI_DU_BROUILLON_EN_MS,
): Promise<IssueDuBrouillon> {
  if (!Number.isSafeInteger(relance) || relance <= 0) {
    return Promise.reject(new RangeError("Un identifiant est un entier positif."));
  }

  return new Promise((resoudre) => {
    let minuterie: ReturnType<typeof setTimeout> | undefined;
    const ecouter = (evenement: MessageEvent) => {
      const issue = lireLIssueDuBrouillon(evenement.data, relance);
      if (issue !== undefined) {
        clearTimeout(minuterie);
        canal.removeEventListener("message", ecouter);
        resoudre(issue);
      }
    };

    minuterie = setTimeout(() => {
      canal.removeEventListener("message", ecouter);
      resoudre("sansReponse");
    }, delaiEnMs);
    canal.addEventListener("message", ecouter);
    canal.postMessage({ type: "ouvrirLeBrouillon", relance });
  });
}

function lireLIssueDuBrouillon(message: unknown, relanceAttendue: number): IssueDuBrouillon | undefined {
  if (typeof message !== "object" || message === null) {
    return undefined;
  }

  const { type, relance, issue } = message as { type?: unknown; relance?: unknown; issue?: unknown };
  return type === "brouillonDeRelance" && relance === relanceAttendue && ISSUES_DU_BROUILLON.includes(issue as IssueDuBrouillon)
    ? (issue as IssueDuBrouillon)
    : undefined;
}

function lireLIssueDuChoix(message: unknown): IssueDuChoixDuDossier | undefined {
  if (typeof message !== "object" || message === null) {
    return undefined;
  }

  const { type, issue } = message as { type?: unknown; issue?: unknown };
  return type === "dossierDesSauvegardes" && ISSUES_DU_CHOIX.includes(issue as IssueDuChoixDuDossier)
    ? (issue as IssueDuChoixDuDossier)
    : undefined;
}

function lireLeJeton(message: unknown): string | undefined {
  if (typeof message !== "object" || message === null) {
    return undefined;
  }

  const { type, jeton } = message as { type?: unknown; jeton?: unknown };
  return type === "jeton" && typeof jeton === "string" && FORME_DU_JETON.test(jeton) ? jeton : undefined;
}
