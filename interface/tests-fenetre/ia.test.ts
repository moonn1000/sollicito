// La clé d'IA dans la vraie fenêtre (#43, ADR-0016), dans la version de test, journaux de l'API au niveau Trace. Le
// dossier de données est un dossier temporaire neuf : jamais celui de l'utilisateur ; le trousseau de Data Protection
// y naît, et la clé, fictive et reconnaissable, n'est en clair nulle part. L'essai de la clé (#48, ADR-0017) passe par le
// faux fournisseur de la version de test, qui ne touche jamais le réseau. Les tests se suivent dans l'ordre.
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterAll, beforeAll, expect, test } from "vitest";
import { lancerLaFenetre, type FenetreLancee } from "./outils/fenetre.ts";
import { fichiersQuiContiennent, profilRelache } from "./outils/fichiers.ts";

const CONNECTEE = "Connectée à l'API locale.";
// La partie reconnaissable des clés fictives, en ASCII : la sortie de l'exe est dans la page de code OEM.
const PARTIE = "SOLLICITO-ESSAI-43";
const CLE_DE_CLAUDE = `sk-ant-api03-${PARTIE}-cle-fictive-de-la-fenetre`;
const CLE_D_OPENAI = `sk-proj-${PARTIE}-cle-fictive-de-la-fenetre`;
const AU_PLUS_BAVARD = { Logging__LogLevel__Default: "Trace" };

let dossier = "";
let fenetre: FenetreLancee;
// Ce que l'API a rendu à la page, toutes réponses confondues : lues jusqu'au bout avant chaque vérification.
const lectures: Promise<string>[] = [];
const reponses = async () => (await Promise.all(lectures)).join(" | ");

beforeAll(async () => {
  dossier = await mkdtemp(join(tmpdir(), "sollicito-ia-"));
  fenetre = await lancerLaFenetre({ dossierDeDonnees: dossier, environnement: AU_PLUS_BAVARD });
  fenetre.page.on("response", (reponse) => {
    if (new URL(reponse.url()).pathname.startsWith("/api/")) {
      lectures.push(reponse.text().catch(() => ""));
    }
  });
});

afterAll(async () => {
  await fenetre?.nettoyer();
  if (dossier !== "") {
    await rm(dossier, { recursive: true, force: true, maxRetries: 20, retryDelay: 500 });
  }
});

