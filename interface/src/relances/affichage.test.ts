import { expect, test } from "vitest";
import { relanceFictive } from "../tests/clientSimule.ts";
import { afficherLeJour, decrireLeDestinataire, delaiDe, delaiPropose, echeancePour, jourDeLEnvoi, situerLEcheance } from "./affichage.ts";

test("un jour de l'API s'affiche en toutes lettres, sans qu'aucun fuseau ne le décale", () => {
  expect(afficherLeJour("2026-04-03")).toBe("vendredi 3 avril 2026");
  expect(afficherLeJour("2026-11-01")).toBe("dimanche 1 novembre 2026");
});

test("le jour de l'envoi est celui du fuseau du poste, et l'échéance compte des jours, jamais des heures", () => {
  // Le 27 mars 2026 à 23:30 à Bruxelles ; le passage à l'heure d'été tombe le 29.
  expect(jourDeLEnvoi("2026-03-27T22:30:00Z")).toBe("2026-03-27");
  expect(echeancePour("2026-03-27T22:30:00Z", 7)).toBe("2026-04-03");
  // Le 1er octobre 2026 à 00:30 à Bruxelles : encore le 30 septembre en UTC.
  expect(jourDeLEnvoi("2026-09-30T22:30:00Z")).toBe("2026-10-01");
  expect(delaiDe("2026-03-27T22:30:00Z", "2026-04-03")).toBe(7);
});

test("le délai proposé est celui par défaut, ou après une relance close, son délai plus celui par défaut", () => {
  const envoyeeLe = "2026-03-27T22:30:00Z";

  expect(delaiPropose({ envoyeeLe, relances: [] }, 7)).toBe(7);
  expect(delaiPropose({ envoyeeLe, relances: [relanceFictive({ etat: "Faite", echeance: "2026-04-03" })] }, 7)).toBe(14);
  expect(
    delaiPropose(
      {
        envoyeeLe,
        relances: [relanceFictive({ id: 1, etat: "Faite", echeance: "2026-04-03" }), relanceFictive({ id: 2, etat: "Abandonnee", echeance: "2026-04-10" })],
      },
      10,
    ),
  ).toBe(24);
  expect(delaiPropose({ envoyeeLe, relances: [relanceFictive({ etat: "Faite", echeance: "2027-03-20" })] }, 7)).toBe(365);
});

test("une échéance se situe par rapport à aujourd'hui", () => {
  expect(situerLEcheance("2026-11-15", "2026-11-18")).toBe("échue depuis 3 jours");
  expect(situerLEcheance("2026-11-17", "2026-11-18")).toBe("échue depuis hier");
  expect(situerLEcheance("2026-11-18", "2026-11-18")).toBe("échue aujourd'hui");
  expect(situerLEcheance("2026-11-19", "2026-11-18")).toBe("demain");
  expect(situerLEcheance("2026-11-25", "2026-11-18")).toBe("dans 7 jours");
});

test("un destinataire se décrit par son nom et son adresse, ou son absence", () => {
  expect(decrireLeDestinataire(null)).toBe("sans destinataire");
  expect(decrireLeDestinataire({ id: 1, nom: "Jeanne Martin", email: "jeanne@exemple.invalid" })).toBe("Jeanne Martin (jeanne@exemple.invalid)");
});
