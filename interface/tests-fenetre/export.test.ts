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
const EXPORT_OCCUPE = "Sollicito est occupé : réessayez dans un instant. Rien n'a été enregistré.";
const RESTAURATION_OCCUPEE = "Sollicito est occupé : réessayez dans un instant. Votre base n'a pas changé.";
const CHOIX_OCCUPE = "Sollicito est occupé : réessayez dans un instant. Le dossier des sauvegardes n'a pas changé.";
const dossiers: string[] = [];

type CanalDeLaPage = {
  chrome: { webview: { postMessage(message: unknown): void; addEventListener(type: "message", ecouteur: (evenement: { data: unknown }) => void): void } };
  messagesRecus?: unknown[];
};

// Note dans la page chaque message que l'hôte lui envoie, pour que le test lise aussi ceux que l'écran ne montre pas.
async function ecouterLHote(page: Page) {
  await page.evaluate(() => {
    const fenetre = window as unknown as CanalDeLaPage;
    fenetre.messagesRecus = [];
    fenetre.chrome.webview.addEventListener("message", (evenement) => fenetre.messagesRecus!.push(evenement.data));
  });
}

async function messagesDeLExport(page: Page): Promise<unknown[]> {
  const recus = await page.evaluate(() => (window as unknown as CanalDeLaPage).messagesRecus ?? []);
  return recus.filter((message) => (message as { type?: unknown }).type === "export");
}

async function messagesDuChoix(page: Page): Promise<unknown[]> {
  const recus = await page.evaluate(() => (window as unknown as CanalDeLaPage).messagesRecus ?? []);
  return recus.filter((message) => (message as { type?: unknown }).type === "dossierDesSauvegardes");
}

// Envoie un message à l'hôte comme le ferait un script de la page, sans passer par un bouton.
async function envoyerALHote(page: Page, message: unknown) {
  await page.evaluate((aEnvoyer) => (window as unknown as CanalDeLaPage).chrome.webview.postMessage(aEnvoyer), message);
}

async function attendreLeFichier(fichier: string) {
  for (let essai = 1; !existsSync(fichier); essai++) {
    expect(essai, `${fichier} n'est pas apparu`).toBeLessThan(120);
    await attendre(250);
  }
}

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

test("un export CSV s'enregistre en archive ZIP d'un fichier par entité", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  const fichier = join(sortie, "export.zip");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await saisir(page, "=Atelier fictif", "Comptable");
    await writeFile(join(donnees, "export-a-choisir.txt"), fichier, "utf8");

    await page.getByRole("button", { name: "Réglages" }).click();
    await page.getByRole("region", { name: "Exporter vos données" }).getByRole("button", { name: "Exporter en CSV…" }).click();
    await page.getByText(EXPORTE).waitFor();

    // Une archive ZIP, dont l'annuaire nomme les sept fichiers ; leur contenu se vérifie dans les tests de l'API.
    const octets = await readFile(fichier);
    expect(octets.subarray(0, 4)).toEqual(Buffer.from([0x50, 0x4b, 0x03, 0x04]));
    for (const nom of ["export.csv", "entreprises.csv", "contacts.csv", "offres.csv", "candidatures.csv", "relances.csv", "evenements.csv"]) {
      expect(octets.includes(Buffer.from(nom, "ascii"))).toBe(true);
    }
    expect(await fichiers(sortie)).toEqual(["export.zip"]);
    const demandes = await readFile(join(donnees, "exports-demandes.txt"), "utf8");
    expect(demandes).toMatch(/^Csv sollicito-export-\d{4}-\d{2}-\d{2}\.zip\r?\n$/u);
    expect(await fenetre.fermer()).toBe(0);
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

    // Un choix de dossier reçoit « occupé », sans dialogue (#118). Les boutons des sauvegardes attendent la fin de la
    // restauration : un script de la page le demande.
    await ecouterLHote(page);
    await envoyerALHote(page, { type: "choisirLeDossierDesSauvegardes" });
    for (let essai = 1; (await messagesDuChoix(page)).length === 0; essai++) {
      expect(essai, "la demande de choix n'a pas reçu de réponse").toBeLessThan(80);
      await attendre(250);
    }
    expect(await messagesDuChoix(page)).toEqual([{ type: "dossierDesSauvegardes", issue: "occupe" }]);
  } finally {
    await fenetre.tuer();
    await fenetre.nettoyer();
  }
});

