// La vraie fenêtre de Sollicito, pilotée par Playwright sur le parcours principal (spec #5, « Décisions de test »,
// point 4 ; ADR-0003, « Runtime et tests »). Elle tourne dans la version de test (configuration Test), qui ouvre le
// protocole CDP ; les réglages propres à la version publiée se vérifient à la main.
// Lancement : `pnpm run test:fenetre`, sous Windows. Les tests se suivent dans l'ordre : le dernier ferme la fenêtre.
import { existsSync } from "node:fs";
import { readdir, readFile, rename } from "node:fs/promises";
import { join } from "node:path";
import type { Request } from "playwright-core";
import { afterAll, beforeAll, expect, test } from "vitest";
import { attendre, lancerLaFenetre, type FenetreLancee } from "./outils/fenetre.ts";
import { demarrerLeTemoin, type Temoin } from "./outils/temoin.ts";

const CONNECTEE = "Connectée à l'API locale.";
const FORME_DU_JETON = /^[A-Za-z0-9_-]{43}$/;

let fenetre: FenetreLancee;
let temoin: Temoin;
let jetonRemis = "";

beforeAll(async () => {
  temoin = await demarrerLeTemoin();
  fenetre = await lancerLaFenetre();
});

afterAll(async () => {
  await fenetre?.nettoyer();
  await temoin?.arreter();
});

test("un seul lancement démarre l'API et ouvre la fenêtre sur l'interface, servie à la même origine que l'API", async () => {
  const { page, origineDeLApi } = fenetre;

  await page.getByText(CONNECTEE).waitFor();

  expect(new URL(page.url()).origin).toBe(origineDeLApi);
  expect(await page.title()).toBe("Sollicito");
  expect((await fetch(`${origineDeLApi}/api/etat`)).status).toBe(401);
});

test("le jeton parvient à l'interface par le canal interne, part dans son en-tête, et ne va nulle part ailleurs", async () => {
  const { page, origineDeLApi } = fenetre;
  const appels: Request[] = [];
  page.on("request", (requete) => {
    if (new URL(requete.url()).pathname === "/api/etat") {
      appels.push(requete);
    }
  });

  // Une page rechargée redemande le jeton, et le reçoit de nouveau.
  await page.reload();
  await page.getByText(CONNECTEE).waitFor();

  expect(appels.length).toBeGreaterThan(0);
  jetonRemis = (await appels[0]!.allHeaders())["sollicito-jeton"] ?? "";
  expect(jetonRemis).toMatch(FORME_DU_JETON);
  // C'est bien le jeton du lancement : il ouvre l'API.
  expect((await fetch(`${origineDeLApi}/api/etat`, { headers: { "Sollicito-Jeton": jetonRemis } })).status).toBe(200);

  const traces = await page.evaluate(async () => ({
    adresse: location.href,
    local: localStorage.length,
    session: sessionStorage.length,
    cookies: document.cookie,
    bases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length,
    document: document.documentElement.outerHTML,
  }));
  expect(traces).toMatchObject({ local: 0, session: 0, cookies: "", bases: 0, caches: 0 });
  expect(traces.adresse).not.toContain(jetonRemis);
  expect(traces.document).not.toContain(jetonRemis);
  expect(fenetre.sortie()).not.toContain(jetonRemis);
});

test("aucune navigation ni requête ne sort vers une autre origine", async () => {
  const { page, dossierDeDonnees } = fenetre;
  const avant = page.url();
  // Trente navigations vers le témoin : sans le filtre des requêtes, les trente lui arrivent malgré leur annulation
  // (ADR-0006). Puis le même serveur sous un autre nom, un site, et deux schémas externes.
  const cibles = [
    ...Array.from({ length: 30 }, (_, numero) => `${temoin.origine}/navigation/${numero}?donnee=fictive`),
    `http://localhost:${new URL(avant).port}/`,
    "https://exemple.invalid/navigation",
    "mailto:contact@exemple.invalid",
    "ms-settings:privacy",
  ];

  for (const cible of cibles) {
    await page.evaluate((adresse) => {
      location.href = adresse;
    }, cible);
  }
  const sousRessources = await page.evaluate(async (origine) => {
    const resultat: Record<string, string> = {};
    try {
      await fetch(`${origine}/fetch`);
      resultat.fetch = "partie";
    } catch {
      resultat.fetch = "refusée";
    }
    const charger = (element: HTMLImageElement | HTMLScriptElement) =>
      new Promise<string>((resoudre) => {
        element.onload = () => resoudre("chargée");
        element.onerror = () => resoudre("refusée");
      });
    const image = new Image();
    const imageChargee = charger(image);
    image.src = `${origine}/image`;
    resultat.image = await imageChargee;
    const script = document.createElement("script");
    const scriptCharge = charger(script);
    script.src = `${origine}/script.js`;
    document.body.append(script);
    resultat.script = await scriptCharge;
    const cadre = document.createElement("iframe");
    cadre.src = `${origine}/cadre`;
    document.body.append(cadre);
    navigator.sendBeacon(`${origine}/balise`, "donnee=fictive");
    const formulaire = document.createElement("form");
    formulaire.method = "post";
    formulaire.action = `${origine}/formulaire`;
    document.body.append(formulaire);
    formulaire.submit();
    return resultat;
  }, temoin.origine);
  await attendre(1_500);

  expect(page.url()).toBe(avant);
  expect(sousRessources).toEqual({ fetch: "refusée", image: "refusée", script: "refusée" });
  expect(temoin.requetes).toEqual([]);
  // La fenêtre a annulé chaque navigation, schémas externes compris (la version de test les note).
  expect(await lignes(join(dossierDeDonnees, "navigations-annulees.txt"))).toEqual(expect.arrayContaining(cibles));
  // Celle vers localhost n'a pas atteint l'API, qui l'aurait refusée en le notant (texte sans accent : la sortie de
  // l'exe n'est pas en UTF-8).
  expect(fenetre.sortie()).not.toContain("Host inattendu");
});

