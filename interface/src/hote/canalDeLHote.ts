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
