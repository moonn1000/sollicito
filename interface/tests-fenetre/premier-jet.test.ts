// Le premier jet d'une relance par l'IA (#47, ADR-0019) dans la vraie fenêtre, version de test, journaux de l'API au
// niveau Trace. Le dossier de données est un dossier temporaire neuf : jamais celui de l'utilisateur. Le faux fournisseur
// de la version de test répond à la place du réseau, par le texte qu'écrit le test (scénario « texte ») ; le faux de la
// messagerie note le lien du brouillon au lieu de l'ouvrir. Les tests se suivent dans l'ordre.
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterAll, beforeAll, expect, test } from "vitest";
import { lancerLaFenetre, type FenetreLancee } from "./outils/fenetre.ts";
import { fichiersQuiContiennent, profilRelache } from "./outils/fichiers.ts";

const CONNECTEE = "Connectée à l'API locale.";
// Les marques fictives, en ASCII : la sortie de l'exe est dans la page de code OEM.
const PARTIE = "SOLLICITO-ESSAI-47";
const CLE_DE_CLAUDE = `sk-ant-api03-${PARTIE}-cle-fictive-de-la-fenetre`;
const POSTE = "Comptable MARQUE-POSTE-47";
const ENTREPRISE = "Atelier MARQUE-ENTREPRISE-47";
const CONTACT = "Jeanne MARQUE-CONTACT-47";
const ADRESSE = "marque-contact-47@exemple.invalid";
const PARAGRAPHE = "Je reviens vers vous au sujet de ma candidature, envoyée le 27 mars 2026. MARQUE-IA-47 Ce poste m'intéresse toujours.";
const RELU = "Bonjour Jeanne,\n\nMon texte relu, corrigé à la main. MARQUE-RELU-47\n\nBien cordialement,";
const MARQUES = ["MARQUE-POSTE-47", "MARQUE-ENTREPRISE-47", "MARQUE-CONTACT-47", "marque-contact-47", "MARQUE-IA-47", "MARQUE-RELU-47"];
const AU_PLUS_BAVARD = { Logging__LogLevel__Default: "Trace" };
const RELANCE = "relance du vendredi 3 avril 2026";

let dossier = "";
let fenetre: FenetreLancee;

beforeAll(async () => {
  dossier = await mkdtemp(join(tmpdir(), "sollicito-premier-jet-"));
  fenetre = await lancerLaFenetre({ dossierDeDonnees: dossier, environnement: AU_PLUS_BAVARD });
});

afterAll(async () => {
  await fenetre?.nettoyer();
  if (dossier !== "") {
    await rm(dossier, { recursive: true, force: true, maxRetries: 20, retryDelay: 500 });
  }
});

