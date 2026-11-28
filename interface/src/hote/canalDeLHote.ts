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

/**
 * L'issue du choix du dossier des sauvegardes, telle que l'hôte la rend (#26). « occupe » : un export ou une restauration
 * tient le dialogue de l'hôte, et aucun dialogue de dossier ne s'ouvre (#118).
 */
export type IssueDuChoixDuDossier = "choisi" | "annule" | "refuse" | "occupe";

const ISSUES_DU_CHOIX: readonly IssueDuChoixDuDossier[] = ["choisi", "annule", "refuse", "occupe"];

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
  return demanderUnBrouillon(canal, relance, delaiEnMs, "ouvrirLeBrouillon", "brouillonDeRelance");
}

/**
 * Demande à l'hôte d'ouvrir le brouillon relu d'une relance (#47, ADR-0019), puis rend l'issue : le texte que la page
 * vient de déposer auprès de l'API, avec l'objet et le destinataire que l'hôte lit en base. Le message ne porte que
 * l'identifiant de la relance, jamais le texte ; la réponse a son propre type, pour ne pas se mêler à celle du brouillon
 * de #14.
 */
export function demanderLeBrouillonRelu(
  canal: CanalDeLHote,
  relance: number,
  delaiEnMs = DELAI_DU_BROUILLON_EN_MS,
): Promise<IssueDuBrouillon> {
  return demanderUnBrouillon(canal, relance, delaiEnMs, "ouvrirLeBrouillonRelu", "brouillonRelu");
}

