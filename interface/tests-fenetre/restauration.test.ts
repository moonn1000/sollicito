// La restauration d'une copie dans la vraie fenêtre (#28, ADR-0012), dans la version de test : son dialogue y est remplacé
// par un fichier du dossier de données, et un point d'arrêt permet de tuer l'exe à chaque étape, puis de le relancer.
// Chaque dossier de données est un dossier temporaire neuf : jamais celui de l'utilisateur. L'exe se tue par son PID.
import { existsSync } from "node:fs";
import { copyFile, mkdir, mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { DatabaseSync } from "node:sqlite";
import type { Page } from "playwright-core";
import { afterAll, beforeAll, expect, test } from "vitest";
import { attendre, lancerLaFenetre } from "./outils/fenetre.ts";

const CONNECTEE = "Connectée à l'API locale.";
const dossiers: string[] = [];
// Un dossier de données fait par la fenêtre : une base avec A et B, et la copie du jour faite quand elle n'avait que A.
let modele = "";

async function nouveauDossier(): Promise<string> {
  const dossier = await mkdtemp(join(tmpdir(), "sollicito-restauration-"));
  dossiers.push(dossier);
  return dossier;
}

async function saisir(page: Page, entreprise: string, poste: string) {
  await page.getByRole("button", { name: "Nouvelle candidature" }).click();
  await page.getByLabel("Entreprise").fill(entreprise);
  await page.getByLabel("Poste").fill(poste);
  await page.getByLabel("Date et heure d'envoi").fill("2026-03-27T10:00");
  await page.getByRole("button", { name: "Enregistrer la candidature" }).click();
  await page.getByText(`Candidature enregistrée : ${poste}, chez ${entreprise}.`).waitFor();
  // L'annonce paraît dès le retour au tableau de bord, la liste quand sa lecture revient : on attend sa première ligne.
  await page.getByRole("row").nth(1).waitFor();
}

// Les postes du tableau de bord, une fois sa liste lue.
async function postes(page: Page): Promise<string[]> {
  await page.getByRole("button", { name: "Tableau de bord" }).click();
  await page.getByRole("row").nth(1).waitFor();
  const lignes = await page.getByRole("row").all();
  const textes = await Promise.all(lignes.slice(1).map(async (ligne) => (await ligne.getByRole("cell").allTextContents())[1] ?? ""));
  return textes.sort();
}

async function fichiers(dossier: string): Promise<string[]> {
  return existsSync(dossier) ? (await readdir(dossier)).sort() : [];
}

// La base relue par le SQLite de Node, l'exe arrêté : son contrôle d'intégrité et ses postes.
function relire(fichier: string): { integrite: string; postes: string[] } {
  const base = new DatabaseSync(fichier, { readOnly: true });
  try {
    const integrite = (base.prepare("PRAGMA integrity_check").get() as { integrity_check: string }).integrity_check;
    const lignes = base.prepare("SELECT Poste FROM Candidatures ORDER BY Poste").all() as { Poste: string }[];
    return { integrite, postes: lignes.map((ligne) => ligne.Poste) };
  } finally {
    base.close();
  }
}

// Copie le modèle dans un dossier neuf : la base et son journal, les copies et l'instant de la dernière. Ses copies y sont
// celles d'une autre installation, puisque l'installation se tire du dossier de données.
async function depuisLeModele(): Promise<string> {
  const dossier = await nouveauDossier();
  await mkdir(join(dossier, "Sauvegardes"));
  for (const nom of await fichiers(modele)) {
    if (/^sollicito\.db(-wal)?$|^sauvegardes\.json$/u.test(nom)) {
      await copyFile(join(modele, nom), join(dossier, nom));
    }
  }
  for (const nom of await fichiers(join(modele, "Sauvegardes"))) {
    await copyFile(join(modele, "Sauvegardes", nom), join(dossier, "Sauvegardes", nom));
  }
  return dossier;
}

async function demanderLaRestauration(page: Page, sorte: string) {
  await page.getByRole("button", { name: "Réglages" }).click();
  const section = page.getByRole("region", { name: "Sauvegardes" });
  const ligne = section.getByRole("row").filter({ hasText: sorte });
  await ligne.first().waitFor();
  await ligne.first().getByRole("button", { name: /^Restaurer la copie du / }).click();
}

beforeAll(async () => {
  modele = await nouveauDossier();
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: modele });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await saisir(page, "Atelier Azur fictif", "Comptable A");
    await page.getByRole("button", { name: "Réglages" }).click();
    await page.getByRole("button", { name: "Sauvegarder maintenant" }).click();
    await page.getByText("Copie du jour faite.").waitFor();
    await page.getByRole("button", { name: "Tableau de bord" }).click();
    await saisir(page, "Beta Logistique fictif", "Chauffeur B");
  } finally {
    // Tuée, et non fermée : sa copie de fermeture referait la copie du jour, avec B.
    await fenetre.tuer();
  }
});

afterAll(async () => {
  for (const dossier of dossiers) {
    await rm(dossier, { recursive: true, force: true, maxRetries: 20, retryDelay: 500 });
  }
});

