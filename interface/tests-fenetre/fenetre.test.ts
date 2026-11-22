// La vraie fenêtre de Sollicito, pilotée par Playwright sur le parcours principal (spec #5, « Décisions de test »,
// point 4 ; ADR-0003, « Runtime et tests »). Elle tourne dans la version de test (configuration Test), qui ouvre le
// protocole CDP ; les réglages propres à la version publiée se vérifient à la main.
// Lancement : `pnpm run test:fenetre`, sous Windows. Les tests se suivent dans l'ordre : le dernier ferme la fenêtre.
import { existsSync } from "node:fs";
import { mkdir, readdir, readFile, realpath, rename, stat, writeFile } from "node:fs/promises";
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
// La copie du jour dans le dossier choisi, et sa date d'écriture avant la fermeture (#26).
let copieChoisie = { chemin: "", ecriteLe: 0 };

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

test("une candidature saisie dans la fenêtre s'enregistre dans la base du dossier de données et paraît au tableau de bord", async () => {
  const { page, dossierDeDonnees } = fenetre;
  // Au premier lancement, les migrations ont créé la base dans le dossier de données.
  expect(existsSync(join(dossierDeDonnees, "sollicito.db"))).toBe(true);
  const entreprise = '<img src="x" onerror="alert(1)"> Lumière Verte SA';
  await rechargerLInterface();

  await page.getByRole("button", { name: "Nouvelle candidature" }).click();
  await page.getByLabel("Entreprise").fill(entreprise);
  await page.getByLabel("Poste").fill("Comptable");
  await page.getByLabel("Date et heure d'envoi").fill("2026-03-27T23:30");
  await page.getByRole("button", { name: "Enregistrer la candidature", exact: true }).click();

  // L'écriture est passée par le contrôle de l'origine : c'est le navigateur intégré qui a posé l'en-tête Origin.
  await page.getByText(`Candidature enregistrée : Comptable, chez ${entreprise}.`).waitFor();
  // L'annonce paraît dès le retour au tableau de bord, la liste quand sa lecture revient : on attend sa première ligne.
  await page.getByRole("row").nth(1).waitFor();
  const cellules = await page.getByRole("row").nth(1).getByRole("cell").allTextContents();
  // L'heure saisie revient telle quelle : l'API l'a lue dans le fuseau du poste, l'interface l'affiche dans le même.
  expect(cellules).toEqual([entreprise, "Comptable", "27/03/2026 23:30", "Envoyée"]);
  // Le nom s'affiche comme du texte : aucune image ne s'est créée.
  expect(await page.locator("main img").count()).toBe(0);
});

test("la fiche de la candidature fait avancer son statut et note un échange, puis le tableau de bord montre le statut à jour", async () => {
  const { page } = fenetre;
  const entreprise = '<img src="x" onerror="alert(1)"> Lumière Verte SA';
  const note = "<b>Relance</b> envoyée\npar courriel";

  await page.getByRole("button", { name: `Comptable, chez ${entreprise} : ouvrir la fiche` }).click();
  await page.getByRole("heading", { level: 2, name: "Fiche de la candidature" }).waitFor();
  // Deux écritures de plus, passées par le contrôle de l'origine : le changement de statut, puis l'étape.
  await page.getByRole("group", { name: "Passer au statut :" }).getByRole("button", { name: "Entretien" }).click();
  await page.getByText("Statut changé : Entretien.").waitFor();
  await page.getByLabel("Type").selectOption("Echange");
  await page.getByLabel("Date et heure").fill("2026-03-28T10:00");
  await page.getByLabel("Note (facultative)").fill(note);
  await page.getByRole("button", { name: "Ajouter à l'historique" }).click();
  await page.getByText("Étape ajoutée à l'historique : Échange (courriel, message).").waitFor();

  // L'historique commence à l'envoi ; l'étape, datée par l'utilisateur, précède le changement, daté du jour.
  const lignes = await page.locator("ol.historique > li").allTextContents();
  expect(lignes).toHaveLength(3);
  expect(lignes[0]).toBe("27/03/2026 23:30Candidature envoyée");
  // L'étape, en vigueur, porte son bouton « Corriger » (#25).
  expect(lignes[1]).toBe(`28/03/2026 10:00Échange (courriel, message)${note}Corriger`);
  expect(lignes[2]).toMatch(/^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}Statut passé d'Envoyée à Entretien$/u);
  // La note s'affiche comme du texte : aucune balise ne s'est créée.
  expect(await page.locator("main b, main img").count()).toBe(0);

  await page.getByRole("button", { name: "Revenir au tableau de bord" }).click();
  await page.getByRole("heading", { level: 2, name: "Tableau de bord" }).waitFor();
  await page.getByRole("row").nth(1).waitFor();
  expect(await page.getByRole("row").nth(1).getByRole("cell").allTextContents()).toEqual([entreprise, "Comptable", "27/03/2026 23:30", "Entretien"]);
});

