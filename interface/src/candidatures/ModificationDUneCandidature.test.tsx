import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi } from "../api/clientDeLApi.ts";
import { changementFictif, clientSimule, etapeFictive, ficheFictive } from "../tests/clientSimule.ts";
import { ModificationDUneCandidature } from "./ModificationDUneCandidature.tsx";

const PREMIER_EVENEMENT = "La date et l'heure d'envoi ne peuvent pas suivre le premier événement de l'historique : l'historique commence à l'envoi.";

// Le 18/11/2026 à 14:30, heure de Bruxelles (le fuseau des tests).
const maintenant = () => new Date("2026-11-18T13:30:00Z");

function afficher(client = clientSimule()) {
  const revenir = vi.fn();
  const quandModifiee = vi.fn();
  render(<ModificationDUneCandidature client={client} candidature={7} revenir={revenir} quandModifiee={quandModifiee} maintenant={maintenant} />);
  return { client, revenir, quandModifiee };
}

function remplir(libelle: string, valeur: string) {
  fireEvent.change(screen.getByLabelText(libelle), { target: { value: valeur } });
}

// Les textes que désigne la description d'un champ (aria-describedby), dans l'ordre.
function description(libelle: string): string[] {
  const ids = screen.getByLabelText(libelle).getAttribute("aria-describedby")?.split(" ") ?? [];
  return ids.map((id) => document.getElementById(id)?.textContent ?? "");
}

test("l'écran est rempli d'avance : l'entreprise enregistrée, le poste, et la date d'envoi à la minute, dans le fuseau du poste", async () => {
  // Envoyée à 23:30:45, heure de Bruxelles : l'écran montre la minute.
  const fiche = ficheFictive({ envoyeeLe: "2026-03-27T22:30:45Z", statut: "Refusee", passagesPermis: [] });
  const { client } = afficher(clientSimule({ lireLaFiche: vi.fn(async () => fiche) }));

  expect(await screen.findByLabelText<HTMLInputElement>("Entreprise")).toBeDefined();
  expect(client.lireLaFiche).toHaveBeenCalledWith(7);
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Lumière Verte SA");
  expect(screen.getByLabelText<HTMLInputElement>("Poste").value).toBe("Comptable");
  expect(screen.getByLabelText<HTMLInputElement>("Date et heure d'envoi").value).toBe("2026-03-27T23:30");
  // Le statut ne se modifie pas ici, même clos.
  expect(screen.getByText(/^Statut :/u).textContent).toBe("Statut : Refusée. Il ne change pas ici : il avance depuis la fiche.");
  expect(screen.queryByRole("combobox")).toBeNull();
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { level: 2, name: "Modifier la candidature" })));
});

test("sans historique, rien sous la date ; avec un historique, l'écran rappelle la date de son premier événement", async () => {
  afficher();
  await screen.findByLabelText("Date et heure d'envoi");
  expect(description("Date et heure d'envoi")).toEqual([]);
});

test("l'aide sous la date nomme le premier événement, quelle que soit sa sorte et sa place dans la liste", async () => {
  const fiche = ficheFictive({
    historique: [changementFictif({ id: 1, survenuLe: "2026-11-18T13:30:00Z" }), etapeFictive({ id: 2, survenuLe: "2026-11-10T09:00:00Z" })],
  });
  afficher(clientSimule({ lireLaFiche: async () => fiche }));

  await screen.findByLabelText("Date et heure d'envoi");

  expect(description("Date et heure d'envoi")).toEqual(["Au plus tard le 10/11/2026 10:00, date du premier événement de l'historique."]);
});

test("la modification part telle quelle vers l'API, puis l'écran rend la fiche à jour", async () => {
  const apres = ficheFictive({ poste: "Comptable senior" });
  const { client, quandModifiee } = afficher(clientSimule({ modifierUneCandidature: vi.fn(async () => ({ modifiee: true as const, fiche: apres })) }));

  await screen.findByLabelText("Poste");
  remplir("Poste", " Comptable senior ");
  remplir("Date et heure d'envoi", "2026-03-26T09:15");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  await waitFor(() => expect(quandModifiee).toHaveBeenCalledExactlyOnceWith(apres));
  expect(client.modifierUneCandidature).toHaveBeenCalledExactlyOnceWith(7, {
    entreprise: "Lumière Verte SA",
    poste: " Comptable senior ",
    envoyeeLe: "2026-03-26T09:15",
  });
});

test("les refus s'affichent à côté du champ, après l'aide, et le premier champ refusé reçoit le focus", async () => {
  const fiche = ficheFictive({ historique: [etapeFictive({ survenuLe: "2026-11-10T09:00:00Z" })] });
  const { quandModifiee } = afficher(
    clientSimule({
      lireLaFiche: async () => fiche,
      modifierUneCandidature: async () => ({ modifiee: false, refus: { envoyeeLe: [PREMIER_EVENEMENT] } }),
    }),
  );

  await screen.findByLabelText("Date et heure d'envoi");
  remplir("Date et heure d'envoi", "2026-11-10T10:01");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  const envoi = screen.getByLabelText("Date et heure d'envoi");
  await waitFor(() => expect(envoi.getAttribute("aria-invalid")).toBe("true"));
  expect(description("Date et heure d'envoi")).toEqual(["Au plus tard le 10/11/2026 10:00, date du premier événement de l'historique.", PREMIER_EVENEMENT]);
  await waitFor(() => expect(document.activeElement).toBe(envoi));
  expect(quandModifiee).not.toHaveBeenCalled();
  // Modifier le champ efface son refus, et garde l'aide.
  remplir("Date et heure d'envoi", "2026-11-10T10:00");
  expect(envoi.getAttribute("aria-invalid")).toBeNull();
  expect(description("Date et heure d'envoi")).toEqual(["Au plus tard le 10/11/2026 10:00, date du premier événement de l'historique."]);
});

