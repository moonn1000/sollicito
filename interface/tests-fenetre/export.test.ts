// L'export de toutes les données dans la vraie fenêtre (#49, ADR-0020), dans la version de test : son dialogue « Enregistrer
// sous » y est remplacé par un fichier du dossier de données, qui donne le chemin, et le oui à remplacer un fichier existant
// sur sa seconde ligne. Chaque dossier, de données comme d'export, est un dossier temporaire neuf : jamais un dossier de
// l'utilisateur. L'exe se tue par son PID.
import { existsSync } from "node:fs";
import { mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { Page } from "playwright-core";
import { afterAll, expect, test } from "vitest";
import { attendre, lancerLaFenetre } from "./outils/fenetre.ts";

const CONNECTEE = "Connectée à l'API locale.";
const EXPORTE = "Export enregistré dans le fichier choisi.";
const ANNULE = "Export annulé : rien n'a été enregistré.";
const dossiers: string[] = [];

async function nouveauDossier(prefixe: string): Promise<string> {
  const dossier = await mkdtemp(join(tmpdir(), prefixe));
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

async function exporterEnJson(page: Page) {
  await page.getByRole("button", { name: "Réglages" }).click();
  await page.getByRole("region", { name: "Exporter vos données" }).getByRole("button", { name: "Exporter en JSON…" }).click();
}

async function fichiers(dossier: string): Promise<string[]> {
  return existsSync(dossier) ? (await readdir(dossier)).sort() : [];
}

afterAll(async () => {
  for (const dossier of dossiers) {
    await rm(dossier, { recursive: true, force: true, maxRetries: 20, retryDelay: 500 });
  }
});

test("un export JSON s'enregistre dans le fichier choisi, sans que la page ni les journaux n'en voient le chemin", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  const fichier = join(sortie, "export-de-Jeanne-Fictive.json");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees, environnement: { Logging__LogLevel__Default: "Trace" } });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await saisir(page, "Atelier Éole Fictif-Export", "Comptable « senior » Poste-Export");
    await writeFile(join(donnees, "export-a-choisir.txt"), fichier, "utf8");

    await exporterEnJson(page);
    await page.getByText(EXPORTE).waitFor();

    const export_ = JSON.parse(await readFile(fichier, "utf8")) as {
      format: string;
      version: number;
      entreprises: { nom: string }[];
      candidatures: { poste: string }[];
    };
    expect(export_.format).toBe("sollicito-export");
    expect(export_.version).toBe(1);
    expect(export_.entreprises.map((entreprise) => entreprise.nom)).toEqual(["Atelier Éole Fictif-Export"]);
    expect(export_.candidatures.map((candidature) => candidature.poste)).toEqual(["Comptable « senior » Poste-Export"]);
    // Rien de provisoire ne reste à côté de l'export.
    expect(await fichiers(sortie)).toEqual(["export-de-Jeanne-Fictive.json"]);

    // Le remplaçant du dialogue a reçu le format et le nom proposé, au jour local.
    const demandes = (await readFile(join(donnees, "exports-demandes.txt"), "utf8")).split(/\r?\n/u).filter((ligne) => ligne !== "");
    expect(demandes).toEqual([expect.stringMatching(/^Json sollicito-export-\d{4}-\d{2}-\d{2}\.json$/u)]);

    // Ni la page, ni les journaux au niveau Trace, ne portent le chemin ou le nom du fichier. La page montre déjà le dossier
    // des sauvegardes, sous le dossier de données (#26) : seuls les journaux ne le portent jamais.
    const document = await page.content();
    for (const interdit of [sortie, "export-de-Jeanne-Fictive"]) {
      expect(document).not.toContain(interdit);
    }

    expect(await fenetre.fermer()).toBe(0);
    // La sortie de l'exe est en page de code OEM : les marques se cherchent par leur partie ASCII.
    const journaux = fenetre.sortie();
    expect(journaux).toMatch(/Export Json fait : 1 entreprise\(s\), 0 contact\(s\), 0 offre\(s\), 1 candidature\(s\), 0 relance\(s\) et 0 /u);
    for (const interdit of [sortie, "export-de-Jeanne-Fictive", donnees, "Fictif-Export", "Poste-Export"]) {
      expect(journaux).not.toContain(interdit);
    }
  } finally {
    await fenetre.nettoyer();
  }
});

test("sans choix l'export est annulé ; un fichier existant n'est remplacé qu'avec le oui", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  const fichier = join(sortie, "export.json");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();

    // Le dialogue fermé sans choix : rien ne s'écrit.
    await exporterEnJson(page);
    await page.getByText(ANNULE).waitFor();
    expect(await fichiers(sortie)).toEqual([]);

    // Un fichier existant, sans le oui à le remplacer : le vrai dialogue ne le rendrait pas ; il reste intact.
    await writeFile(fichier, "contenu d'avant", "utf8");
    await writeFile(join(donnees, "export-a-choisir.txt"), fichier, "utf8");
    await page.getByRole("button", { name: "Tableau de bord" }).click();
    await exporterEnJson(page);
    await page.getByText(ANNULE).waitFor();
    expect(await readFile(fichier, "utf8")).toBe("contenu d'avant");

    // Avec le oui, il est remplacé, et rien de provisoire ne reste.
    await writeFile(join(donnees, "export-a-choisir.txt"), `${fichier}\nremplacer`, "utf8");
    await page.getByRole("button", { name: "Tableau de bord" }).click();
    await exporterEnJson(page);
    await page.getByText(EXPORTE).waitFor();
    expect((JSON.parse(await readFile(fichier, "utf8")) as { format: string }).format).toBe("sollicito-export");
    expect(await fichiers(sortie)).toEqual(["export.json"]);
    expect(await fenetre.fermer()).toBe(0);
  } finally {
    await fenetre.nettoyer();
  }
});

test("pendant une restauration arrêtée en route, une demande d'export reçoit « occupé » et n'écrit rien", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  await writeFile(join(donnees, "restauration-a-confirmer.txt"), "restaurer", "utf8");
  await writeFile(join(donnees, "export-a-choisir.txt"), join(sortie, "export.json"), "utf8");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees, environnement: { SOLLICITO_TEST_ARRET_RESTAURATION: "PorteFermee" } });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await saisir(page, "Atelier Azur fictif", "Comptable A");
    await page.getByRole("button", { name: "Réglages" }).click();
    await page.getByRole("button", { name: "Sauvegarder maintenant" }).click();
    await page.getByText("Copie du jour faite.").waitFor();
    const ligne = page.getByRole("region", { name: "Sauvegardes" }).getByRole("row").filter({ hasText: "Copie du jour" });
    await ligne.first().getByRole("button", { name: /^Restaurer la copie du / }).click();
    // Le point d'arrêt écrit son étape, puis attend, porte fermée : la restauration reste en cours.
    for (let essai = 1; !existsSync(join(donnees, "restauration-arretee.txt")); essai++) {
      expect(essai, "l'étape n'a pas été atteinte").toBeLessThan(120);
      await attendre(250);
    }

    await page.getByRole("region", { name: "Exporter vos données" }).getByRole("button", { name: "Exporter en JSON…" }).click();
    await page.getByText("Sollicito est occupé : réessayez dans un instant. Rien n'a été enregistré.").waitFor();
    expect(await fichiers(sortie)).toEqual([]);
    // Le dialogue de l'export ne s'est pas ouvert.
    expect(existsSync(join(donnees, "exports-demandes.txt"))).toBe(false);
  } finally {
    await fenetre.tuer();
    await fenetre.nettoyer();
  }
});
