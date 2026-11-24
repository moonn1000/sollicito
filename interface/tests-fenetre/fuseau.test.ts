import { expect, test } from "vitest";

// Seule la CI règle son fuseau (ci.yml, #80 point 2) : sur un poste, c'est celui de l'utilisateur, UTC compris. Sans cette
// garde, retirer l'étape de ci.yml laisserait la vraie fenêtre lire et afficher l'heure dans le même fuseau, UTC, où
// aucun de ses tests ne verrait une API qui lirait l'heure saisie en UTC.
test.skipIf(process.env.CI !== "true")("sur la CI, le fuseau du poste n'est pas UTC", () => {
  expect(new Date(2026, 10, 10, 14, 0).getTimezoneOffset()).not.toBe(0);
});