test("après un refus puis un échec de l'API, aucun refus ne reste, et l'échec se signale sans perdre la saisie", async () => {
  const modifierUneCandidature = vi
    .fn<ReturnType<typeof clientSimule>["modifierUneCandidature"]>()
    .mockResolvedValueOnce({ modifiee: false, refus: { poste: ["Indiquez le poste."] } })
    .mockRejectedValueOnce(new Error("panne"));
  afficher(clientSimule({ modifierUneCandidature }));

  await screen.findByLabelText("Poste");
  remplir("Poste", "");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));
  const poste = screen.getByLabelText("Poste");
  await waitFor(() => expect(poste.getAttribute("aria-invalid")).toBe("true"));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "La candidature n'a pas pu être modifiée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(poste.getAttribute("aria-invalid")).toBeNull();
  expect(screen.queryByText("Indiquez le poste.")).toBeNull();
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Lumière Verte SA");
});

test("« Annuler » ramène à la fiche sans rien envoyer", async () => {
  const { client, revenir } = afficher();

  await screen.findByLabelText("Poste");
  remplir("Poste", "Autre poste");
  fireEvent.click(screen.getByRole("button", { name: "Annuler" }));

  expect(revenir).toHaveBeenCalledOnce();
  expect(client.modifierUneCandidature).not.toHaveBeenCalled();
});

test("une candidature supprimée entre-temps le dit, à la place du formulaire", async () => {
  const { revenir, quandModifiee } = afficher(clientSimule({ modifierUneCandidature: () => Promise.reject(new ErreurDeLApi(404)) }));

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer les modifications" }));

  expect((await screen.findByRole("alert")).textContent).toBe("Cette candidature n'existe pas, ou plus.");
  expect(screen.queryByRole("button", { name: "Enregistrer les modifications" })).toBeNull();
  expect(quandModifiee).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Revenir à la fiche" }));
  expect(revenir).toHaveBeenCalledOnce();
});

test("une candidature introuvable au chargement le dit", async () => {
  afficher(clientSimule({ lireLaFiche: () => Promise.reject(new ErreurDeLApi(404)) }));
  expect((await screen.findByRole("alert")).textContent).toBe("Cette candidature n'existe pas, ou plus.");
});

test("une fiche qui ne se charge pas le dit", async () => {
  afficher(clientSimule({ lireLaFiche: () => Promise.reject(new ErreurDeLApi(500)) }));
  expect((await screen.findByRole("alert")).textContent).toBe("La candidature n'a pas pu être chargée. Fermez Sollicito, puis relancez-le.");
  expect(screen.queryByLabelText("Entreprise")).toBeNull();
});

test("pendant qu'on tape une autre entreprise, l'écran propose celles déjà enregistrées", async () => {
  afficher();

  await screen.findByLabelText("Entreprise");
  remplir("Entreprise", "brasserie");

  const propositions = await screen.findByRole("list", { name: "Entreprises déjà enregistrées :" });
  fireEvent.click(within(propositions).getByRole("button", { name: "Brasserie Lune Rousse S.R.L." }));
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Brasserie Lune Rousse S.R.L.");
});

test("un premier événement à venir ne borne pas l'envoi : l'écran n'en dit rien, l'envoi futur étant déjà refusé", async () => {
  const fiche = ficheFictive({ historique: [etapeFictive({ type: "Entretien", survenuLe: "2026-12-01T08:30:00Z" })] });
  afficher(clientSimule({ lireLaFiche: async () => fiche }));

  await screen.findByLabelText("Date et heure d'envoi");

  expect(description("Date et heure d'envoi")).toEqual([]);
});

test("deux envois rapprochés, avant le rendu suivant, ne font qu'une modification", async () => {
  const { client } = afficher(clientSimule({ modifierUneCandidature: vi.fn(() => new Promise<never>(() => {})) }));

  const formulaire = (await screen.findByRole("button", { name: "Enregistrer les modifications" })).closest("form")!;
  // Dans un même lot, sans rendu entre les deux : seule la garde lue aussitôt arrête le second.
  act(() => {
    formulaire.requestSubmit();
    formulaire.requestSubmit();
  });

  expect(client.modifierUneCandidature).toHaveBeenCalledOnce();
});

test("un événement corrigé ne borne plus l'envoi : l'aide nomme le premier événement en vigueur", async () => {
  const fiche = ficheFictive({
    historique: [
      etapeFictive({ id: 2, survenuLe: "2026-11-10T09:00:00Z", estCorrige: true }),
      changementFictif({ id: 3, survenuLe: "2026-11-12T09:00:00Z", estCorrige: true }),
      etapeFictive({ id: 4, survenuLe: "2026-11-14T09:00:00Z", corrige: 2 }),
      changementFictif({ id: 5, survenuLe: "2026-11-15T09:00:00Z", ancien: "Entretien", nouveau: "Envoyee", corrige: 3 }),
    ],
  });
  afficher(clientSimule({ lireLaFiche: async () => fiche }));

  await screen.findByLabelText("Date et heure d'envoi");

  expect(description("Date et heure d'envoi")).toEqual(["Au plus tard le 14/11/2026 10:00, date du premier événement de l'historique."]);
});