test("une copie se restaure après le oui du dialogue de l'hôte, qui montre sa date et la perte, et la page revient au tableau de bord", async () => {
  const dossier = await depuisLeModele();
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: dossier });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    expect(await postes(page)).toEqual(["Chauffeur B", "Comptable A"]);

    // Sans le fichier qui vaut le oui, la restauration est annulée, et rien ne change.
    await demanderLaRestauration(page, "Copie du jour d'une autre installation");
    await page.getByText("Restauration annulée : votre base n'a pas changé.").waitFor();
    expect(await postes(page)).toEqual(["Chauffeur B", "Comptable A"]);

    await writeFile(join(dossier, "restauration-a-confirmer.txt"), "restaurer", "utf8");
    await demanderLaRestauration(page, "Copie du jour d'une autre installation");
    await page.getByText(/^Base restaurée : vous retrouvez la copie du .+\. Votre base d'avant est gardée dans la copie « Avant une restauration »\.$/u).waitFor();
    expect(await postes(page)).toEqual(["Comptable A"]);

    // Le dialogue a montré la date de la copie et ce qui serait perdu, deux fois.
    const questions = (await readFile(join(dossier, "restaurations-demandees.txt"), "utf8")).split(/\r?\n/u).filter((ligne) => ligne !== "");
    expect(questions).toHaveLength(4);
    expect(questions[2]).toMatch(/^Restaurer la copie du \d{1,2}(er)? [a-zéû]+ \d{4} à \d{2}:\d{2} \?$/u);
    expect(questions[3]).toContain("tout ce que vous avez saisi ou modifié depuis sera perdu");
    expect(questions[3]).toContain("Cette copie vient d'une autre installation de Sollicito");
    // La copie d'avant est gardée, et se liste ; la sauvegarde restaurée est intacte.
    const avant = (await fichiers(join(dossier, "Sauvegardes"))).filter((nom) => nom.startsWith("sollicito-avant-restauration-"));
    expect(avant).toHaveLength(1);
    await page.getByRole("button", { name: "Réglages" }).click();
    await page.getByRole("row").filter({ hasText: "Avant une restauration" }).first().waitFor();
    expect(await fenetre.fermer()).toBe(0);
    expect(relire(join(dossier, "Sauvegardes", avant[0]!)).postes).toEqual(["Chauffeur B", "Comptable A"]);
  } finally {
    await fenetre.nettoyer();
  }
});

// Ce que l'arrêt laisse, au lancement suivant : la base d'avant (A et B), ou la base restaurée (A seul).
const ETAPES: { etape: string; restauree: boolean; copieDAvant: boolean }[] = [
  { etape: "CopieDeTravailEcrite", restauree: false, copieDAvant: false },
  { etape: "CopieDeTravailVerifiee", restauree: false, copieDAvant: false },
  { etape: "PorteFermee", restauree: false, copieDAvant: false },
  { etape: "CopieDAvantEcrite", restauree: false, copieDAvant: false },
  { etape: "CopieDAvantFaite", restauree: false, copieDAvant: true },
  { etape: "PendantLeRemplacement", restauree: false, copieDAvant: true },
  { etape: "BaseRemplacee", restauree: true, copieDAvant: true },
];

test.each(ETAPES)(
  "un arrêt brutal à l'étape $etape laisse une base entière, et le lancement suivant repart sans reste",
  async ({ etape, restauree, copieDAvant }) => {
    const dossier = await depuisLeModele();
    await writeFile(join(dossier, "restauration-a-confirmer.txt"), "restaurer", "utf8");
    const arretee = await lancerLaFenetre({ dossierDeDonnees: dossier, environnement: { SOLLICITO_TEST_ARRET_RESTAURATION: etape } });
    try {
      await arretee.page.getByText(CONNECTEE).waitFor();
      await demanderLaRestauration(arretee.page, "Copie du jour d'une autre installation");
      // Le point d'arrêt écrit son étape, puis attend : l'exe se tue alors par son PID.
      for (let essai = 1; !existsSync(join(dossier, "restauration-arretee.txt")); essai++) {
        expect(essai, "l'étape n'a pas été atteinte").toBeLessThan(120);
        await attendre(250);
      }
      expect(await readFile(join(dossier, "restauration-arretee.txt"), "utf8")).toBe(etape);
    } finally {
      await arretee.tuer();
    }

    const relancee = await lancerLaFenetre({ dossierDeDonnees: dossier });
    try {
      await relancee.page.getByText(CONNECTEE).waitFor();
      expect(await postes(relancee.page)).toEqual(restauree ? ["Comptable A"] : ["Chauffeur B", "Comptable A"]);
      expect(await relancee.fermer()).toBe(0);
    } finally {
      await relancee.nettoyer();
    }

    // Aucun reste : ni copie de travail, ni fichier provisoire ; la copie d'avant, si elle était finie, est là.
    expect((await fichiers(dossier)).filter((nom) => nom.includes(".restauration"))).toEqual([]);
    const copies = await fichiers(join(dossier, "Sauvegardes"));
    expect(copies.filter((nom) => nom.endsWith(".partielle"))).toEqual([]);
    expect(copies.filter((nom) => /^sollicito-avant-restauration-\d{8}-\d{9}\.db$/u.test(nom))).toHaveLength(copieDAvant ? 1 : 0);
    // La base, relue par un autre SQLite une fois l'exe arrêté : entière, jamais à moitié remplacée.
    const base = relire(join(dossier, "sollicito.db"));
    expect(base.integrite).toBe("ok");
    expect(base.postes).toEqual(restauree ? ["Comptable A"] : ["Chauffeur B", "Comptable A"]);
  },
);
