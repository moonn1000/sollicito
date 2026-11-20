import { afterEach, expect, test, vi } from "vitest";
import { CanalSimule, JETON_FICTIF } from "../tests/canalSimule.ts";
import { demanderLeChoixDuDossier, demanderLeJeton } from "./canalDeLHote.ts";

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