test("la candidature et son étape se corrigent depuis la fiche, puis la candidature se supprime après une confirmation affichée dans la page", async () => {
  const { page } = fenetre;
  const entreprise = '<img src="x" onerror="alert(1)"> Lumière Verte SA';
  let boites = 0;
  page.on("dialog", (boite) => {
    boites++;
    void boite.dismiss();
  });

  await page.getByRole("button", { name: `Comptable, chez ${entreprise} : ouvrir la fiche` }).click();
  await page.getByRole("button", { name: "Modifier la candidature" }).click();
  await page.getByRole("heading", { level: 2, name: "Modifier la candidature" }).waitFor();
  // L'échange du 28/03 ouvre l'historique : l'écran le rappelle, et l'API refuse un envoi plus tardif.
  await page.getByText("Au plus tard le 28/03/2026 10:00, date du premier événement de l'historique.").waitFor();
  await page.getByLabel("Date et heure d'envoi").fill("2026-03-28T10:01");
  await page.getByRole("button", { name: "Enregistrer les modifications" }).click();
  await page.getByText("La date et l'heure d'envoi ne peuvent pas suivre le premier événement de l'historique : l'historique commence à l'envoi.").waitFor();
  await page.getByRole("button", { name: "Annuler" }).click();

  // L'échange était mal daté : sa correction (POST), passée par le contrôle de l'origine, s'ajoute à l'historique.
  await page.getByRole("button", { name: "Corriger l'étape : Échange (courriel, message), 28/03/2026 10:00" }).click();
  const correction = page.getByRole("group", { name: "Corriger l'étape : Échange (courriel, message), 28/03/2026 10:00" });
  await correction.getByLabel("Date et heure").fill("2026-03-29T10:00");
  await correction.getByRole("button", { name: "Enregistrer la correction" }).click();
  await page.getByText("Étape corrigée : Échange (courriel, message).").waitFor();
  expect(await page.locator("ol.historique .marque").allTextContents()).toEqual(["corrigée"]);

  // L'échange corrigé ne borne plus l'envoi : sa correction, du 29/03, le borne. La modification (PUT), puis la
  // suppression (DELETE), passent aussi par le contrôle de l'origine.
  await page.getByRole("button", { name: "Modifier la candidature" }).click();
  await page.getByText("Au plus tard le 29/03/2026 10:00, date du premier événement de l'historique.").waitFor();
  await page.getByLabel("Date et heure d'envoi").fill("2026-03-28T10:01");
  await page.getByLabel("Poste").fill("Comptable senior");
  await page.getByRole("button", { name: "Enregistrer les modifications" }).click();
  await page.getByText(`Candidature modifiée : Comptable senior, chez ${entreprise}.`).waitFor();
  expect(await page.locator("dl.resume dd").allTextContents()).toEqual([entreprise, "Comptable senior", "28/03/2026 10:01", "Entretien"]);
  // Le nom, prérempli puis renvoyé, s'affiche toujours comme du texte : aucune image ne s'est créée.
  expect(await page.locator("main img").count()).toBe(0);

  await page.getByRole("button", { name: "Supprimer la candidature" }).click();
  await page.getByRole("region", { name: "Supprimer cette candidature ?" }).getByRole("button", { name: "Supprimer définitivement" }).click();
  await page.getByText(`Candidature supprimée : Comptable senior, chez ${entreprise}.`).waitFor();
  await page.getByText("Aucune candidature enregistrée pour l'instant.").waitFor();
  // La confirmation vit dans la page : le navigateur intégré n'a ouvert aucune boîte.
  expect(boites).toBe(0);
  expect(await page.locator("main img").count()).toBe(0);
});

