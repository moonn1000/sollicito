import { expect, test } from "vitest";
import { changementFictif, ENTREPRISES_FICTIVES, etapeFictive } from "../tests/clientSimule.ts";
import { afficherLInstant, decrireLEvenement, deStatut, heureSaisieDe, proposerDesEntreprises } from "./affichage.ts";

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

test("un statut cité s'écrit avec sa préposition, élidée devant une voyelle", () => {
  expect(deStatut("Envoyee")).toBe("d'Envoyée");
  expect(deStatut("Entretien")).toBe("d'Entretien");
  expect(deStatut("Acceptee")).toBe("d'Acceptée");
  expect(deStatut("Proposition")).toBe("de Proposition");
  expect(deStatut("Refusee")).toBe("de Refusée");
  expect(deStatut("Retiree")).toBe("de Retirée");
  expect(deStatut("SansReponse")).toBe("de Sans réponse");
});

test("chaque événement de l'historique se dit en une phrase", () => {
  expect(decrireLEvenement(changementFictif())).toBe("Statut passé d'Envoyée à Entretien");
  expect(decrireLEvenement(changementFictif({ ancien: "Proposition", nouveau: "SansReponse" }))).toBe("Statut passé de Proposition à Sans réponse");
  expect(decrireLEvenement(changementFictif({ ancien: "Refusee", nouveau: "Entretien", corrige: 3 }))).toBe(
    "Correction : retour de Refusée à Entretien",
  );
  expect(decrireLEvenement(etapeFictive({ type: "Echange" }))).toBe("Échange (courriel, message)");
  expect(decrireLEvenement(etapeFictive({ type: "Entretien" }))).toBe("Entretien");
});

test("cinq entreprises au plus sont proposées", () => {
  const nombreuses = Array.from({ length: 8 }, (_, numero) => ({ id: numero, nom: `Atelier ${numero}` }));

  expect(proposerDesEntreprises(nombreuses, "atelier")).toHaveLength(5);
});
