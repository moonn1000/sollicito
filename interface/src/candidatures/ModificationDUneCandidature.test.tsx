import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi, type FicheDUneCandidature } from "../api/clientDeLApi.ts";
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

test("sans historique, rien sous la date (le rappel du premier événement est gardé par les tests suivants)", async () => {
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
  const { client, quandModifiee } = afficher(clientSimule({ modifierUneCandidature: vi.fn(async () => ({ issue: "modifiee" as const, fiche: apres })) }));

  await screen.findByLabelText("Poste");
  remplir("Poste", " Comptable senior ");
  remplir("Date et heure d'envoi", "2026-03-26T09:15");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  await waitFor(() => expect(quandModifiee).toHaveBeenCalledExactlyOnceWith(apres));
  expect(client.modifierUneCandidature).toHaveBeenCalledExactlyOnceWith(
    7,
    { entreprise: "Lumière Verte SA", poste: " Comptable senior ", envoyeeLe: "2026-03-26T09:15" },
    false,
  );
});

test("les refus s'affichent à côté du champ, après l'aide, et le premier champ refusé reçoit le focus", async () => {
  const fiche = ficheFictive({ historique: [etapeFictive({ survenuLe: "2026-11-10T09:00:00Z" })] });
  const { quandModifiee } = afficher(
    clientSimule({
      lireLaFiche: async () => fiche,
      modifierUneCandidature: async () => ({ issue: "refusee", refus: { envoyeeLe: [PREMIER_EVENEMENT] } }),
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
    .mockResolvedValueOnce({ issue: "refusee", refus: { poste: ["Indiquez le poste."] } })
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

test("un premier événement à venir se rappelle aussi, avec ce qui borne alors l'envoi : l'instant présent", async () => {
  const fiche = ficheFictive({ historique: [etapeFictive({ type: "Entretien", survenuLe: "2026-12-01T08:30:00Z" })] });
  afficher(clientSimule({ lireLaFiche: async () => fiche }));

  await screen.findByLabelText("Date et heure d'envoi");

  expect(description("Date et heure d'envoi")).toEqual([
    "Le premier événement de l'historique, le 01/12/2026 09:30, est à venir : l'envoi ne peut pas être dans le futur.",
  ]);
});

test("pendant l'envoi de la modification, « Annuler » attend la réponse : elle ne s'enregistre pas après qu'on a annulé (#85, point 6)", async () => {
  let repondre: () => void = () => {};
  const { revenir, quandModifiee } = afficher(
    clientSimule({
      modifierUneCandidature: vi.fn(
        () => new Promise<{ issue: "modifiee"; fiche: FicheDUneCandidature }>((resoudre) => (repondre = () => resoudre({ issue: "modifiee", fiche: ficheFictive() }))),
      ),
    }),
  );

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer les modifications" }));

  const annuler = screen.getByRole("button", { name: "Annuler" });
  await waitFor(() => expect(annuler.hasAttribute("disabled")).toBe(true));
  fireEvent.click(annuler);
  expect(revenir).not.toHaveBeenCalled();

  repondre();
  await waitFor(() => expect(quandModifiee).toHaveBeenCalledOnce());
  expect(revenir).not.toHaveBeenCalled();
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

// L'avertissement d'un nom qui corrigerait celui d'une entreprise enregistrée.
function avertissement() {
  return screen.queryByRole("region", { name: "Corriger le nom de l'entreprise ?" });
}

test("un nom de même clé, écrit autrement, attend l'accord : l'écran dit le nom corrigé et le nombre de candidatures", async () => {
  const apres = ficheFictive({ entreprise: { id: 1, nom: "LUMIÈRE VERTE S.A." } });
  const modifierUneCandidature = vi
    .fn<ReturnType<typeof clientSimule>["modifierUneCandidature"]>()
    .mockResolvedValueOnce({ issue: "nomACorriger", entreprise: { id: 1, nom: "Lumière Verte SA" }, candidatures: 3, offres: 0 })
    .mockResolvedValueOnce({ issue: "modifiee", fiche: apres });
  const { quandModifiee } = afficher(clientSimule({ modifierUneCandidature }));

  await screen.findByLabelText("Entreprise");
  remplir("Entreprise", " LUMIÈRE VERTE S.A. ");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  const question = await screen.findByRole("region", { name: "Corriger le nom de l'entreprise ?" });
  // Même entreprise : le texte ne parle pas d'un changement d'entreprise ; le nom saisi vient de l'écran.
  expect(within(question).getByText(/^Ce nom est celui/u).textContent).toBe(
    "Ce nom est celui de l'entreprise de la candidature, écrit autrement. Son nom affiché « Lumière Verte SA » deviendrait « LUMIÈRE VERTE S.A. », pour ses 3 candidatures, celle-ci comprise.",
  );
  await waitFor(() => expect(document.activeElement).toBe(within(question).getByRole("heading", { level: 3 })));
  expect(quandModifiee).not.toHaveBeenCalled();

  fireEvent.click(within(question).getByRole("button", { name: "Corriger le nom pour les 3 candidatures" }));

  await waitFor(() => expect(quandModifiee).toHaveBeenCalledExactlyOnceWith(apres));
  expect(modifierUneCandidature).toHaveBeenCalledTimes(2);
  expect(modifierUneCandidature.mock.calls[0]![2]).toBe(false);
  expect(modifierUneCandidature.mock.calls[1]).toEqual([7, { entreprise: " LUMIÈRE VERTE S.A. ", poste: "Comptable", envoyeeLe: "2026-03-27T23:30" }, true]);
});

test("un nom qui désigne une autre entreprise le dit, et « Garder » rattache la candidature sous le nom enregistré", async () => {
  const modifierUneCandidature = vi
    .fn<ReturnType<typeof clientSimule>["modifierUneCandidature"]>()
    .mockResolvedValueOnce({ issue: "nomACorriger", entreprise: { id: 4, nom: "Brasserie Lune Rousse" }, candidatures: 2, offres: 0 })
    .mockResolvedValueOnce({ issue: "modifiee", fiche: ficheFictive({ entreprise: { id: 4, nom: "Brasserie Lune Rousse" } }) });
  const { quandModifiee } = afficher(clientSimule({ modifierUneCandidature }));

  await screen.findByLabelText("Entreprise");
  remplir("Entreprise", "BRASSERIE LUNE ROUSSE");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  const question = await screen.findByRole("region", { name: "Corriger le nom de l'entreprise ?" });
  expect(within(question).getByText(/^La candidature quitterait/u).textContent).toBe(
    "La candidature quitterait « Lumière Verte SA » pour rejoindre l'entreprise enregistrée « Brasserie Lune Rousse », que ce nom désigne. Son nom affiché « Brasserie Lune Rousse » deviendrait « BRASSERIE LUNE ROUSSE », pour ses 2 candidatures, celle-ci comprise.",
  );

  fireEvent.click(within(question).getByRole("button", { name: "Garder « Brasserie Lune Rousse »" }));

  await waitFor(() => expect(quandModifiee).toHaveBeenCalledOnce());
  expect(modifierUneCandidature.mock.calls[1]).toEqual([7, { entreprise: "Brasserie Lune Rousse", poste: "Comptable", envoyeeLe: "2026-03-27T23:30" }, false]);
});

test.each([
  [2, "pour ses 3 candidatures, celle-ci comprise, et pour les 2 offres qui la désignent."],
  [1, "pour ses 3 candidatures, celle-ci comprise, et pour l'offre qui la désigne."],
])("l'avertissement compte aussi les offres qui désignent l'entreprise (#38, suites de #36) : %i", async (offres, portee) => {
  afficher(
    clientSimule({
      modifierUneCandidature: async () => ({ issue: "nomACorriger", entreprise: { id: 1, nom: "Lumière Verte SA" }, candidatures: 3, offres }),
    }),
  );

  await screen.findByLabelText("Entreprise");
  remplir("Entreprise", "LUMIÈRE VERTE S.A.");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  const question = await screen.findByRole("region", { name: "Corriger le nom de l'entreprise ?" });
  expect(within(question).getByText(/^Ce nom est celui/u).textContent).toBe(
    `Ce nom est celui de l'entreprise de la candidature, écrit autrement. Son nom affiché « Lumière Verte SA » deviendrait « LUMIÈRE VERTE S.A. », ${portee}`,
  );
});

test("pour une seule candidature, l'accord se dit au singulier ; modifier un champ retire l'avertissement", async () => {
  afficher(
    clientSimule({
      modifierUneCandidature: async () => ({ issue: "nomACorriger", entreprise: { id: 1, nom: "Lumière Verte SA" }, candidatures: 1, offres: 0 }),
    }),
  );

  await screen.findByLabelText("Entreprise");
  remplir("Entreprise", "Lumiere Verte");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  const question = await screen.findByRole("region", { name: "Corriger le nom de l'entreprise ?" });
  expect(within(question).getByRole("button", { name: "Corriger le nom" })).toBeDefined();
  expect(within(question).getByText(/pour cette seule candidature\.$/u)).toBeDefined();
  remplir("Poste", "Comptable senior");
  expect(avertissement()).toBeNull();
});

test("un accord donné, puis un champ modifié, ne vaut plus : l'envoi suivant repart sans accord", async () => {
  let repondre: () => void = () => {};
  const modifierUneCandidature = vi
    .fn<ReturnType<typeof clientSimule>["modifierUneCandidature"]>()
    .mockResolvedValueOnce({ issue: "nomACorriger", entreprise: { id: 1, nom: "Lumière Verte SA" }, candidatures: 2, offres: 0 })
    .mockImplementationOnce(() => new Promise((resoudre) => (repondre = () => resoudre({ issue: "refusee", refus: { envoyeeLe: [PREMIER_EVENEMENT] } }))))
    .mockResolvedValueOnce({ issue: "nomACorriger", entreprise: { id: 1, nom: "Lumière Verte SA" }, candidatures: 2, offres: 0 });
  afficher(clientSimule({ modifierUneCandidature }));

  await screen.findByLabelText("Entreprise");
  remplir("Entreprise", "LUMIÈRE VERTE S.A.");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));
  const corriger = within(await screen.findByRole("region", { name: "Corriger le nom de l'entreprise ?" })).getByRole("button", {
    name: "Corriger le nom pour les 2 candidatures",
  });
  // Deux clics : le second, arrêté par l'envoi en cours, laisserait un accord en attente.
  act(() => {
    corriger.click();
    corriger.click();
  });
  repondre();
  await waitFor(() => expect(screen.getByLabelText("Date et heure d'envoi").getAttribute("aria-invalid")).toBe("true"));
  remplir("Entreprise", "Lumiere Verte");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  await waitFor(() => expect(modifierUneCandidature).toHaveBeenCalledTimes(3));
  expect(modifierUneCandidature.mock.calls.map((appel) => appel[2])).toEqual([false, true, false]);
});
