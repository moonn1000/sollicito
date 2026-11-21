import { execFile, spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import { mkdtemp, rm } from "node:fs/promises";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import { chromium, type Browser, type Page } from "playwright-core";

const executer = promisify(execFile);

/** L'exe de la version de test (configuration Test), que construit `pnpm run test:fenetre`. */
export const EXE_DE_TEST = fileURLToPath(
  new URL("../../../src/Sollicito.Fenetre/bin/Test/net10.0-windows/win-x64/Sollicito.exe", import.meta.url),
);

/** La vraie fenêtre de Sollicito, lancée par le test et pilotée par le protocole CDP. */
export interface FenetreLancee {
  /** La page de l'interface, dans la fenêtre. */
  readonly page: Page;
  /** La connexion CDP au navigateur intégré. */
  readonly navigateur: Browser;
  /** L'adresse où l'API écoute, lue dans son journal : « http://127.0.0.1:port ». */
  readonly origineDeLApi: string;
  /** Le dossier de données donné à la version de test. */
  readonly dossierDeDonnees: string;
  /** Ce que l'exe a écrit sur ses sorties : les journaux de l'API. */
  sortie(): string;
  /** Ferme la fenêtre comme l'utilisateur (message WM_CLOSE), puis rend le code de sortie de l'exe. */
  fermer(): Promise<number | null>;
  /** Tue l'exe par son PID, sans rien lui laisser finir : un arrêt brutal (#28). Le dossier de données reste. */
  tuer(): Promise<void>;
  /** Arrête l'exe s'il tourne encore, puis supprime le dossier de données, s'il est celui que le lancement a créé. */
  nettoyer(): Promise<void>;
}

/** Ce qui règle un lancement de la version de test. */
export interface OptionsDuLancement {
  /** Un dossier de données à reprendre, sous le dossier temporaire ; le test le garde et le supprime lui-même. */
  dossierDeDonnees?: string;
  /** Des variables d'environnement de plus, comme le point d'arrêt de la restauration (#28). */
  environnement?: Record<string, string>;
}

const DELAI_DE_LANCEMENT_EN_MS = 60_000;
const DELAI_DE_FERMETURE_EN_MS = 20_000;

/**
 * Lance la version de test sur un dossier de données neuf, ou sur celui que donne le test, et attend que l'interface
 * soit affichée. Le dossier est toujours sous le dossier temporaire : jamais celui de l'utilisateur.
 */
export async function lancerLaFenetre(options: OptionsDuLancement = {}): Promise<FenetreLancee> {
  if (process.platform !== "win32") {
    throw new Error("La fenêtre de Sollicito ne tourne que sous Windows.");
  }
  if (!existsSync(EXE_DE_TEST)) {
    throw new Error(`Version de test introuvable (${EXE_DE_TEST}) : lancez « pnpm run test:fenetre ».`);
  }

  const dossierDeDonnees = options.dossierDeDonnees ?? (await mkdtemp(join(tmpdir(), "sollicito-fenetre-")));
  if (!resolve(dossierDeDonnees).toLowerCase().startsWith(resolve(tmpdir()).toLowerCase())) {
    throw new Error("La version de test ne se lance que sur un dossier de données temporaire.");
  }
  const cree = options.dossierDeDonnees === undefined;
  const portCdp = await portLibre();
  const processus = spawn(EXE_DE_TEST, [], {
    env: {
      ...process.env,
      ...options.environnement,
      SOLLICITO_TEST_DOSSIER_DONNEES: dossierDeDonnees,
      SOLLICITO_TEST_PORT_CDP: String(portCdp),
    },
    stdio: ["ignore", "pipe", "pipe"],
  });
  let sortie = "";
  processus.stdout?.setEncoding("utf8").on("data", (texte: string) => (sortie += texte));
  processus.stderr?.setEncoding("utf8").on("data", (texte: string) => (sortie += texte));
  const fin = new Promise<number | null>((resoudre) => processus.once("exit", (code) => resoudre(code)));

  // Par le PID de l'exe lancé ici, et ses processus enfants (le navigateur intégré) : jamais par le nom de l'image, un
  // Sollicito de l'utilisateur pouvant tourner en même temps.
  const tuer = async () => {
    if (processus.exitCode === null) {
      await executer("taskkill", ["/PID", String(processus.pid), "/T", "/F"]).catch(() => undefined);
      await Promise.race([fin, attendre(5_000)]);
    }
  };
  const nettoyer = async () => {
    await tuer();
    if (cree) {
      await rm(dossierDeDonnees, { recursive: true, force: true, maxRetries: 20, retryDelay: 500 });
    }
  };

  try {
    const limite = Date.now() + DELAI_DE_LANCEMENT_EN_MS;
    const navigateur = await joindre(`http://127.0.0.1:${portCdp}`, processus, limite);
    const origineDeLApi = await attendreQue(() => /Now listening on: (http:\/\/127\.0\.0\.1:\d+)/.exec(sortie)?.[1], processus, limite);
    const page = await attendreQue(
      () => navigateur.contexts().flatMap((contexte) => contexte.pages()).find((p) => p.url().startsWith(origineDeLApi)),
      processus,
      limite,
    );

    return {
      page,
      navigateur,
      origineDeLApi,
      dossierDeDonnees,
      sortie: () => sortie,
      fermer: async () => {
        await navigateur.close();
        await executer("taskkill", ["/PID", String(processus.pid)]);
        return Promise.race([fin, attendre(DELAI_DE_FERMETURE_EN_MS).then(() => Promise.reject(new Error("L'exe ne s'est pas fermé.")))]);
      },
      tuer: async () => {
        await navigateur.close().catch(() => undefined);
        await tuer();
      },
      nettoyer,
    };
  } catch (erreur) {
    await nettoyer();
    throw new Error(`La fenêtre ne s'est pas ouverte. Sortie de l'exe :\n${sortie}`, { cause: erreur });
  }
}

/** Attend, en millisecondes. */
export function attendre(duree: number): Promise<void> {
  return new Promise((resoudre) => setTimeout(resoudre, duree));
}

async function joindre(adresse: string, processus: ChildProcess, limite: number): Promise<Browser> {
  return attendreQue(() => chromium.connectOverCDP(adresse, { timeout: 2_000 }).catch(() => undefined), processus, limite);
}

async function attendreQue<T>(lire: () => T | undefined | Promise<T | undefined>, processus: ChildProcess, limite: number): Promise<T> {
  while (Date.now() < limite) {
    if (processus.exitCode !== null) {
      throw new Error(`L'exe s'est arrêté (code ${processus.exitCode}).`);
    }
    const valeur = await lire();
    if (valeur !== undefined) {
      return valeur;
    }
    await attendre(250);
  }
  throw new Error("Délai de lancement dépassé.");
}

function portLibre(): Promise<number> {
  return new Promise((resoudre, rejeter) => {
    const serveur = createServer();
    serveur.once("error", rejeter);
    serveur.listen(0, "127.0.0.1", () => {
      const adresse = serveur.address();
      const port = typeof adresse === "object" && adresse !== null ? adresse.port : 0;
      serveur.close(() => resoudre(port));
    });
  });
}
