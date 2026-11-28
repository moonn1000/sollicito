import { afterEach, expect, test, vi } from "vitest";
import { CanalSimule, JETON_FICTIF } from "../tests/canalSimule.ts";
import { demanderLaRestauration, demanderLeBrouillon, demanderLeChoixDuDossier, demanderLeJeton, demanderLExport } from "./canalDeLHote.ts";

afterEach(() => {
  vi.useRealTimers();
});

test("la page signale à l'hôte qu'elle est prête, puis reçoit le jeton qu'il remet", async () => {
  const canal = new CanalSimule();

  const jeton = demanderLeJeton(canal);
  expect(canal.messagesRecus).toEqual([{ type: "prete" }]);
  canal.envoyer({ type: "jeton", jeton: JETON_FICTIF });

  await expect(jeton).resolves.toBe(JETON_FICTIF);
  expect(canal.abonnes).toBe(0);
});

test("un message qui n'est pas un jeton de la bonne forme est ignoré", async () => {
  const canal = new CanalSimule();

  const jeton = demanderLeJeton(canal);
  canal.envoyer("texte");
  canal.envoyer(null);
  canal.envoyer({ type: "autre", jeton: JETON_FICTIF });
  canal.envoyer({ type: "jeton", jeton: 42 });
  canal.envoyer({ type: "jeton", jeton: "trop-court" });
  canal.envoyer({ type: "jeton", jeton: JETON_FICTIF + "x" });
  canal.envoyer({ type: "jeton", jeton: JETON_FICTIF.slice(0, 42) + "=" });
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "jeton", jeton: JETON_FICTIF });

  await expect(jeton).resolves.toBe(JETON_FICTIF);
});

test("sans jeton remis dans le délai, la demande échoue et cesse d'écouter", async () => {
  vi.useFakeTimers();
  const canal = new CanalSimule();

  const jeton = demanderLeJeton(canal, 1_000);
  const verification = expect(jeton).rejects.toThrow("La fenêtre n'a pas remis le jeton");
  await vi.advanceTimersByTimeAsync(1_000);

  await verification;
  expect(canal.abonnes).toBe(0);
});

test("la page demande à l'hôte le choix du dossier des sauvegardes, sans chemin, et reçoit son issue", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeChoixDuDossier(canal);
  expect(canal.messagesRecus).toEqual([{ type: "choisirLeDossierDesSauvegardes" }]);
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "choisi" });

  await expect(issue).resolves.toBe("choisi");
  expect(canal.abonnes).toBe(0);
});

test("une réponse au choix du dossier qui n'a pas la forme attendue est ignorée", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeChoixDuDossier(canal);
  canal.envoyer({ type: "jeton", jeton: JETON_FICTIF });
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "C:\Users\Compte.Fictif" });
  canal.envoyer({ type: "dossierDesSauvegardes" });
  canal.envoyer("choisi");
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "refuse" });

  await expect(issue).resolves.toBe("refuse");
});

test("pendant un export ou une restauration, le choix du dossier reçoit « occupé » (#118)", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeChoixDuDossier(canal);
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "occupe" });

  await expect(issue).resolves.toBe("occupe");
  expect(canal.abonnes).toBe(0);
});

test("la page demande le brouillon d'une relance par son seul identifiant, et reçoit son issue (#14)", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeBrouillon(canal, 7);
  expect(canal.messagesRecus).toEqual([{ type: "ouvrirLeBrouillon", relance: 7 }]);
  canal.envoyer({ type: "brouillonDeRelance", relance: 7, issue: "ouvert" });

  await expect(issue).resolves.toBe("ouvert");
  expect(canal.abonnes).toBe(0);
});

test("une réponse au brouillon d'une autre forme, ou pour une autre relance, est ignorée", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeBrouillon(canal, 7);
  canal.envoyer({ type: "brouillonDeRelance", relance: 8, issue: "ouvert" });
  canal.envoyer({ type: "brouillonDeRelance", relance: "7", issue: "ouvert" });
  canal.envoyer({ type: "brouillonDeRelance", relance: 7, issue: "mailto:pirate@exemple.invalid" });
  canal.envoyer({ type: "brouillonDeRelance", relance: 7, issue: "sansReponse" });
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "choisi" });
  canal.envoyer("ouvert");
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "brouillonDeRelance", relance: 7, issue: "impossible" });

  await expect(issue).resolves.toBe("impossible");
});

test("sans réponse de l'hôte dans le délai, la page cesse d'attendre le brouillon", async () => {
  vi.useFakeTimers();
  const canal = new CanalSimule();

  const issue = demanderLeBrouillon(canal, 7, 1_000);
  await vi.advanceTimersByTimeAsync(1_000);

  await expect(issue).resolves.toBe("sansReponse");
  expect(canal.abonnes).toBe(0);
});

test("un identifiant qui n'est pas un entier positif ne part pas", async () => {
  const canal = new CanalSimule();

  await expect(demanderLeBrouillon(canal, 0)).rejects.toThrow(RangeError);
  await expect(demanderLeBrouillon(canal, 1.5)).rejects.toThrow(RangeError);
  await expect(demanderLeBrouillon(canal, Number.NaN)).rejects.toThrow(RangeError);
  expect(canal.messagesRecus).toEqual([]);
});