test("la base se copie au lancement, puis dans le dossier choisi par le dialogue de l'hôte, sans qu'aucun chemin ne vienne de la page", async () => {
  const { page, dossierDeDonnees } = fenetre;
  // Au lancement, la copie du jour s'est faite dans le dossier par défaut, celui du dossier de données.
  const parDefaut = join(dossierDeDonnees, "Sauvegardes");
  const copiesDuJour = async (dossier: string) =>
    existsSync(dossier) ? (await readdir(dossier)).filter((nom) => /^sollicito-[0-9a-f]{8}-\d{4}-\d{2}-\d{2}\.db$/u.test(nom)) : [];
  for (let essai = 1; (await copiesDuJour(parDefaut)).length === 0; essai++) {
    expect(essai).toBeLessThan(80);
    await attendre(250);
  }
  const [copie] = await copiesDuJour(parDefaut);
  // Un même dossier s'écrit de deux façons sous Windows : le runner de la CI donne son dossier temporaire en nom court
  // (RUNNER~1), et l'application garde le dossier choisi en nom long. On compare donc des chemins rendus canoniques.
  const canonique = async (chemin: string | null) => realpath(chemin ?? "");

  // Le dialogue de la version de test lit le dossier à choisir dans un fichier du dossier de données.
  const choisi = join(dossierDeDonnees, "cle-usb-fictive");
  await mkdir(choisi);
  await writeFile(join(dossierDeDonnees, "dossier-a-choisir.txt"), choisi, "utf8");
  await page.getByRole("button", { name: "Réglages" }).click();
  const section = page.getByRole("region", { name: "Sauvegardes" });
  await section.getByText("Les copies ne sont pas chiffrées : toute personne qui accède à leur dossier peut les lire.").waitFor();
  expect(await canonique(await section.locator("dl.resume dd").first().textContent())).toBe(await canonique(parDefaut));
  await section.getByRole("button", { name: "Choisir un autre dossier…" }).click();

  await section.getByText("Dossier des sauvegardes changé : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.").waitFor();
  expect(await canonique(await section.locator("dl.resume dd").first().textContent())).toBe(await canonique(choisi));
  expect(await copiesDuJour(choisi)).toEqual([copie]);
  expect(await copiesDuJour(parDefaut)).toEqual([copie]);
  // Le réglage vit dans le dossier de données, hors de la base.
  const reglages = JSON.parse(await readFile(join(dossierDeDonnees, "reglages.json"), "utf8")) as Record<string, string>;
  expect(Object.keys(reglages)).toEqual(["dossierDesSauvegardes"]);
  expect(await canonique(reglages.dossierDesSauvegardes ?? null)).toBe(await canonique(choisi));
  copieChoisie = { chemin: join(choisi, copie!), ecriteLe: (await stat(join(choisi, copie!))).mtimeMs };
});

