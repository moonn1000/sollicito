import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { clientSimule, contactFictif, relanceSignaleeFictive } from "../tests/clientSimule.ts";
import { MESSAGES_DU_BROUILLON } from "./affichage.ts";
import { RelancesDuTableauDeBord } from "./RelancesDuTableauDeBord.tsx";

const RELANCES = {
  aujourdhui: "2026-11-18",
  echues: [
    relanceSignaleeFictive({ id: 7, echeance: "2026-11-15", destinataire: contactFictif() }),
    relanceSignaleeFictive({ id: 8, echeance: "2026-11-18", candidature: { id: 2, poste: "Brasseuse", entreprise: { id: 2, nom: "Brasserie Lune Rousse" } } }),
  ],
  deLaSemaine: [relanceSignaleeFictive({ id: 9, echeance: "2026-11-19", candidature: { id: 3, poste: "Animateur", entreprise: { id: 3, nom: "Les Amis du Parc ASBL" } } })],
};

function section(titre: string) {
  return screen.getByRole("heading", { name: new RegExp(`^${titre}`, "u") }).parentElement!;
}

test("le tableau de bord signale les relances échues et celles de la semaine, avec leur échéance et leur destinataire", async () => {
  render(<RelancesDuTableauDeBord client={clientSimule({ listerLesRelances: async () => RELANCES })} ouvrirLaFiche={() => {}} />);

  expect(await screen.findByRole("heading", { name: "Échues (2)" })).toBeDefined();
  const echues = within(section("Échues")).getAllByRole("listitem");
  expect(echues.map((relance) => relance.textContent)).toEqual([
    "Comptable, chez Lumière Verte SAdimanche 15 novembre 2026 (échue depuis 3 jours), Jeanne Martin (jeanne.martin@exemple.invalid)",
    "Brasseuse, chez Brasserie Lune Roussemercredi 18 novembre 2026 (échue aujourd'hui), sans destinataire",
  ]);
  expect(within(section("Cette semaine")).getByRole("listitem").textContent).toBe(
    "Animateur, chez Les Amis du Parc ASBLjeudi 19 novembre 2026 (demain), sans destinataire",
  );
  // Hors de la fenêtre, aucun bouton de brouillon.
  expect(screen.queryByRole("button", { name: /Préparer le brouillon/u })).toBeNull();
});

test("sans relance à signaler, le tableau de bord le dit ; une liste qui ne se lit pas aussi", async () => {
  const { unmount } = render(<RelancesDuTableauDeBord client={clientSimule()} ouvrirLaFiche={() => {}} />);
  expect(await screen.findByText("Aucune relance échue, ni prévue dans les sept jours.")).toBeDefined();
  unmount();

  render(
    <RelancesDuTableauDeBord
      client={clientSimule({
        listerLesRelances: async () => {
          throw new Error("hors service");
        },
      })}
      ouvrirLaFiche={() => {}}
    />,
  );
  expect((await screen.findByRole("alert")).textContent).toBe("Les relances n'ont pas pu être chargées. Fermez Sollicito, puis relancez-le.");
});

test("le poste d'une relance ouvre la fiche de sa candidature", async () => {
  const ouvrirLaFiche = vi.fn();
  render(<RelancesDuTableauDeBord client={clientSimule({ listerLesRelances: async () => RELANCES })} ouvrirLaFiche={ouvrirLaFiche} />);

  fireEvent.click(await screen.findByRole("button", { name: "Brasseuse, chez Brasserie Lune Rousse : ouvrir la fiche" }));

  expect(ouvrirLaFiche).toHaveBeenCalledWith(2);
});

test("« Préparer le brouillon » ne donne à la fenêtre que l'identifiant de la relance, puis dit l'issue", async () => {
  const ouvrirLeBrouillon = vi.fn<(relance: number) => Promise<IssueDuBrouillon>>(async () => "ouvert");
  render(<RelancesDuTableauDeBord client={clientSimule({ listerLesRelances: async () => RELANCES })} ouvrirLaFiche={() => {}} ouvrirLeBrouillon={ouvrirLeBrouillon} />);

  fireEvent.click(await screen.findByRole("button", { name: "Préparer le brouillon de la relance : Comptable, chez Lumière Verte SA" }));

  expect((await screen.findByRole("status")).textContent).toBe(MESSAGES_DU_BROUILLON.ouvert);
  expect(ouvrirLeBrouillon.mock.calls).toEqual([[7]]);
});

test.each<IssueDuBrouillon>(["impossible", "sansReponse"])("un brouillon qui ne s'ouvre pas le dit (%s)", async (issue) => {
  render(
    <RelancesDuTableauDeBord
      client={clientSimule({ listerLesRelances: async () => RELANCES })}
      ouvrirLaFiche={() => {}}
      ouvrirLeBrouillon={async () => issue}
    />,
  );

  fireEvent.click(await screen.findByRole("button", { name: "Préparer le brouillon de la relance : Animateur, chez Les Amis du Parc ASBL" }));

  expect((await screen.findByRole("alert")).textContent).toBe(MESSAGES_DU_BROUILLON[issue]);
});

test("un brouillon refusé fait relire les relances : celle-ci n'était plus à faire", async () => {
  const listerLesRelances = vi.fn(async () => RELANCES);
  render(<RelancesDuTableauDeBord client={clientSimule({ listerLesRelances })} ouvrirLaFiche={() => {}} ouvrirLeBrouillon={async () => "refuse"} />);

  fireEvent.click(await screen.findByRole("button", { name: "Préparer le brouillon de la relance : Comptable, chez Lumière Verte SA" }));

  expect((await screen.findByRole("alert")).textContent).toBe(MESSAGES_DU_BROUILLON.refuse);
  await vi.waitFor(() => expect(listerLesRelances).toHaveBeenCalledTimes(2));
});

test("les textes d'une relance s'affichent échappés, jamais comme du HTML", async () => {
  const { container } = render(
    <RelancesDuTableauDeBord
      client={clientSimule({
        listerLesRelances: async () => ({
          aujourdhui: "2026-11-18",
          echues: [
            relanceSignaleeFictive({
              candidature: { id: 1, poste: "<b>Comptable</b>", entreprise: { id: 1, nom: '<img src="x" onerror="alert(1)">' } },
              destinataire: contactFictif({ nom: "<script>alert(2)</script>" }),
            }),
          ],
          deLaSemaine: [],
        }),
      })}
      ouvrirLaFiche={() => {}}
    />,
  );

  expect((await screen.findByRole("button", { name: /ouvrir la fiche/u })).textContent).toBe("<b>Comptable</b>");
  expect(container.textContent).toContain("<script>alert(2)</script>");
  expect(container.querySelector("img, b, script")).toBeNull();
});