test("pendant un export arrêté en route, un seul dialogue de l'hôte à la fois (#118)", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  const autreDossier = await nouveauDossier("sollicito-dossier-");
  await writeFile(join(donnees, "export-a-choisir.txt"), join(sortie, "export.json"), "utf8");
  await writeFile(join(donnees, "restauration-a-confirmer.txt"), "restaurer", "utf8");
  await writeFile(join(donnees, "dossier-a-choisir.txt"), autreDossier, "utf8");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees, environnement: { SOLLICITO_TEST_ARRET_EXPORT: "EntreprisesEcrites" } });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await saisir(page, "Atelier Azur fictif", "Comptable A");
    await page.getByRole("button", { name: "Réglages" }).click();
    await page.getByRole("button", { name: "Sauvegarder maintenant" }).click();
    await page.getByText("Copie du jour faite.").waitFor();
    await ecouterLHote(page);

    await page.getByRole("region", { name: "Exporter vos données" }).getByRole("button", { name: "Exporter en JSON…" }).click();
    // Le point d'arrêt écrit son étape, puis attend : l'export reste en cours.
    await attendreLeFichier(join(donnees, "export-arrete.txt"));

    // Une demande du même format ne reçoit rien, et une de l'autre format reçoit « occupé » ; l'hôte répond dans l'ordre,
    // donc une réponse à la première arriverait avant celle de la seconde.
    await envoyerALHote(page, { type: "exporterLesDonnees", format: "json" });
    await envoyerALHote(page, { type: "exporterLesDonnees", format: "csv" });
    for (let essai = 1; (await messagesDeLExport(page)).length < 2; essai++) {
      expect(essai, "la demande du CSV n'a pas reçu de réponse").toBeLessThan(80);
      await attendre(250);
    }
    expect(await messagesDeLExport(page)).toEqual([
      { type: "export", format: "json", issue: "enCours" },
      { type: "export", format: "csv", issue: "occupe" },
    ]);

    // Une restauration reçoit « occupée », sans dialogue ; un choix de dossier reçoit « occupé », sans dialogue (#118).
    const ligne = page.getByRole("region", { name: "Sauvegardes" }).getByRole("row").filter({ hasText: "Copie du jour" });
    await ligne.first().getByRole("button", { name: /^Restaurer la copie du / }).click();
    await page.getByText(RESTAURATION_OCCUPEE).waitFor();
    expect(existsSync(join(donnees, "restaurations-demandees.txt"))).toBe(false);
    await page.getByRole("button", { name: "Choisir un autre dossier…" }).click();
    await page.getByText(CHOIX_OCCUPE).waitFor();
    expect(await fichiers(autreDossier)).toEqual([]);

    // L'export reprend, et va au bout : la page qui attendait le sien en reçoit la vraie issue, une seule fois.
    await writeFile(join(donnees, "export-a-reprendre.txt"), "", "utf8");
    await page.getByText(EXPORTE).waitFor();
    expect(await fichiers(sortie)).toEqual(["export.json"]);
    expect((await messagesDeLExport(page)).slice(2)).toEqual([{ type: "export", format: "json", issue: "exporte" }]);
    expect(await fenetre.fermer()).toBe(0);
  } finally {
    await fenetre.nettoyer();
  }
});