test("avant le clic, la fiche dit ce qui part chez Anthropic, et rien ne part à l'affichage", async () => {
  const { page } = fenetre;
  await page.getByText(CONNECTEE).waitFor();

  // Claude et sa clé fictive.
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page.getByLabel("Claude, d'Anthropic, avec votre clé").check();
  await page.getByRole("button", { name: "Enregistrer ce choix", exact: true }).click();
  await page.getByText("Choix enregistré.", { exact: true }).waitFor();
  await page.getByLabel("Clé d'API de Claude").fill(CLE_DE_CLAUDE);
  await page.getByRole("button", { name: "Enregistrer la clé", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();

  // Une candidature, son contact, sa relance.
  await page.getByRole("button", { name: "Tableau de bord", exact: true }).click();
  await page.getByRole("button", { name: "Nouvelle candidature" }).click();
  await page.getByLabel("Entreprise").fill(ENTREPRISE);
  await page.getByLabel("Poste").fill(POSTE);
  await page.getByLabel("Date et heure d'envoi").fill("2026-03-27T23:30");
  await page.getByRole("button", { name: "Enregistrer la candidature", exact: true }).click();
  await page.getByRole("button", { name: `${POSTE}, chez ${ENTREPRISE} : ouvrir la fiche` }).click();
  await page.getByRole("heading", { level: 2, name: "Fiche de la candidature" }).waitFor();
  await page.getByRole("button", { name: "Ajouter un contact" }).click();
  await page.getByLabel("Nom").fill(CONTACT);
  await page.getByLabel("Adresse électronique").fill(ADRESSE);
  await page.getByRole("button", { name: "Ajouter le contact" }).click();
  await page.getByText(`Contact ajouté : ${CONTACT}.`).waitFor();
  await page.getByRole("button", { name: "Planifier une relance" }).click();
  await page.getByRole("button", { name: "Planifier la relance" }).click();
  await page.getByText("Relance planifiée.").waitFor();

  await page
    .getByText(
      "Sur votre demande, le poste, l'entreprise et la date d'envoi de cette candidature partent chez Anthropic, avec les consignes de Sollicito. Rien d'autre : ni le contact, ni vos notes, ni l'historique. L'appel est décompté sur votre compte chez Anthropic.",
      { exact: true },
    )
    .waitFor();
  expect(existsSync(join(dossier, "ia-demandes.txt"))).toBe(false);
});

test("le premier jet revient entre le salut et la formule ; modifié, il s'ouvre par l'hôte avec l'objet et le destinataire de la base", async () => {
  const { page } = fenetre;
  await writeFile(join(dossier, "ia-scenario.txt"), "texte");
  await writeFile(join(dossier, "ia-texte.txt"), PARAGRAPHE);

  await page.getByRole("button", { name: "Rédiger un premier jet avec Claude", exact: true }).click();
  const champ = page.getByLabel("Premier jet, à relire et à corriger");
  await champ.waitFor();
  expect(await champ.inputValue()).toBe(`Bonjour ${CONTACT},\n\n${PARAGRAPHE}\n\nBien cordialement,`);

  // Une seule demande, à l'adresse fixée, la clé dans x-api-key, ni dans l'adresse ni dans le corps.
  const demandes = (await readFile(join(dossier, "ia-demandes.txt"), "utf8")).trim().split(/\r?\n/u);
  expect(demandes).toHaveLength(1);
  expect(demandes[0]!.startsWith("POST https://api.anthropic.com/v1/messages | ")).toBe(true);
  expect(demandes[0]).toContain(`clé : x-api-key sha256=${createHash("sha256").update(CLE_DE_CLAUDE).digest("hex")}`);
  expect(demandes[0]).toContain("clé dans l'adresse : non | clé dans le corps : non");

  await champ.fill(RELU);
  await page.getByRole("button", { name: "Ouvrir dans ma messagerie", exact: true }).click();
  await page.getByText("Le brouillon s'est ouvert dans votre messagerie : relisez-le, puis envoyez-le vous-même.", { exact: false }).waitFor();

  expect(await lignes(join(dossier, "brouillons-ouverts.txt"))).toEqual([
    `mailto:${ADRESSE}?subject=${encoderStrictement(`Candidature au poste de ${POSTE}`)}&body=${encoderStrictement(RELU.replaceAll("\n", "\r\n"))}`,
  ]);
});

test("le brouillon de #14 ne change pas : il reste le gabarit, même après un premier jet", async () => {
  const { page } = fenetre;

  await page.getByRole("button", { name: `Préparer le brouillon de la ${RELANCE}` }).click();
  await page.getByText("Le brouillon s'est ouvert dans votre messagerie : relisez-le, puis envoyez-le vous-même.", { exact: false }).nth(0).waitFor();
  for (let essai = 1; (await lignes(join(dossier, "brouillons-ouverts.txt"))).length < 2; essai++) {
    expect(essai).toBeLessThan(80);
    await attendre(250);
  }

  const corps =
    `Bonjour ${CONTACT},\r\n\r\nJe me permets de revenir vers vous au sujet de ma candidature au poste de ${POSTE}, envoyée le 27 mars 2026. ` +
    "Ce poste m'intéresse toujours, et je reste à votre disposition pour en parler.\r\n\r\nBien cordialement,";
  expect((await lignes(join(dossier, "brouillons-ouverts.txt")))[1]).toBe(
    `mailto:${ADRESSE}?subject=${encoderStrictement(`Candidature au poste de ${POSTE}`)}&body=${encoderStrictement(corps)}`,
  );
});

test("une réponse piégée s'affiche nettoyée, comme du texte : aucune balise ne naît, rien ne s'exécute", async () => {
  const { page } = fenetre;
  const titre = await page.title();
  const piege =
    `<img src=x onerror="document.title='piege'"><script>document.title='piege'</script><b>gras</b>` +
    String.fromCodePoint(0x07, 0x1b) + "[31m" +
    "a" + String.fromCodePoint(0x202e) + "b" + String.fromCodePoint(0x2066) + "c" + String.fromCodePoint(0x200b) + "d" +
    String.fromCodePoint(0xe0041) +
    String.fromCodePoint(0xfe0f) + "\r\n\r\n\r\nfin.";
  await writeFile(join(dossier, "ia-texte.txt"), piege);
  await page.getByRole("button", { name: "Effacer le premier jet", exact: true }).click();

  await page.getByRole("button", { name: "Rédiger un premier jet avec Claude", exact: true }).click();
  const champ = page.getByLabel("Premier jet, à relire et à corriger");
  await champ.waitFor();

  expect(await champ.inputValue()).toBe(
    `Bonjour ${CONTACT},\n\n<img src=x onerror="document.title='piege'"><script>document.title='piege'</script><b>gras</b>[31mabcd\n\nfin.\n\nBien cordialement,`,
  );
  expect(await page.locator("main img, main b").count()).toBe(0);
  expect(await page.title()).toBe(titre);
});

test("au-delà de 1 500 caractères, aucun texte ; chaque échec se dit, et le brouillon de Sollicito reste", async () => {
  const { page } = fenetre;
  const sollicito = " Le brouillon de Sollicito reste disponible : « Préparer le brouillon ».";
  await page.getByRole("button", { name: "Effacer le premier jet", exact: true }).click();

  await writeFile(join(dossier, "ia-texte.txt"), "a".repeat(1_501));
  await page.getByRole("button", { name: "Rédiger un premier jet avec Claude", exact: true }).click();
  await page.getByText(`La réponse de Claude n'a pas pu être lue. Réessayez plus tard.${sollicito}`, { exact: true }).waitFor();

  for (const [scenario, message] of [
    ["401", "Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA."],
    ["429", "Claude refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte chez Anthropic. Réessayez plus tard, ou vérifiez votre compte."],
    ["reseau-absent", "Claude n'a pas pu être joint : vérifiez votre connexion à Internet, puis réessayez."],
  ] as const) {
    await writeFile(join(dossier, "ia-scenario.txt"), scenario);
    await page.getByRole("button", { name: "Rédiger un premier jet avec Claude", exact: true }).click();
    await page.getByText(`${message}${sollicito}`, { exact: true }).waitFor();
  }

  expect(await page.getByLabel("Premier jet, à relire et à corriger").count()).toBe(0);
  expect(await page.getByRole("button", { name: `Préparer le brouillon de la ${RELANCE}` }).isEnabled()).toBe(true);
});

test("un message à l'hôte qui porterait le texte, ou d'une autre forme, n'ouvre rien, même avec un texte déposé ; réduit à sa forme, il l'ouvre", async () => {
  const { page } = fenetre;
  const brouillons = join(dossier, "brouillons-ouverts.txt");
  const avant = (await lignes(brouillons)).length;
  const depose = "Bonjour,\n\nUn texte déposé par le test. MARQUE-RELU-47\n\nBien cordialement,";

  // Le jeton que la page remet à l'API, lu sur une requête de la page, pour déposer le texte comme elle le fait.
  const jetonLu = new Promise<string>((resoudre) =>
    page.on("request", (requete) => {
      const jeton = requete.headers()["sollicito-jeton"];
      if (jeton !== undefined) {
        resoudre(jeton);
      }
    }),
  );
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  const jeton = await jetonLu;
  const statut = await page.evaluate(
    async ({ jeton, texte }) =>
      (
        await fetch("/api/candidatures/1/relances/1/brouillon-relu", {
          method: "PUT",
          headers: { "Sollicito-Jeton": jeton, "Content-Type": "application/json" },
          body: JSON.stringify({ texte }),
          cache: "no-store",
        })
      ).status,
    { jeton, texte: depose },
  );
  expect(statut).toBe(204);

  // Un texte déposé attend : un champ de plus, ou un identifiant d'une autre forme, n'en ouvre rien.
  await page.evaluate(() => {
    const canal = (window as unknown as { chrome: { webview: { postMessage(message: unknown): void } } }).chrome.webview;
    canal.postMessage({ type: "ouvrirLeBrouillonRelu", relance: 1, texte: "Un autre texte." });
    canal.postMessage({ type: "ouvrirLeBrouillonRelu", relance: "1" });
  });
  await attendre(1_500);
  expect(await lignes(brouillons)).toHaveLength(avant);

  // Le même message, réduit à sa forme, ouvre le texte déposé : les refus venaient de la forme, non d'un dépôt vide.
  await page.evaluate(() => {
    (window as unknown as { chrome: { webview: { postMessage(message: unknown): void } } }).chrome.webview.postMessage({ type: "ouvrirLeBrouillonRelu", relance: 1 });
  });
  for (let essai = 1; (await lignes(brouillons)).length <= avant; essai++) {
    expect(essai).toBeLessThan(80);
    await attendre(250);
  }

  expect((await lignes(brouillons)).at(-1)).toBe(
    `mailto:${ADRESSE}?subject=${encoderStrictement(`Candidature au poste de ${POSTE}`)}&body=${encoderStrictement(depose.replaceAll("\n", "\r\n"))}`,
  );
});

test("fermée, la fenêtre ne laisse ni la clé ni les données dans les journaux au niveau Trace, ni la clé dans un fichier", async () => {
  const code = await fenetre.fermer();

  expect(code).toBe(0);
  const sortie = fenetre.sortie();
  // Témoins : les journaux étaient au plus bavard, et les demandes à l'IA ont été notées.
  expect(sortie).toContain("trce: ");
  expect(sortie).toContain("Sollicito.Infrastructure.IA.EnLigne.ClaudeEnLigne[91]");
  expect(sortie).toContain("Sollicito.Infrastructure.IA.EnLigne.ClaudeEnLigne[92]");
  expect(sortie).not.toContain(PARTIE);
  expect(sortie).not.toContain("sk-ant-");
  expect(sortie).not.toContain("SOLLICITO-MESSAGE-DU-FOURNISSEUR");
  for (const marque of MARQUES) {
    expect(sortie.toLowerCase()).not.toContain(marque.toLowerCase());
  }

  // Le profil du navigateur intégré, une fois relâché : il écrit encore pendant que l'exe s'arrête.
  expect(await fichiersQuiContiennent(await profilRelache(dossier), PARTIE)).toEqual([]);
  // La base, ses copies, les réglages, le trousseau, et le fichier de la clé, qui ne la porte que chiffrée.
  expect(await fichiersQuiContiennent(dossier, PARTIE)).toEqual([]);
});

async function lignes(fichier: string): Promise<string[]> {
  return existsSync(fichier) ? (await readFile(fichier, "utf8")).split(/\r?\n/).filter(Boolean) : [];
}

// L'encodage de la RFC 3986 que suit le lien mailto de l'hôte : tout sauf les caractères non réservés ; encodeURIComponent
// laisse en plus « ! ' ( ) * ».
function encoderStrictement(texte: string): string {
  return encodeURIComponent(texte).replace(/[!'()*]/gu, (signe) => `%${signe.charCodeAt(0).toString(16).toUpperCase()}`);
}

function attendre(millisecondes: number): Promise<void> {
  return new Promise((resoudre) => setTimeout(resoudre, millisecondes));
}
