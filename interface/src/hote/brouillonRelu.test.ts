import { afterEach, expect, test, vi } from "vitest";
import { CanalSimule } from "../tests/canalSimule.ts";
import { demanderLeBrouillon, demanderLeBrouillonRelu } from "./canalDeLHote.ts";

// Le brouillon relu d'un premier jet (#47) : un message neuf, qui ne porte que l'identifiant de la relance, et sa propre
// réponse, qui ne se mêle pas à celle du brouillon de #14.

afterEach(() => {
  vi.useRealTimers();
});

test("la page demande le brouillon relu par le seul identifiant de la relance, puis reçoit l'issue", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeBrouillonRelu(canal, 7);
  expect(canal.messagesRecus).toEqual([{ type: "ouvrirLeBrouillonRelu", relance: 7 }]);
  canal.envoyer({ type: "brouillonRelu", relance: 7, issue: "ouvert" });

  await expect(issue).resolves.toBe("ouvert");
  expect(canal.abonnes).toBe(0);
});

test("la réponse du brouillon de #14 n'est pas celle du brouillon relu, ni l'inverse", async () => {
  const canal = new CanalSimule();

  const relu = demanderLeBrouillonRelu(canal, 7);
  const gabarit = demanderLeBrouillon(canal, 7);
  canal.envoyer({ type: "brouillonDeRelance", relance: 7, issue: "impossible" });
  canal.envoyer({ type: "brouillonRelu", relance: 7, issue: "ouvert" });

  await expect(relu).resolves.toBe("ouvert");
  await expect(gabarit).resolves.toBe("impossible");
  expect(canal.messagesRecus).toEqual([
    { type: "ouvrirLeBrouillonRelu", relance: 7 },
    { type: "ouvrirLeBrouillon", relance: 7 },
  ]);
});

test("une réponse d'une autre forme, ou pour une autre relance, est ignorée", async () => {
  const canal = new CanalSimule();

  const issue = demanderLeBrouillonRelu(canal, 7);
  canal.envoyer({ type: "brouillonRelu", relance: 8, issue: "ouvert" });
  canal.envoyer({ type: "brouillonRelu", relance: "7", issue: "ouvert" });
  canal.envoyer({ type: "brouillonRelu", relance: 7, issue: "texte" });
  canal.envoyer({ type: "brouillonRelu", relance: 7, issue: "sansReponse" });
  expect(canal.abonnes).toBe(1);
  canal.envoyer({ type: "brouillonRelu", relance: 7, issue: "refuse" });

  await expect(issue).resolves.toBe("refuse");
});

test("sans réponse de l'hôte dans le délai, la page cesse d'attendre", async () => {
  vi.useFakeTimers();
  const canal = new CanalSimule();

  const issue = demanderLeBrouillonRelu(canal, 7, 1_000);
  await vi.advanceTimersByTimeAsync(1_000);

  await expect(issue).resolves.toBe("sansReponse");
  expect(canal.abonnes).toBe(0);
});

test("un identifiant qui n'est pas un entier positif ne part pas", async () => {
  const canal = new CanalSimule();

  await expect(demanderLeBrouillonRelu(canal, 0)).rejects.toBeInstanceOf(RangeError);
  await expect(demanderLeBrouillonRelu(canal, 1.5)).rejects.toBeInstanceOf(RangeError);
  expect(canal.messagesRecus).toEqual([]);
});
