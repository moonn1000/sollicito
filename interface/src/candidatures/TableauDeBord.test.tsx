import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { candidatureFictive, clientSimule } from "../tests/clientSimule.ts";
import { TableauDeBord } from "./TableauDeBord.tsx";

function lignes() {
  return within(screen.getByRole("table")).getAllByRole("row").slice(1);
}

test("le tableau de bord liste les candidatures dans l'ordre de l'API, avec entreprise, poste, date d'envoi et statut", async () => {
  const client = clientSimule({
    listerLesCandidatures: async () => [
      candidatureFictive({ id: 3, entreprise: { id: 2, nom: "Les Amis du Parc ASBL" }, poste: "Animateur", envoyeeLe: "2026-11-18T13:30:00Z" }),
      candidatureFictive({ id: 2, entreprise: { id: 1, nom: "Lumière Verte SA" }, poste: "Comptable", envoyeeLe: "2026-07-01T12:30:00Z" }),
      candidatureFictive({ id: 1, entreprise: { id: 3, nom: "L’Atelier d’Ondine" }, poste: "Couturière", envoyeeLe: "2026-03-27T22:30:00Z" }),
    ],
  });

  render(<TableauDeBord client={client} nouvelleCandidature={() => {}} />);

  expect(await screen.findByRole("table")).toBeDefined();
  expect(screen.getAllByRole("columnheader").map((entete) => entete.textContent)).toEqual([
    "Entreprise",
    "Poste",
    "Envoyée le",
    "Statut",
  ]);
  // Les instants UTC s'affichent à l'heure de Bruxelles, heure d'été comprise.
  expect(lignes().map((ligne) => within(ligne).getAllByRole("cell").map((cellule) => cellule.textContent))).toEqual([
    ["Les Amis du Parc ASBL", "Animateur", "18/11/2026 14:30", "Envoyée"],
    ["Lumière Verte SA", "Comptable", "01/07/2026 14:30", "Envoyée"],
    ["L’Atelier d’Ondine", "Couturière", "27/03/2026 23:30", "Envoyée"],
  ]);
});

test("les textes saisis s'affichent échappés, jamais comme du HTML", async () => {
  const client = clientSimule({
    listerLesCandidatures: async () => [
      candidatureFictive({
        entreprise: { id: 1, nom: '<img src="x" onerror="alert(1)"> & Fils' },
        poste: "<b>Développeuse</b><script>alert(2)</script>",
      }),
    ],
  });

  const { container } = render(<TableauDeBord client={client} nouvelleCandidature={() => {}} />);

  const [ligne] = (await screen.findAllByRole("row")).slice(1);
  expect(within(ligne!).getAllByRole("cell")[0]!.textContent).toBe('<img src="x" onerror="alert(1)"> & Fils');
  expect(within(ligne!).getAllByRole("cell")[1]!.textContent).toBe("<b>Développeuse</b><script>alert(2)</script>");
  expect(container.querySelector("img, b, script")).toBeNull();
});

test("sans candidature, le tableau de bord le dit et mène à la saisie", async () => {
  const nouvelleCandidature = vi.fn();

  render(<TableauDeBord client={clientSimule()} nouvelleCandidature={nouvelleCandidature} />);

  expect(await screen.findByText("Aucune candidature enregistrée pour l'instant.")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer une candidature" }));
  expect(nouvelleCandidature).toHaveBeenCalledOnce();
});

test("si la liste ne se charge pas, le tableau de bord le dit", async () => {
  const client = clientSimule({ listerLesCandidatures: () => Promise.reject(new Error("panne")) });

  render(<TableauDeBord client={client} nouvelleCandidature={() => {}} />);

  expect((await screen.findByRole("alert")).textContent).toBe(
    "Les candidatures n'ont pas pu être chargées. Fermez Sollicito, puis relancez-le.",
  );
});

test("l'annonce d'une candidature enregistrée s'affiche au-dessus de la liste", async () => {
  render(<TableauDeBord client={clientSimule()} annonce="Candidature enregistrée." nouvelleCandidature={() => {}} />);

  expect(screen.getByText("Candidature enregistrée.").getAttribute("role")).toBe("status");
  expect(await screen.findByText("Aucune candidature enregistrée pour l'instant.")).toBeDefined();
});