test("une relance se planifie depuis la fiche, et son brouillon passe par l'hôte, qui bâtit le lien mailto à partir de la base (#14)", async () => {
  const { page, dossierDeDonnees } = fenetre;
  const brouillons = join(dossierDeDonnees, "brouillons-ouverts.txt");
  const poste = "Brasseuse & <b>cheffe</b>";
  await page.getByRole("button", { name: "Nouvelle candidature" }).click();
  await page.getByLabel("Entreprise").fill("Brasserie Lune Rousse");
  await page.getByLabel("Poste").fill(poste);
  await page.getByLabel("Date et heure d'envoi").fill("2026-03-27T23:30");
  await page.getByRole("button", { name: "Enregistrer la candidature", exact: true }).click();
  await page.getByRole("button", { name: `${poste}, chez Brasserie Lune Rousse : ouvrir la fiche` }).click();
  await page.getByRole("heading", { level: 2, name: "Fiche de la candidature" }).waitFor();

  // Un contact de l'entreprise, puis la relance, adressée d'office à ce seul contact.
  await page.getByRole("button", { name: "Ajouter un contact" }).click();
  await page.getByLabel("Nom").fill("Jeanne Martin");
  await page.getByLabel("Adresse électronique").fill("jeanne.martin@exemple.invalid");
  await page.getByRole("button", { name: "Ajouter le contact" }).click();
  await page.getByText("Contact ajouté : Jeanne Martin.").waitFor();
  await page.getByRole("button", { name: "Planifier une relance" }).click();
  await page.getByText("Échéance : vendredi 3 avril 2026.").waitFor();
  await page.getByRole("button", { name: "Planifier la relance" }).click();
  await page.getByText("Relance planifiée.").waitFor();

  // La page ne donne que l'identifiant de la relance ; la version de test note le lien que l'hôte aurait lancé.
  await page.getByRole("button", { name: "Préparer le brouillon de la relance du vendredi 3 avril 2026" }).click();
  await page.getByText("Le brouillon s'est ouvert dans votre messagerie : relisez-le, puis envoyez-le vous-même.", { exact: false }).waitFor();
  const corps =
    "Bonjour Jeanne Martin,\r\n\r\nJe me permets de revenir vers vous au sujet de ma candidature au poste de " +
    `${poste}, envoyée le 27 mars 2026. Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.\r\n\r\n` +
    "Bien cordialement,";
  expect(await lignes(brouillons)).toEqual([
    `mailto:jeanne.martin@exemple.invalid?subject=${encoderStrictement(`Candidature au poste de ${poste}`)}&body=${encoderStrictement(corps)}`,
  ]);

  // Un message d'une autre forme, ou pour une relance inconnue, n'ouvre rien.
  const envoyerALHote = (messages: unknown[]) =>
    page.evaluate((aEnvoyer) => {
      const canal = (window as unknown as { chrome: { webview: { postMessage(message: unknown): void } } }).chrome.webview;
      for (const message of aEnvoyer) {
        canal.postMessage(message);
      }
    }, messages);
  await envoyerALHote([{ type: "ouvrirLeBrouillon", relance: "1" }, { type: "ouvrirLeBrouillon" }, { type: "ouvrirLeBrouillon", relance: 999 }]);
  await attendre(1_500);
  expect(await lignes(brouillons)).toHaveLength(1);

  // Un champ de plus que le type et la relance, et rien ne s'ouvre, même pour une relance à faire : celle de ce test est
  // la première de la base du dossier de données, neuf.
  await envoyerALHote([{ type: "ouvrirLeBrouillon", relance: 1, destinataire: "pirate@exemple.invalid", corps: "autre" }]);
  await attendre(1_500);
  expect(await lignes(brouillons)).toHaveLength(1);

  // Réduit à sa forme, le même message rouvre le même brouillon : l'hôte le bâtit à partir de la base.
  await envoyerALHote([{ type: "ouvrirLeBrouillon", relance: 1 }]);
  for (let essai = 1; (await lignes(brouillons)).length < 2; essai++) {
    expect(essai).toBeLessThan(80);
    await attendre(250);
  }
  const ouverts = await lignes(brouillons);
  expect(ouverts).toHaveLength(2);
  expect(ouverts[1]).toBe(ouverts[0]);

  await page.getByRole("button", { name: "Marquer faite" }).click();
  await page.getByText("Relance marquée faite : elle entre dans l'historique.").waitFor();
  expect(await page.locator("ol.historique > li").last().textContent()).toMatch(/Relance faite, à Jeanne Martin : échéance du vendredi 3 avril 2026$/u);
  // Le poste s'affiche comme du texte : aucune balise ne s'est créée.
  expect(await page.locator("main b, main img").count()).toBe(0);
});

