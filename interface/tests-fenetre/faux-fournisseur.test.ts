// Le scénario « texte » du faux fournisseur de la version de test (#46, #47) : il rend comme texte de l'IA le contenu de
// ia-texte.txt, tel quel, écrit par un écrivain JSON, donc échappé, dans une réponse complète de Claude ou d'OpenAI. Le
// dossier de données est un dossier temporaire neuf : jamais celui de l'utilisateur. L'essai de la clé (#48) sert de
// témoin : il réussit dès qu'un texte lisible revient. Les tests se suivent dans l'ordre.
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterAll, beforeAll, expect, test } from "vitest";
import { lancerLaFenetre, type FenetreLancee } from "./outils/fenetre.ts";

const CONNECTEE = "Connectée à l'API locale.";
// Un texte court, que l'essai admet (20 caractères au plus), qui porte un guillemet, un antislash, une tabulation et un
// caractère accentué : écrit sans échappement, il casserait le JSON de la réponse.
const TEXTE_PIEGE = `O"K${String.fromCharCode(92)}${String.fromCharCode(9)}é`;

let dossier = "";
let fenetre: FenetreLancee;

beforeAll(async () => {
  dossier = await mkdtemp(join(tmpdir(), "sollicito-faux-"));
  fenetre = await lancerLaFenetre({ dossierDeDonnees: dossier });
});

afterAll(async () => {
  await fenetre?.nettoyer();
  if (dossier !== "") {
    await rm(dossier, { recursive: true, force: true, maxRetries: 20, retryDelay: 500 });
  }
});

async function choisir(libelle: string, champ: string, cle: string) {
  const { page } = fenetre;
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page.getByLabel(libelle).check();
  await page.getByRole("button", { name: "Enregistrer ce choix", exact: true }).click();
  const changer = page.getByRole("button", { name: "Changer et effacer la clé", exact: true });
  if ((await changer.count()) > 0) {
    await changer.click();
  }
  await page.getByLabel(champ).fill(cle);
  await page.getByRole("button", { name: "Enregistrer la clé", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();
}

async function tester(attendu: string) {
  const { page } = fenetre;
  await page.getByRole("button", { name: "Tester la clé", exact: true }).click();
  await page.getByText(attendu, { exact: true }).waitFor();
}

test("chez Claude, le scénario « texte » rend le contenu de ia-texte.txt, échappé, guillemet, antislash et contrôle compris", async () => {
  await fenetre.page.getByText(CONNECTEE).waitFor();
  await choisir("Claude, d'Anthropic, avec votre clé", "Clé d'API de Claude", "sk-ant-api03-SOLLICITO-FAUX-cle-fictive-de-la-fenetre");
  await writeFile(join(dossier, "ia-scenario.txt"), "texte");
  await writeFile(join(dossier, "ia-texte.txt"), TEXTE_PIEGE);

  await tester("Claude a répondu : la clé fonctionne.");
});

test("chez OpenAI, de même", async () => {
  await choisir("OpenAI, avec votre clé", "Clé d'API d'OpenAI", "sk-proj-SOLLICITO-FAUX-cle-fictive-de-la-fenetre");

  await tester("OpenAI a répondu : la clé fonctionne.");
});

test("sans ia-texte.txt, le scénario « texte » répond comme un réseau absent", async () => {
  await rm(join(dossier, "ia-texte.txt"));

  await tester("OpenAI n'a pas pu être joint : vérifiez votre connexion à Internet, puis réessayez.");

  // Trois demandes notées, chacune vers l'adresse de son fournisseur, scénario « texte ».
  const lignes = (await readFile(join(dossier, "ia-demandes.txt"), "utf8")).trim().split(/\r?\n/u);
  expect(lignes).toHaveLength(3);
  expect(lignes[0]!.startsWith("POST https://api.anthropic.com/v1/messages | ")).toBe(true);
  expect(lignes[1]!.startsWith("POST https://api.openai.com/v1/responses | ")).toBe(true);
  expect(lignes.every((ligne) => ligne.endsWith("scénario : texte"))).toBe(true);
  expect(await fenetre.fermer()).toBe(0);
});