test("la page demande la restauration d'une copie par son seul identifiant, apprend qu'elle est en cours, puis reçoit son issue (#28)", async () => {
  const canal = new CanalSimule();
  const quandEnCours = vi.fn();

  const issue = demanderLaRestauration(canal, "0123456789abcdef", quandEnCours);
  expect(canal.messagesRecus).toEqual([{ type: "restaurerUneSauvegarde", copie: "0123456789abcdef" }]);
  canal.envoyer({ type: "restauration", copie: "0123456789abcdef", issue: "enCours" });
  expect(quandEnCours).toHaveBeenCalledOnce();
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "restauration", copie: "0123456789abcdef", issue: "restauree" });

  await expect(issue).resolves.toBe("restauree");
  expect(canal.abonnes).toBe(0);
});

test("une réponse à la restauration d'une autre forme, ou pour une autre copie, est ignorée", async () => {
  const canal = new CanalSimule();
  const quandEnCours = vi.fn();

  const issue = demanderLaRestauration(canal, "0123456789abcdef", quandEnCours);
  canal.envoyer({ type: "restauration", copie: "fedcba9876543210", issue: "restauree" });
  canal.envoyer({ type: "restauration", copie: "fedcba9876543210", issue: "enCours" });
  canal.envoyer({ type: "restauration", copie: "0123456789abcdef", issue: String.raw`C:\Users\Compte.Fictif\copie.db` });
  canal.envoyer({ type: "brouillonDeRelance", copie: "0123456789abcdef", issue: "restauree" });
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "choisi" });
  canal.envoyer("restauree");
  expect(quandEnCours).not.toHaveBeenCalled();
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "restauration", copie: "0123456789abcdef", issue: "abimee" });

  await expect(issue).resolves.toBe("abimee");
});

test("un identifiant de copie d'une autre forme ne part pas : jamais un chemin", async () => {
  const canal = new CanalSimule();

  for (const copie of ["0123456789ABCDEF", "0123456789abcde", "0123456789abcdef0", String.raw`C:\Sauvegardes\copie.db`, "../sollicito.db", ""]) {
    await expect(demanderLaRestauration(canal, copie)).rejects.toThrow(RangeError);
  }
  expect(canal.messagesRecus).toEqual([]);
});

test("l'export ne demande à l'hôte que son format, signale qu'il est en cours, puis rend son issue (#49)", async () => {
  const canal = new CanalSimule();
  const quandEnCours = vi.fn();

  const issue = demanderLExport(canal, "json", quandEnCours);
  expect(canal.messagesRecus).toEqual([{ type: "exporterLesDonnees", format: "json" }]);
  canal.envoyer({ type: "export", format: "json", issue: "enCours" });
  expect(quandEnCours).toHaveBeenCalledOnce();
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "export", format: "json", issue: "exporte" });

  await expect(issue).resolves.toBe("exporte");
  expect(canal.abonnes).toBe(0);
});

test("une réponse à l'export d'une autre forme, ou pour un autre format, est ignorée (#49)", async () => {
  const canal = new CanalSimule();
  const quandEnCours = vi.fn();

  const issue = demanderLExport(canal, "json", quandEnCours);
  canal.envoyer({ type: "export", format: "csv", issue: "exporte" });
  canal.envoyer({ type: "export", format: "csv", issue: "enCours" });
  canal.envoyer({ type: "export", format: "json", issue: String.raw`C:\Users\Compte.Fictif\export.json` });
  canal.envoyer({ type: "export", issue: "exporte" });
  canal.envoyer({ type: "restauration", format: "json", issue: "exporte" });
  canal.envoyer({ type: "dossierDesSauvegardes", issue: "choisi" });
  canal.envoyer("exporte");
  canal.envoyer(null);
  expect(quandEnCours).not.toHaveBeenCalled();
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "export", format: "json", issue: "placeManquante" });

  await expect(issue).resolves.toBe("placeManquante");
});

test("chaque issue d'un export que l'hôte rend est reçue telle quelle (#49)", async () => {
  for (const attendue of [
    "exporte",
    "annule",
    "occupe",
    "existe",
    "extension",
    "dossierDeDonnees",
    "accesRefuse",
    "placeManquante",
    "dossierIntrouvable",
    "impossible",
  ] as const) {
    const canal = new CanalSimule();
    const issue = demanderLExport(canal, "json");
    canal.envoyer({ type: "export", format: "json", issue: attendue });
    await expect(issue).resolves.toBe(attendue);
  }
});

test("l'export en CSV ne demande que son format, et n'écoute que la réponse de ce format (#49)", async () => {
  const canal = new CanalSimule();

  const issue = demanderLExport(canal, "csv");
  expect(canal.messagesRecus).toEqual([{ type: "exporterLesDonnees", format: "csv" }]);
  canal.envoyer({ type: "export", format: "json", issue: "exporte" });
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "export", format: "csv", issue: "exporte" });

  await expect(issue).resolves.toBe("exporte");
});