test("un lien http ou https cliqué en nouvelle fenêtre s'ouvre dans le navigateur par défaut ; aucun autre", async () => {
  const { page, navigateur, dossierDeDonnees } = fenetre;
  await rechargerLInterface();
  const avant = page.url();
  const liens: [string, string, string][] = [
    ["lien-https", "https://exemple.invalid/offre?id=1", "_blank"],
    ["lien-http", "http://exemple.invalid/", "_blank"],
    ["lien-mailto", "mailto:contact@exemple.invalid", "_blank"],
    ["lien-ftp", "ftp://exemple.invalid/fichier", "_blank"],
    ["lien-reglages", "ms-settings:privacy", "_blank"],
    // Sur place, sans nouvelle fenêtre : annulé comme toute navigation, il ne s'ouvre nulle part (ADR-0006).
    ["lien-sur-place", "https://exemple.invalid/sur-place", ""],
  ];
  await page.evaluate((aCreer) => {
    for (const [id, adresse, cible] of aCreer) {
      const lien = document.createElement("a");
      lien.id = id;
      lien.href = adresse;
      lien.target = cible;
      lien.textContent = id;
      document.body.append(lien);
    }
  }, liens);

  // Un script qui ouvre une fenêtre sans geste de l'utilisateur. Playwright en donne un à chaque évaluation ; il
  // expire au bout de cinq secondes.
  const activationAuMomentDeLOuverture = await page.evaluate(async () => {
    await new Promise((resoudre) => setTimeout(resoudre, 6_000));
    const active = navigator.userActivation.isActive;
    window.open("https://exemple.invalid/sans-geste");
    return active;
  });
  // Puis les clics de l'utilisateur ; celui sur place en dernier, car sa navigation annulée reste en suspens pour Playwright.
  for (const [id] of liens) {
    await page.click(`#${id}`, { noWaitAfter: true });
  }
  await attendre(1_500);

  expect(activationAuMomentDeLOuverture).toBe(false);
  expect(navigateur.contexts().flatMap((contexte) => contexte.pages())).toHaveLength(1);
  expect(page.url()).toBe(avant);
  expect(await lignes(join(dossierDeDonnees, "liens-ouverts.txt"))).toEqual(["https://exemple.invalid/offre?id=1", "http://exemple.invalid/"]);
  expect(await lignes(join(dossierDeDonnees, "navigations-annulees.txt"))).toContain("https://exemple.invalid/sur-place");
});

test("tout téléchargement est refusé", async () => {
  const { page, navigateur, dossierDeDonnees } = fenetre;
  await rechargerLInterface();
  const avant = page.url();
  // Branché par CDP, Playwright prend les téléchargements à son compte, et la fenêtre ne verrait pas le sien : le
  // navigateur reprend sa propre gestion, celle de la version publiée.
  // Playwright signale encore le début d'un téléchargement, avant que la fenêtre ne l'annule : seul le dossier fait foi.
  const cdp = await navigateur.newBrowserCDPSession();
  await cdp.send("Browser.setDownloadBehavior", { behavior: "default" });

  await page.evaluate(() => {
    for (const [adresse, nom] of [
      ["/index.html", "page.html"],
      [URL.createObjectURL(new Blob(["donnée fictive"])), "donnee.txt"],
    ]) {
      const lien = document.createElement("a");
      lien.href = adresse!;
      lien.download = nom!;
      document.body.append(lien);
      lien.click();
    }
  });
  await attendre(1_500);

  // La version de test crée ce dossier et en fait le dossier de téléchargement : un téléchargement admis y tomberait.
  expect(await readdir(join(dossierDeDonnees, "telechargements"))).toEqual([]);
  expect(page.url()).toBe(avant);
});

