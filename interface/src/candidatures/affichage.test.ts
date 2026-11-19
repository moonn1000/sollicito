import { expect, test } from "vitest";
import { ENTREPRISES_FICTIVES } from "../tests/clientSimule.ts";
import { afficherLInstant, heureSaisieDe, proposerDesEntreprises } from "./affichage.ts";

test("un instant UTC s'affiche à l'heure du poste, avec le décalage en vigueur à cet instant", () => {
  expect(afficherLInstant("2026-03-27T22:30:00Z")).toBe("27/03/2026 23:30");
  expect(afficherLInstant("2026-07-01T12:30:00Z")).toBe("01/07/2026 14:30");
  expect(afficherLInstant("2026-10-25T01:30:00Z")).toBe("25/10/2026 02:30");
});

test("l'heure présente se saisit à la minute, sans décalage", () => {
  expect(heureSaisieDe(new Date("2026-11-18T13:30:59Z"))).toBe("2026-11-18T14:30");
  expect(heureSaisieDe(new Date("2026-07-01T07:05:00Z"))).toBe("2026-07-01T09:05");
});

test("les entreprises proposées contiennent le texte saisi, sans tenir compte des accents ni des majuscules", () => {
  const noms = (saisie: string) => proposerDesEntreprises(ENTREPRISES_FICTIVES, saisie).map((entreprise) => entreprise.nom);

  expect(noms("LUMIERE")).toEqual(["Lumière Verte SA"]);
  expect(noms("  lune   rousse ")).toEqual(["Brasserie Lune Rousse S.R.L."]);
  expect(noms("a")).toEqual(["Lumière Verte SA", "Brasserie Lune Rousse S.R.L.", "Les Amis du Parc ASBL"]);
  expect(noms("")).toEqual([]);
  expect(noms("   ")).toEqual([]);
  expect(noms("Inconnue")).toEqual([]);
  // Le nom exact déjà saisi n'est plus proposé.
  expect(noms("Lumière Verte SA")).toEqual([]);
});

test("cinq entreprises au plus sont proposées", () => {
  const nombreuses = Array.from({ length: 8 }, (_, numero) => ({ id: numero, nom: `Atelier ${numero}` }));

  expect(proposerDesEntreprises(nombreuses, "atelier")).toHaveLength(5);
});