test("une offre collée et piégée s'enregistre, sa fiche l'affiche échappée, et le clic sur son annonce passe par l'hôte (#36)", async () => {
  const { page, dossierDeDonnees } = fenetre;
  const liens = join(dossierDeDonnees, "liens-ouverts.txt");
  const annonce = "https://emploi.exemple.invalid/offres/36?source=fictive";
  const poste = "<b>Brasseuse</b> & cheffe";
  const texte =
    '<h1>Offre</h1>\n<script>document.title = "piégé"</script>\n<img src="x" onerror="alert(1)">\n' +
    "« Développeuse » à Liège, été 2026 : œuvres & co.\n\n\t" +
    "Mission fictive. ".repeat(2_000);
  const ouvertsAvant = (await lignes(liens)).length;
  await rechargerLInterface();

  // « Coller une offre » s'ouvre depuis la liste des offres (#38).
  await page.getByRole("button", { name: "Offres", exact: true }).click();
  await page.getByRole("button", { name: "Coller une offre", exact: true }).click();
  await page.getByLabel("Texte de l'offre").fill(texte);
  await page.getByLabel("Entreprise").fill("Brasserie Lune Rousse");
  await page.getByLabel("Poste").fill(poste);
  await page.getByLabel("Adresse de l'annonce").fill(annonce);
  await page.getByRole("button", { name: "Enregistrer l'offre" }).click();

  // L'écriture est passée par le contrôle de l'origine ; la fiche de l'offre s'ouvre, avec l'annonce.
  await page.getByText(`Offre enregistrée : ${poste}, chez Brasserie Lune Rousse.`).waitFor();
  await page.getByRole("heading", { level: 2, name: poste }).waitFor();
  // Le texte s'affiche tel que collé, à ses blancs de bord près, comme du texte : ni titre, ni script, ni image, ni gras.
  expect(await page.locator(".texte-de-l-offre").textContent()).toBe(texte.trim());
  expect(await page.locator("main h1, main script, main img, main b").count()).toBe(0);
  expect(await page.title()).toBe("Sollicito");

  // Le lien de l'annonce s'ouvre en nouvelle fenêtre, sur le clic : la version de test note l'adresse que l'hôte aurait lancée.
  await page.getByRole("link", { name: annonce }).click({ noWaitAfter: true });
  for (let essai = 1; (await lignes(liens)).length <= ouvertsAvant; essai++) {
    expect(essai).toBeLessThan(80);
    await attendre(250);
  }
  expect((await lignes(liens)).slice(ouvertsAvant)).toEqual([annonce]);

  // Le même texte, recollé avec un autre poste, est reconnu à son empreinte : rien ne s'ajoute. L'offre enregistrée
  // s'ouvre sur place, et le collage reste dans la saisie (suites de #36).
  await page.getByRole("button", { name: "Offres", exact: true }).click();
  await page.getByRole("button", { name: "Coller une offre", exact: true }).click();
  await page.getByLabel("Texte de l'offre").fill(texte);
  await page.getByLabel("Poste").fill("Autre poste fictif");
  await page.getByRole("button", { name: "Enregistrer l'offre" }).click();
  await page.getByText("Cette offre est déjà enregistrée : vous l'avez collée le", { exact: false }).waitFor();
  await page.getByRole("button", { name: "Voir l'offre enregistrée" }).click();
  await page.getByRole("heading", { level: 4, name: poste }).waitFor();
  expect(await page.getByLabel("Texte de l'offre").inputValue()).toBe(texte);
  expect(await page.getByLabel("Poste").inputValue()).toBe("Autre poste fictif");
  expect(await page.locator("main h1, main script, main img, main b").count()).toBe(0);
});

