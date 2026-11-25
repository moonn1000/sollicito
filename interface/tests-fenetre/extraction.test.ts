// L'extraction d'une offre collée dans la vraie fenêtre (#46, ADR-0018), dans la version de test, journaux de l'API au
// niveau Trace. Le dossier de données est un dossier temporaire neuf : jamais celui de l'utilisateur. Le faux fournisseur
// de la version de test répond à la place du réseau : son scénario « texte » rend, comme texte de l'IA, le contenu de
// ia-texte.txt. La clé et le texte de l'offre sont fictifs et reconnaissables. Les tests se suivent dans l'ordre.
import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { Request } from "playwright-core";
import { afterAll, beforeAll, expect, test } from "vitest";
import { lancerLaFenetre, type FenetreLancee } from "./outils/fenetre.ts";
import { fichiersQuiContiennent, profilRelache } from "./outils/fichiers.ts";

const CONNECTEE = "Connectée à l'API locale.";
// Les parties reconnaissables, en ASCII : la sortie de l'exe est dans la page de code OEM.
const PARTIE_DE_LA_CLE = "SOLLICITO-ESSAI-46";
const CLE_DE_CLAUDE = `sk-ant-api03-${PARTIE_DE_LA_CLE}-cle-fictive-de-la-fenetre`;
const MARQUE_DU_TEXTE = "SOLLICITO-TEXTE-46";
const ADRESSE = "https://emplois.exemple/offres/42";
const TEXTE = `Lumiere Verte SA recrute un comptable a Liege.\nPostulez sur ${ADRESSE} avant le 15 decembre.\n${MARQUE_DU_TEXTE}`;
const PROPOSITION = JSON.stringify({ entreprise: "Lumiere Verte SA", poste: "Comptable", lieu: "Liege", publieeLe: "2026-11-12", adresse: ADRESSE });
const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script>";
const AU_PLUS_BAVARD = { Logging__LogLevel__Default: "Trace" };

let dossier = "";
let fenetre: FenetreLancee;
const lectures: Promise<string>[] = [];
const reponses = async () => (await Promise.all(lectures)).join(" | ");