test("les permissions du navigateur sont refusées", async () => {
  const permissions = await fenetre.page.evaluate(async () => {
    const demander = (contraintes: MediaStreamConstraints) =>
      navigator.mediaDevices.getUserMedia(contraintes).then(
        () => "accordée",
        (erreur: unknown) => (erreur instanceof Error ? erreur.name : String(erreur)),
      );
    return {
      notifications: await Notification.requestPermission(),
      geolocalisation: await new Promise<number | string>((resoudre) =>
        navigator.geolocation.getCurrentPosition(
          () => resoudre("accordée"),
          (erreur) => resoudre(erreur.code),
          { timeout: 10_000 },
        ),
      ),
      camera: await demander({ video: true }),
      micro: await demander({ audio: true }),
    };
  });

  // GeolocationPositionError.PERMISSION_DENIED vaut 1. La version de test simule caméra et micro : le refus ne
  // dépend pas du matériel du poste.
  expect(permissions).toEqual({ notifications: "denied", geolocalisation: 1, camera: "NotAllowedError", micro: "NotAllowedError" });
});

test("la politique de sécurité du contenu part en en-tête et bloque un script injecté", async () => {
  const reponse = await fetch(`${fenetre.origineDeLApi}/`);
  const politique = reponse.headers.get("content-security-policy") ?? "";

  const injecte = await fenetre.page.evaluate(() => {
    const script = document.createElement("script");
    script.textContent = "document.body.dataset.injecte = 'oui'";
    document.body.append(script);
    return document.body.dataset.injecte ?? "non";
  });

  expect(politique).toContain("script-src 'self'");
  expect(politique).toContain("connect-src 'self'");
  expect(politique).toContain("img-src 'self'");
  expect(politique).toContain("frame-src 'none'");
  expect(injecte).toBe("non");
});

test("le profil du navigateur intégré vit dans le dossier de données", () => {
  expect(existsSync(join(fenetre.dossierDeDonnees, "WebView2", "EBWebView"))).toBe(true);
});

test("fermer la fenêtre arrête l'API, et le jeton n'est écrit dans aucun fichier", async () => {
  const { origineDeLApi, dossierDeDonnees } = fenetre;

  const code = await fenetre.fermer();

  expect(code).toBe(0);
  await expect(fetch(`${origineDeLApi}/`)).rejects.toThrow();
  expect(jetonRemis).toMatch(FORME_DU_JETON);
  expect(await fichiersQuiContiennent(await profilRelache(dossierDeDonnees), jetonRemis)).toEqual([]);
  expect(await fichiersQuiContiennent(join(dossierDeDonnees, "telechargements"), jetonRemis)).toEqual([]);
});

// Le navigateur intégré finit de s'arrêter après l'exe, et écrit encore dans le profil. Le dossier du profil ne se
// renomme qu'une fois tous ses fichiers fermés : c'est le signal que le navigateur l'a relâché.
async function profilRelache(dossierDeDonnees: string): Promise<string> {
  const profil = join(dossierDeDonnees, "WebView2");
  const relache = join(dossierDeDonnees, "WebView2-relache");
  for (let essai = 1; ; essai++) {
    try {
      await rename(profil, relache);
      return relache;
    } catch (erreur) {
      if (essai >= 120) {
        throw erreur;
      }
      await attendre(250);
    }
  }
}

// Les lignes d'un fichier que note la version de test ; aucune si elle n'a rien noté.
async function lignes(fichier: string): Promise<string[]> {
  return existsSync(fichier) ? (await readFile(fichier, "utf8")).split(/\r?\n/).filter(Boolean) : [];
}

// Une navigation annulée par la fenêtre reste en suspens pour Playwright, qui attendrait sa fin avant un clic : la
// page repart d'un chargement neuf.
async function rechargerLInterface(): Promise<void> {
  await fenetre.page.reload();
  await fenetre.page.getByText(CONNECTEE).waitFor();
}

// Les fichiers du dossier qui contiennent le texte, en UTF-8 ou en UTF-16.
async function fichiersQuiContiennent(dossier: string, texte: string): Promise<string[]> {
  const motifs = [Buffer.from(texte, "utf8"), Buffer.from(texte, "utf16le")];
  const trouves: string[] = [];
  for (const entree of await readdir(dossier, { recursive: true, withFileTypes: true })) {
    if (!entree.isFile()) {
      continue;
    }
    const chemin = join(entree.parentPath, entree.name);
    const contenu = await readFile(chemin);
    if (motifs.some((motif) => contenu.includes(motif))) {
      trouves.push(chemin);
    }
  }
  return trouves;
}