test("la liste des offres affiche une offre piégée échappée, et « Postuler » depuis sa fiche fait une candidature, une seule (#38)", async () => {
  const { page } = fenetre;
  const poste = "<b>Brasseuse</b> & cheffe";
  await rechargerLInterface();

  await page.getByRole("button", { name: "Offres", exact: true }).click();
  // La liste paraît quand sa lecture revient : on attend sa première ligne.
  await page.getByRole("row").nth(1).waitFor();
  const cellules = await page.getByRole("row").nth(1).getByRole("cell").allTextContents();
  expect(cellules[0]).toBe(poste);
  expect(cellules[1]).toBe("Brasserie Lune Rousse");
  expect(cellules[4]).toBe("Aucune");
  expect(await page.locator("main table b, main table img, main table script").count()).toBe(0);

  // La recherche se fait dans la page : un texte piégé ne crée rien, et la liste vide ne le reprend pas.
  await page.getByLabel("Rechercher").fill('<img src="x" onerror="alert(1)">');
  await page.getByText("Aucune offre ne correspond à ces filtres.").waitFor();
  expect(await page.locator("main img").count()).toBe(0);
  await page.getByLabel("Rechercher").fill("brasserie LUNE");
  await page.getByRole("button", { name: `${poste}, chez Brasserie Lune Rousse : ouvrir la fiche` }).click();

  // « Postuler » depuis la fiche : l'entreprise et le poste viennent de l'offre, la date d'envoi est préremplie.
  await page.getByRole("button", { name: "Postuler", exact: true }).click();
  await page.getByRole("heading", { level: 2, name: "Postuler à cette offre" }).waitFor();
  await page.getByRole("button", { name: "Enregistrer la candidature", exact: true }).click();
  await page.getByText(`Candidature enregistrée depuis l'offre : ${poste}, chez Brasserie Lune Rousse.`).waitFor();
  await page.getByRole("button", { name: "Voir l'offre", exact: true }).click();
  await page.getByText("Candidature envoyée le", { exact: false }).waitFor();
  expect(await page.getByRole("button", { name: "Postuler", exact: true }).count()).toBe(0);

  // Au tableau de bord, la candidature paraît, son poste en texte.
  await page.getByRole("button", { name: "Tableau de bord", exact: true }).click();
  await page.getByRole("button", { name: `${poste}, chez Brasserie Lune Rousse : ouvrir la fiche` }).waitFor();
  expect(await page.locator("main table b").count()).toBe(0);

  // Deux « Postuler » simultanés sur une autre offre, depuis la page : une seule candidature, l'autre refusée (409).
  const statuts = await page.evaluate(async (jeton) => {
    const enTetes = { "Sollicito-Jeton": jeton, "Content-Type": "application/json" };
    const collee = await fetch("/api/offres", {
      method: "POST",
      headers: enTetes,
      body: JSON.stringify({ texte: "Offre fictive à qui postuler deux fois.", poste: "Comptable", entreprise: "Lumière Verte SA" }),
    });
    const { id } = (await collee.json()) as { id: number };
    const envoi = () =>
      fetch(`/api/offres/${id}/candidature`, { method: "POST", headers: enTetes, body: JSON.stringify({ envoyeeLe: "2026-11-01T09:00" }) }).then(
        (reponse) => reponse.status,
      );
    return Promise.all([envoi(), envoi()]);
  }, jetonRemis);
  expect([...statuts].sort()).toEqual([201, 409]);
  await page.getByRole("button", { name: "Offres", exact: true }).click();
  await page.getByRole("button", { name: "Effacer les filtres", exact: true }).click();
  await page.getByRole("button", { name: "Comptable, chez Lumière Verte SA : ouvrir la fiche" }).waitFor();
  expect(await page.getByRole("button", { name: /^Candidature Envoyée pour / }).count()).toBe(2);
});

test("fermer la fenêtre arrête l'API, et le jeton n'est écrit dans aucun fichier", async () => {
  const { origineDeLApi, dossierDeDonnees } = fenetre;

  const code = await fenetre.fermer();

  expect(code).toBe(0);
  await expect(fetch(`${origineDeLApi}/`)).rejects.toThrow();
  expect(jetonRemis).toMatch(FORME_DU_JETON);
  expect(await fichiersQuiContiennent(await profilRelache(dossierDeDonnees), jetonRemis)).toEqual([]);
  expect(await fichiersQuiContiennent(join(dossierDeDonnees, "telechargements"), jetonRemis)).toEqual([]);
  // Ni la base, ni rien d'autre dans le dossier de données, copies de la base comprises.
  expect(await fichiersQuiContiennent(dossierDeDonnees, jetonRemis)).toEqual([]);
  // À la fermeture, la copie du jour s'est refaite dans le dossier choisi (#26).
  expect((await stat(copieChoisie.chemin)).mtimeMs).toBeGreaterThan(copieChoisie.ecriteLe);
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

// L'encodage de la RFC 3986 que suit le lien mailto de l'hôte : tout sauf les caractères non réservés ; encodeURIComponent
// laisse en plus « ! ' ( ) * ».
function encoderStrictement(texte: string): string {
  return encodeURIComponent(texte).replace(/[!'()*]/gu, (signe) => `%${signe.charCodeAt(0).toString(16).toUpperCase()}`);
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