beforeAll(async () => {
  dossier = await mkdtemp(join(tmpdir(), "sollicito-extraction-"));
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

async function collerUneOffre() {
  const { page } = fenetre;
  await page.getByRole("button", { name: "Offres", exact: true }).click();
  await page.getByRole("button", { name: "Coller une offre", exact: true }).click();
  await page.getByRole("heading", { level: 2, name: "Coller une offre" }).waitFor();
}

// La base et son journal (WAL), tels qu'ils sont sur le disque : un octet écrit, dans l'un ou dans l'autre, change l'empreinte.
async function empreinteDeLaBase(): Promise<string> {
  const fichier = join(dossier, "sollicito.db");
  const octets = [await readFile(fichier)];
  if (existsSync(`${fichier}-wal`)) {
    octets.push(await readFile(`${fichier}-wal`));
  }

  return createHash("sha256").update(Buffer.concat(octets)).digest("hex");
}

test("sans IA prête, l'écran le dit, et rien ne part", async () => {
  const { page } = fenetre;
  await page.getByText(CONNECTEE).waitFor();

  await collerUneOffre();

  await page
    .getByText(
      "Sans IA prête, remplissez les champs vous-même. Pour vous faire aider, choisissez une IA et enregistrez sa clé dans les réglages, section « IA ».",
      { exact: true },
    )
    .waitFor();
  expect(await page.getByRole("button", { name: "Proposer les champs avec l'IA" }).count()).toBe(0);
  expect(existsSync(join(dossier, "ia-demandes.txt"))).toBe(false);
});

test("avec Claude prêt, la proposition remplit les champs vides, marqués, puis l'offre corrigée s'enregistre", async () => {
  const { page } = fenetre;
  await page.getByRole("button", { name: "Réglages", exact: true }).click();
  await page.getByLabel("Claude, d'Anthropic, avec votre clé").check();
  await page.getByRole("button", { name: "Enregistrer ce choix", exact: true }).click();
  await page.getByText("Choix enregistré.", { exact: true }).waitFor();
  await page.getByLabel("Clé d'API de Claude").fill(CLE_DE_CLAUDE);
  await page.getByRole("button", { name: "Enregistrer la clé", exact: true }).click();
  await page.getByText("Une clé est enregistrée.", { exact: true }).waitFor();
  await writeFile(join(dossier, "ia-scenario.txt"), "texte");
  await writeFile(join(dossier, "ia-texte.txt"), PROPOSITION);

  await collerUneOffre();
  await page.getByLabel("Texte de l'offre").fill(TEXTE);
  // Coller et taper n'appellent personne.
  expect(existsSync(join(dossier, "ia-demandes.txt"))).toBe(false);
  await page.getByRole("button", { name: "Proposer les champs avec l'IA", exact: true }).click();
  await page.getByText("L'IA a proposé 5 champs : relisez-les, et corrigez-les au besoin, avant d'enregistrer l'offre.", { exact: true }).waitFor();

  expect(await page.getByLabel("Entreprise").inputValue()).toBe("Lumiere Verte SA");
  expect(await page.getByLabel("Adresse de l'annonce").inputValue()).toBe(ADRESSE);
  expect(await page.getByText("Proposé par l'IA : relisez-le.", { exact: true }).count()).toBe(5);
  // Une seule demande, vers l'adresse fixée ; rien n'est encore enregistré.
  const demandes = (await readFile(join(dossier, "ia-demandes.txt"), "utf8")).trim().split(/\r?\n/u);
  expect(demandes).toHaveLength(1);
  expect(demandes[0]!.startsWith("POST https://api.anthropic.com/v1/messages | ")).toBe(true);
  expect(demandes[0]).toContain("clé dans l'adresse : non | clé dans le corps : non");

  await page.getByLabel("Poste").fill("Comptable senior");
  await page.getByRole("button", { name: "Enregistrer l'offre", exact: true }).click();
  await page.getByText("Offre enregistrée : Comptable senior, chez Lumiere Verte SA.", { exact: true }).waitFor();
});

test("une proposition retirée, puis un écran quitté, n'écrivent rien", async () => {
  const { page } = fenetre;
  await collerUneOffre();
  await page.getByLabel("Texte de l'offre").fill(`${TEXTE}\nUne autre offre.`);
  await page.getByRole("button", { name: "Proposer les champs avec l'IA", exact: true }).click();
  await page.getByRole("button", { name: "Retirer la proposition", exact: true }).click();

  expect(await page.getByLabel("Entreprise").inputValue()).toBe("");
  expect(await page.getByText("Proposé par l'IA : relisez-le.", { exact: true }).count()).toBe(0);
  // Une nouvelle proposition, puis l'écran quitté sans enregistrer.
  await page.getByRole("button", { name: "Proposer les champs avec l'IA", exact: true }).click();
  await page.getByText("Proposé par l'IA : relisez-le.", { exact: true }).first().waitFor();
  await page.getByRole("button", { name: "Offres", exact: true }).click();
  await page.locator("table.offres tbody tr").first().waitFor();

  // La seule offre de la liste est celle du test précédent.
  expect(await page.locator("table.offres tbody tr").count()).toBe(1);
});

test("une réponse piégée est écartée en entier : rien n'est proposé, rien ne s'exécute, aucune navigation, aucune fenêtre de plus, rien en base, et l'écran le dit", async () => {
  const { page, navigateur } = fenetre;
  await collerUneOffre();
  await page.getByLabel("Texte de l'offre").fill(`${PIEGE}\n${TEXTE}\nTroisième offre.`);
  // Ce qu'une réponse piégée ne doit jamais provoquer : une navigation, une fenêtre de plus, une écriture en base.
  const navigations: string[] = [];
  const fenetresDeLaPage: string[] = [];
  const appelsDeLAPI: Request[] = [];
  page.on("framenavigated", (cadre) => navigations.push(cadre.url()));
  page.context().on("page", (nouvelle) => fenetresDeLaPage.push(nouvelle.url()));
  page.on("request", (requete) => {
    if (new URL(requete.url()).pathname.startsWith("/api/")) {
      appelsDeLAPI.push(requete);
    }
  });
  const pagesAvant = navigateur.contexts().flatMap((contexte) => contexte.pages()).length;
  const adresseAvant = page.url();
  const baseAvant = await empreinteDeLaBase();
  for (const reponse of [
    JSON.stringify({ entreprise: PIEGE, poste: "Comptable", lieu: null, publieeLe: null, adresse: "javascript:alert(1)" }),
    JSON.stringify({ entreprise: null, poste: null, lieu: null, publieeLe: null, adresse: null, inconnue: PIEGE }),
    `Voici : ${PIEGE}`,
  ]) {
    await writeFile(join(dossier, "ia-texte.txt"), reponse);
    await page.getByRole("button", { name: "Proposer les champs avec l'IA", exact: true }).click();
    await page
      .getByText("La réponse de Claude ne correspond pas à ce qu'attend Sollicito : elle a été écartée. Réessayez, ou remplissez les champs vous-même.", {
        exact: true,
      })
      .waitFor();
    expect(await page.getByLabel("Entreprise").inputValue()).toBe("");
    expect(await page.getByLabel("Poste").inputValue()).toBe("");
  }

  // Aucune balise de la réponse ni du texte ne s'est créée, et aucun lien ne s'est ouvert.
  expect(await page.locator("main img, main script, main a[href^='javascript']").count()).toBe(0);
  expect(existsSync(join(dossier, "liens-ouverts.txt"))).toBe(false);
  // Aucune navigation, aucune fenêtre de plus.
  expect(navigations).toEqual([]);
  expect(fenetresDeLaPage).toEqual([]);
  expect(page.url()).toBe(adresseAvant);
  expect(navigateur.contexts().flatMap((contexte) => contexte.pages()).length).toBe(pagesAvant);
  // Rien en base : le fichier de la base et son journal n'ont pas bougé d'un octet, et l'API ne connaît toujours que
  // l'offre et l'entreprise du premier test.
  expect(await empreinteDeLaBase()).toBe(baseAvant);
  const jeton = (await appelsDeLAPI[0]!.allHeaders())["sollicito-jeton"] ?? "";
  const lire = (chemin: string) =>
    page.evaluate(
      async ({ chemin, jeton }) => (await (await fetch(chemin, { headers: { "Sollicito-Jeton": jeton } })).json()) as unknown[],
      { chemin, jeton },
    );
  expect(await lire("/api/offres")).toHaveLength(1);
  expect(await lire("/api/entreprises")).toHaveLength(1);
});

test("un échec du fournisseur se dit, et la saisie à la main reste possible", async () => {
  const { page } = fenetre;
  await writeFile(join(dossier, "ia-scenario.txt"), "429");
  await page.getByRole("button", { name: "Proposer les champs avec l'IA", exact: true }).click();
  await page
    .getByText(
      "Claude refuse pour l'instant : limite d'usage ou crédit épuisé sur votre compte chez Anthropic. Réessayez plus tard, ou vérifiez votre compte. Vous pouvez remplir les champs vous-même.",
      { exact: true },
    )
    .waitFor();

  await page.getByLabel("Poste").fill("Chauffeur");
  await page.getByRole("button", { name: "Enregistrer l'offre", exact: true }).click();
  await page.getByText("Offre enregistrée : Chauffeur.", { exact: true }).waitFor();
});

test("fermée, la fenêtre ne laisse ni la clé ni le texte de l'offre dans les journaux au niveau Trace, et la clé dans aucun fichier", async () => {
  const code = await fenetre.fermer();

  expect(code).toBe(0);
  const sortie = fenetre.sortie();
  expect(sortie).toContain("trce: ");
  expect(sortie).toContain("Sollicito.Api.Offres.ExtractionDesOffres[100]");
  expect(sortie).toContain("Sollicito.Api.Offres.ExtractionDesOffres[102]");
  expect(sortie).toContain("Sollicito.Api.Offres.ExtractionDesOffres[103]");
  for (const interdit of [PARTIE_DE_LA_CLE, "sk-ant-", MARQUE_DU_TEXTE, "emplois.exemple", "Lumiere Verte", "onerror", "javascript", "api.anthropic.com"]) {
    expect(sortie).not.toContain(interdit);
  }

  expect(await reponses()).not.toContain(PARTIE_DE_LA_CLE);
  // Le profil du navigateur intégré, une fois relâché : il écrit encore pendant que l'exe s'arrête.
  expect(await fichiersQuiContiennent(await profilRelache(dossier), PARTIE_DE_LA_CLE)).toEqual([]);
  // La base, ses copies, les réglages, le trousseau, et le fichier de la clé, qui ne la porte que chiffrée.
  expect(await fichiersQuiContiennent(dossier, PARTIE_DE_LA_CLE)).toEqual([]);
});