test("pendant un choix de dossier, un export et une restauration reçoivent « occupé » (#118)", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  const autreDossier = await nouveauDossier("sollicito-dossier-");
  await writeFile(join(donnees, "export-a-choisir.txt"), join(sortie, "export.json"), "utf8");
  await writeFile(join(donnees, "restauration-a-confirmer.txt"), "restaurer", "utf8");
  await writeFile(join(donnees, "dossier-a-choisir.txt"), autreDossier, "utf8");
  // Tant que ce fichier existe, le choix reste ouvert, comme un dialogue que l'utilisateur n'a pas encore fermé.
  await writeFile(join(donnees, "dossier-en-attente.txt"), "", "utf8");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await page.getByRole("button", { name: "Réglages" }).click();
    await ecouterLHote(page);
    await page.getByRole("button", { name: "Choisir un autre dossier…" }).click();

    // L'hôte traite les messages dans l'ordre : celui-ci arrive pendant le choix.
    await page.getByRole("region", { name: "Exporter vos données" }).getByRole("button", { name: "Exporter en JSON…" }).click();
    await page.getByText(EXPORT_OCCUPE).waitFor();
    expect(existsSync(join(donnees, "exports-demandes.txt"))).toBe(false);

    // Les boutons de restauration attendent la fin du choix : un script de la page demande la restauration d'une copie.
    await envoyerALHote(page, { type: "restaurerUneSauvegarde", copie: "0123456789abcdef" });
    for (let essai = 1; ; essai++) {
      const recus = await page.evaluate(() => (window as unknown as CanalDeLaPage).messagesRecus ?? []);
      if (recus.some((message) => (message as { type?: unknown }).type === "restauration")) {
        expect(recus.filter((message) => (message as { type?: unknown }).type === "restauration")).toEqual([
          { type: "restauration", copie: "0123456789abcdef", issue: "occupee" },
        ]);
        break;
      }
      expect(essai, "la demande de restauration n'a pas reçu de réponse").toBeLessThan(80);
      await attendre(250);
    }
    expect(existsSync(join(donnees, "restaurations-demandees.txt"))).toBe(false);

    // Un second choix, demandé par un script, ne reçoit rien : la page qui attend le premier en recevra la vraie issue.
    await envoyerALHote(page, { type: "choisirLeDossierDesSauvegardes" });
    // L'hôte traite les messages dans l'ordre : la réponse à cet export dit que le second choix a été traité, pendant le
    // premier.
    await envoyerALHote(page, { type: "exporterLesDonnees", format: "csv" });
    for (let essai = 1; !(await messagesDeLExport(page)).some((message) => (message as { format?: unknown }).format === "csv"); essai++) {
      expect(essai, "la demande d'export n'a pas reçu de réponse").toBeLessThan(80);
      await attendre(250);
    }
    expect(await messagesDeLExport(page)).toContainEqual({ type: "export", format: "csv", issue: "occupe" });

    // Le choix se ferme sur le dossier écrit par le test, qui reçoit une copie ; une seule réponse au choix arrive.
    await rm(join(donnees, "dossier-en-attente.txt"));
    await page.getByText("Dossier des sauvegardes changé : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.").waitFor();
    await attendre(1_000);
    expect(await messagesDuChoix(page)).toEqual([{ type: "dossierDesSauvegardes", issue: "choisi" }]);
    expect(await fichiers(sortie)).toEqual([]);
    expect(await fenetre.fermer()).toBe(0);
  } finally {
    await fenetre.nettoyer();
  }
});

test("fermer pendant un export l'interrompt, et attend qu'il efface son provisoire (#118)", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  await writeFile(join(donnees, "export-a-choisir.txt"), join(sortie, "export.json"), "utf8");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees, environnement: { SOLLICITO_TEST_ARRET_EXPORT: "EntreprisesEcrites" } });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await exporterEnJson(page);
    await attendreLeFichier(join(donnees, "export-arrete.txt"));
    expect(await fichiers(sortie)).toEqual([expect.stringMatching(/^sollicito-export-[0-9a-f]{8}\.partiel$/u)]);

    // La fermeture interrompt l'export, qui reprend une seconde plus tard, voit l'interruption, et efface son provisoire.
    const fermeture = fenetre.fermer();
    await attendre(1_000);
    await writeFile(join(donnees, "export-a-reprendre.txt"), "", "utf8");
    // L'attente ne va pas à son terme : pas d'événement 136, et le provisoire est effacé ; la durée, qui dépend de la
    // charge du poste, ne se compare pas.
    expect(await fermeture).toBe(0);
    expect(await fichiers(sortie)).toEqual([]);
    // La sortie de l'exe est en page de code OEM : les marques se cherchent par leur partie ASCII.
    const journaux = fenetre.sortie();
    expect(journaux).toContain("ExportDesDonnees[134]");
    expect(journaux).not.toContain("ExportDesDonnees[136]");
  } finally {
    await fenetre.nettoyer();
  }
});

test("au-delà de cinq secondes, la fenêtre se ferme quand même, et le journal dit que le provisoire a pu rester (#118)", async () => {
  const donnees = await nouveauDossier("sollicito-fenetre-");
  const sortie = await nouveauDossier("sollicito-export-");
  await writeFile(join(donnees, "export-a-choisir.txt"), join(sortie, "export.json"), "utf8");
  const fenetre = await lancerLaFenetre({ dossierDeDonnees: donnees, environnement: { SOLLICITO_TEST_ARRET_EXPORT: "EntreprisesEcrites" } });
  try {
    const { page } = fenetre;
    await page.getByText(CONNECTEE).waitFor();
    await exporterEnJson(page);
    await attendreLeFichier(join(donnees, "export-arrete.txt"));

    // L'export ne reprend jamais : la fenêtre l'attend cinq secondes, puis se ferme sans lui.
    const debut = Date.now();
    expect(await fenetre.fermer()).toBe(0);
    expect(Date.now() - debut).toBeGreaterThanOrEqual(5_000);
    expect(fenetre.sortie()).toContain("ExportDesDonnees[136]");
    // Le provisoire est resté dans le dossier choisi, un dossier temporaire du test, qui le supprime à la fin.
    expect(await fichiers(sortie)).toEqual([expect.stringMatching(/^sollicito-export-[0-9a-f]{8}\.partiel$/u)]);
  } finally {
    await fenetre.nettoyer();
  }
});