test("sur un dossier neuf, la carte propose une IA ; la clé saisie s'enregistre, et la page ne la relit jamais", async () => {
  const { page } = fenetre;
  await page.getByText(CONNECTEE).waitFor();

  await page.getByRole("heading", { level: 3, name: "Connecter une IA ?" }).waitFor();
  // Rien n'est écrit tant qu'aucune clé ne l'est : ni trousseau, ni clé.
  expect(existsSync(join(dossier, "Trousseau"))).toBe(false);
  expect(existsSync(join(dossier, "cle-ia.json"))).toBe(false);
  await page.getByRole("button", { name: "Choisir une IA", exact: true }).click();
  await page.getByRole("heading", { level: 3, name: "IA" }).waitFor();

  await page.getByLabel("Claude, d'Anthropic, avec votre clé").check();
  await page.getByRole("button", { name: "Enregistrer ce choix", exact: true }).click();
  await page.getByText("Choix enregistré.", { exact: true }).waitFor();
  const champ = page.getByLabel("Clé d'API de Claude");
  expect(await champ.getAttribute("type")).toBe("password");
  await champ.fill(CLE_DE_CLAUDE);
  await page.getByRole("button", { name: "Enregistrer la clé", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();

  const traces = await page.evaluate(async () => ({
    document: document.documentElement.outerHTML,
    champs: [...document.querySelectorAll("input")].map((champ) => champ.value).join("\n"),
    local: localStorage.length,
    session: sessionStorage.length,
    cookies: document.cookie,
    bases: (await indexedDB.databases()).length,
    caches: (await caches.keys()).length,
  }));
  expect(traces).toMatchObject({ local: 0, session: 0, cookies: "", bases: 0, caches: 0 });
  expect(traces.document).not.toContain(PARTIE);
  expect(traces.champs).not.toContain(PARTIE);
  expect(lectures.length).toBeGreaterThan(0);
  expect(await reponses()).not.toContain(PARTIE);

  // La clé chiffrée et le trousseau sont nés dans le dossier de données ; la carte ne revient plus.
  expect(existsSync(join(dossier, "cle-ia.json"))).toBe(true);
  expect((await readdir(join(dossier, "Trousseau"))).some((nom) => /^key-[0-9a-f-]+\.xml$/u.test(nom))).toBe(true);
  await page.getByRole("button", { name: "Tableau de bord", exact: true }).click();
  await page.getByRole("heading", { level: 2, name: "Tableau de bord" }).waitFor();
  await page.getByText("Aucune candidature enregistrée pour l'instant.").waitFor();
  expect(await page.getByRole("heading", { level: 3, name: "Connecter une IA ?" }).count()).toBe(0);
});

test("« Tester la clé » ne part que sur le clic, vers l'adresse fixée, la clé dans x-api-key, et l'écran dit le résultat", async () => {
  const { page } = fenetre;
  const demandes = join(dossier, "ia-demandes.txt");
  // Enregistrer la clé n'a appelé personne.
  expect(existsSync(demandes)).toBe(false);
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();
  await page
    .getByText(
      "L'essai envoie à Claude une courte question fixe, sans aucune de vos données. Comme tout appel, il est décompté sur votre compte chez Anthropic.",
      { exact: true },
    )
    .waitFor();

  // Le faux fournisseur de la version de test répond selon ce fichier ; aucune requête ne quitte le poste.
  await writeFile(join(dossier, "ia-scenario.txt"), "reussi");
  await page.getByRole("button", { name: "Tester la clé", exact: true }).click();
  await page.getByText("Claude a répondu : la clé fonctionne.", { exact: true }).waitFor();
  await writeFile(join(dossier, "ia-scenario.txt"), "401");
  await page.getByRole("button", { name: "Tester la clé", exact: true }).click();
  await page.getByText("Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA.", { exact: true }).waitFor();
  await writeFile(join(dossier, "ia-scenario.txt"), "redirection");
  await page.getByRole("button", { name: "Tester la clé", exact: true }).click();
  await page.getByText("La réponse de Claude n'a pas pu être lue. Réessayez plus tard.", { exact: true }).waitFor();

  // Trois essais, trois requêtes : la redirection n'a pas été suivie. Chacune vers l'adresse fixée, la clé dans x-api-key
  // seulement, ni dans l'adresse ni dans le corps ; le faux n'en note que l'empreinte.
  const lignes = (await readFile(demandes, "utf8")).trim().split(/\r?\n/u);
  expect(lignes).toHaveLength(3);
  const empreinte = createHash("sha256").update(CLE_DE_CLAUDE).digest("hex");
  for (const ligne of lignes) {
    expect(ligne.startsWith("POST https://api.anthropic.com/v1/messages | ")).toBe(true);
    expect(ligne).toContain(`clé : x-api-key sha256=${empreinte}`);
    expect(ligne).toContain("clé dans l'adresse : non | clé dans le corps : non");
    expect(ligne).toContain("anthropic-version");
    expect(ligne).not.toMatch(/authorization|cookie/iu);
    expect(ligne).not.toContain(PARTIE);
  }

  const html = await page.evaluate(() => document.documentElement.outerHTML);
  expect(html).not.toContain(PARTIE);
  expect(html).not.toContain("SOLLICITO-MESSAGE-DU-FOURNISSEUR");
  expect(await reponses()).not.toContain(PARTIE);
  expect(await reponses()).not.toContain("SOLLICITO-MESSAGE-DU-FOURNISSEUR");
});

test("changer de fournisseur, après une confirmation dans la page, efface la clé", async () => {
  const { page } = fenetre;
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();

  await page.getByLabel("OpenAI, avec votre clé").check();
  await page.getByRole("button", { name: "Enregistrer ce choix", exact: true }).click();
  await page.getByText("Changer de fournisseur efface la clé enregistrée.", { exact: true }).waitFor();
  await page.getByRole("button", { name: "Changer et effacer la clé", exact: true }).click();
  await page.getByText("Aucune clé n'est enregistrée : Sollicito continue sans IA.", { exact: true }).waitFor();
  expect(existsSync(join(dossier, "cle-ia.json"))).toBe(false);

  // Une clé d'OpenAI, qui reste jusqu'à la fermeture.
  await page.getByLabel("Clé d'API d'OpenAI").fill(CLE_D_OPENAI);
  await page.getByRole("button", { name: "Enregistrer la clé", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();
  expect(await reponses()).not.toContain(PARTIE);
});

test("fermée, la fenêtre ne laisse la clé dans aucun fichier, profil compris, ni dans les journaux au niveau Trace", async () => {
  const code = await fenetre.fermer();

  expect(code).toBe(0);
  const sortie = fenetre.sortie();
  // Témoins : les journaux étaient au plus bavard, et la clé a bien été enregistrée (ReglageDeLIA, événement 84).
  expect(sortie).toContain("trce: ");
  expect(sortie).toContain("Sollicito.Api.IA.ReglageDeLIA[84]");
  // Et les essais de la clé (#48) : réussi (91), refusé (92), sans jamais le message du fournisseur ni un journal de HttpClient.
  expect(sortie).toContain("Sollicito.Infrastructure.IA.EnLigne.ClaudeEnLigne[91]");
  expect(sortie).toContain("Sollicito.Infrastructure.IA.EnLigne.ClaudeEnLigne[92]");
  expect(sortie).not.toContain("SOLLICITO-MESSAGE-DU-FOURNISSEUR");
  expect(sortie).not.toContain("System.Net.Http");
  expect(sortie).not.toContain("api.anthropic.com");
  expect(sortie).not.toContain(PARTIE);
  expect(sortie).not.toContain("sk-ant-");
  expect(sortie).not.toContain("sk-proj-");
  expect(await fichiersQuiContiennent(await profilRelache(dossier), PARTIE)).toEqual([]);
  // La base, ses copies, les réglages, le trousseau, et le fichier de la clé, qui ne la porte que chiffrée.
  expect(await fichiersQuiContiennent(dossier, PARTIE)).toEqual([]);
});

test("un trousseau effacé : Sollicito démarre, marche sans IA, et la section dit que la clé est à ressaisir", async () => {
  await rm(join(dossier, "Trousseau"), { recursive: true, force: true });
  fenetre = await lancerLaFenetre({ dossierDeDonnees: dossier, environnement: AU_PLUS_BAVARD });
  const { page } = fenetre;

  await page.getByText(CONNECTEE).waitFor();
  await page.getByText("Aucune candidature enregistrée pour l'instant.").waitFor();
  expect(await page.getByRole("heading", { level: 3, name: "Connecter une IA ?" }).count()).toBe(0);
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page
    .getByText(
      "La clé enregistrée ne peut pas être relue sur ce poste, par exemple après le passage à un autre ordinateur ou à un autre compte Windows. Sollicito continue sans IA : saisissez-la de nouveau.",
      { exact: true },
    )
    .waitFor();

  expect(await fenetre.fermer()).toBe(0);
  expect(fenetre.sortie()).not.toContain(PARTIE);
});