// Un message qui ne porte que son type et l'identifiant de la relance, puis l'issue que l'hôte rend sous son type de
// réponse, pour cette relance.
function demanderUnBrouillon(
  canal: CanalDeLHote,
  relance: number,
  delaiEnMs: number,
  typeDeLaDemande: "ouvrirLeBrouillon" | "ouvrirLeBrouillonRelu",
  typeDeLaReponse: "brouillonDeRelance" | "brouillonRelu",
): Promise<IssueDuBrouillon> {
  if (!Number.isSafeInteger(relance) || relance <= 0) {
    return Promise.reject(new RangeError("Un identifiant est un entier positif."));
  }

  return new Promise((resoudre) => {
    let minuterie: ReturnType<typeof setTimeout> | undefined;
    const ecouter = (evenement: MessageEvent) => {
      const issue = lireLIssueDuBrouillon(evenement.data, relance, typeDeLaReponse);
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
    canal.postMessage({ type: typeDeLaDemande, relance });
  });
}

/**
 * L'issue de la restauration d'une copie (#28), telle que l'hôte la rend : restaurée (sa copie d'avant se restaure, ou,
 * d'une base abîmée, n'est qu'une copie brute), annulée dans le dialogue, ou le motif du refus. Seule une issue
 * « restauree… » a changé la base.
 */
export type IssueDeLaRestauration =
  | "restauree"
  | "restaureeDepuisUneBaseAbimee"
  | "annulee"
  | "abimee"
  | "etrangere"
  | "plusRecente"
  | "introuvable"
  | "changee"
  | "baseChangee"
  | "occupee"
  | "copieDAvantImpossible"
  | "impossible";

const ISSUES_DE_LA_RESTAURATION: readonly IssueDeLaRestauration[] = [
  "restauree",
  "restaureeDepuisUneBaseAbimee",
  "annulee",
  "abimee",
  "etrangere",
  "plusRecente",
  "introuvable",
  "changee",
  "baseChangee",
  "occupee",
  "copieDAvantImpossible",
  "impossible",
];

// Seize caractères hexadécimaux en minuscules : la forme de l'identifiant d'une copie.
const FORME_D_UNE_COPIE = /^[0-9a-f]{16}$/;

/**
 * Demande à l'hôte de restaurer une copie gardée, désignée par son seul identifiant, jamais par un chemin (#28,
 * ADR-0012). L'hôte demande lui-même le oui de l'utilisateur dans son dialogue, puis signale que la restauration est en
 * cours (`quandEnCours`), puis rend son issue. Pas de délai : l'utilisateur prend le temps qu'il veut dans le dialogue.
 * Une réponse d'une autre forme, ou pour une autre copie, est ignorée.
 */
export function demanderLaRestauration(canal: CanalDeLHote, copie: string, quandEnCours?: () => void): Promise<IssueDeLaRestauration> {
  if (!FORME_D_UNE_COPIE.test(copie)) {
    return Promise.reject(new RangeError("L'identifiant d'une copie tient en seize caractères hexadécimaux."));
  }

  return new Promise((resoudre) => {
    const ecouter = (evenement: MessageEvent) => {
      const issue = lireLIssueDeLaRestauration(evenement.data, copie);
      if (issue === "enCours") {
        quandEnCours?.();
      } else if (issue !== undefined) {
        canal.removeEventListener("message", ecouter);
        resoudre(issue);
      }
    };

    canal.addEventListener("message", ecouter);
    canal.postMessage({ type: "restaurerUneSauvegarde", copie });
  });
}

/** Les formes de l'export (#49, ADR-0020) : le JSON, et l'archive de fichiers CSV. */
export type FormatDExport = "json" | "csv";

/**
 * L'issue d'un export (#49), telle que l'hôte la rend : seule « exporte » a écrit un fichier sous le nom choisi ; aucune
 * autre n'y a rien laissé, ni remplacé un fichier.
 */
export type IssueDeLExport =
  | "exporte"
  | "annule"
  | "occupe"
  | "existe"
  | "extension"
  | "dossierDeDonnees"
  | "accesRefuse"
  | "placeManquante"
  | "dossierIntrouvable"
  | "impossible";

const ISSUES_DE_L_EXPORT: readonly IssueDeLExport[] = [
  "exporte",
  "annule",
  "occupe",
  "existe",
  "extension",
  "dossierDeDonnees",
  "accesRefuse",
  "placeManquante",
  "dossierIntrouvable",
  "impossible",
];

/**
 * Demande à l'hôte d'exporter toutes les données dans ce format (#49, ADR-0020). L'hôte ouvre le dialogue « Enregistrer
 * sous » de Windows, où l'utilisateur choisit seul le fichier, signale que l'export est en cours (`quandEnCours`), puis
 * rend son issue. Le message ne porte que le format, jamais un chemin ni un nom de fichier, et la réponse non plus. Pas
 * de délai : l'utilisateur prend le temps qu'il veut dans le dialogue. Une réponse d'une autre forme, ou pour un autre
 * format, est ignorée.
 */
export function demanderLExport(canal: CanalDeLHote, format: FormatDExport, quandEnCours?: () => void): Promise<IssueDeLExport> {
  return new Promise((resoudre) => {
    const ecouter = (evenement: MessageEvent) => {
      const issue = lireLIssueDeLExport(evenement.data, format);
      if (issue === "enCours") {
        quandEnCours?.();
      } else if (issue !== undefined) {
        canal.removeEventListener("message", ecouter);
        resoudre(issue);
      }
    };

    canal.addEventListener("message", ecouter);
    canal.postMessage({ type: "exporterLesDonnees", format });
  });
}

function lireLIssueDeLExport(message: unknown, formatAttendu: FormatDExport): IssueDeLExport | "enCours" | undefined {
  if (typeof message !== "object" || message === null) {
    return undefined;
  }

  const { type, format, issue } = message as { type?: unknown; format?: unknown; issue?: unknown };
  if (type !== "export" || format !== formatAttendu) {
    return undefined;
  }

  return issue === "enCours" || ISSUES_DE_L_EXPORT.includes(issue as IssueDeLExport) ? (issue as IssueDeLExport | "enCours") : undefined;
}

function lireLIssueDeLaRestauration(message: unknown, copieAttendue: string): IssueDeLaRestauration | "enCours" | undefined {
  if (typeof message !== "object" || message === null) {
    return undefined;
  }

  const { type, copie, issue } = message as { type?: unknown; copie?: unknown; issue?: unknown };
  if (type !== "restauration" || copie !== copieAttendue) {
    return undefined;
  }

  return issue === "enCours" || ISSUES_DE_LA_RESTAURATION.includes(issue as IssueDeLaRestauration)
    ? (issue as IssueDeLaRestauration | "enCours")
    : undefined;
}

function lireLIssueDuBrouillon(message: unknown, relanceAttendue: number, typeAttendu: string): IssueDuBrouillon | undefined {
  if (typeof message !== "object" || message === null) {
    return undefined;
  }

  const { type, relance, issue } = message as { type?: unknown; relance?: unknown; issue?: unknown };
  return type === typeAttendu && relance === relanceAttendue && ISSUES_DU_BROUILLON.includes(issue as IssueDuBrouillon)
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
