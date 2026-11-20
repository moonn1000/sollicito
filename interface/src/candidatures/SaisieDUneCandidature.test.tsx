import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { candidatureFictive, clientSimule } from "../tests/clientSimule.ts";
import { SaisieDUneCandidature } from "./SaisieDUneCandidature.tsx";

// Le 18/11/2026 à 14:30, heure de Bruxelles (le fuseau des tests).
const maintenant = () => new Date("2026-11-18T13:30:00Z");

function remplir(libelle: string, valeur: string) {
  fireEvent.change(screen.getByLabelText(libelle), { target: { value: valeur } });
}

function afficher(client = clientSimule(), quandEnregistree = vi.fn()) {
  render(<SaisieDUneCandidature client={client} quandEnregistree={quandEnregistree} maintenant={maintenant} />);
  return { client, quandEnregistree };
}

test("l'écran de saisie demande l'entreprise, le poste, la date et l'heure d'envoi, et part du statut Envoyée", () => {
  afficher();

  expect(screen.getByRole("heading", { level: 2, name: "Nouvelle candidature" })).toBeDefined();
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("");
  expect(screen.getByLabelText<HTMLInputElement>("Poste").value).toBe("");
  // L'heure présente, à la minute, remplit d'avance l'envoi.
  expect(screen.getByLabelText<HTMLInputElement>("Date et heure d'envoi").value).toBe("2026-11-18T14:30");
  expect(screen.getByLabelText("Date et heure d'envoi").getAttribute("type")).toBe("datetime-local");
  expect(screen.getByText("Statut de départ :").textContent).toBe("Statut de départ : Envoyée");
});

test("la saisie part telle quelle vers l'API, qui la valide, puis l'écran rend la candidature enregistrée", async () => {
  const { client, quandEnregistree } = afficher();

  remplir("Entreprise", " Lumière Verte SA ");
  remplir("Poste", "Comptable");
  remplir("Date et heure d'envoi", "2026-03-27T23:30");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledOnce());
  expect(client.enregistrerUneCandidature).toHaveBeenCalledWith({
    entreprise: " Lumière Verte SA ",
    poste: "Comptable",
    envoyeeLe: "2026-03-27T23:30",
  });
  expect(quandEnregistree.mock.calls[0]![0]).toMatchObject({ poste: "Comptable" });
});

test("les refus de l'API s'affichent à côté du champ concerné, et le premier champ refusé reçoit le focus", async () => {
  const client = clientSimule({
    enregistrerUneCandidature: async () => ({
      enregistree: false,
      refus: {
        poste: ["Indiquez le poste."],
        envoyeeLe: ["La date et l'heure d'envoi ne peuvent pas être dans le futur."],
      },
    }),
  });
  const { quandEnregistree } = afficher(client);

  remplir("Entreprise", "Lumière Verte SA");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  const poste = screen.getByLabelText("Poste");
  await waitFor(() => expect(poste.getAttribute("aria-invalid")).toBe("true"));
  expect(document.getElementById(poste.getAttribute("aria-describedby")!)?.textContent).toBe("Indiquez le poste.");
  const envoi = screen.getByLabelText("Date et heure d'envoi");
  expect(document.getElementById(envoi.getAttribute("aria-describedby")!)?.textContent).toBe(
    "La date et l'heure d'envoi ne peuvent pas être dans le futur.",
  );
  // L'entreprise, admise, ne porte aucun refus.
  expect(screen.getByLabelText("Entreprise").getAttribute("aria-invalid")).toBeNull();
  // Le focus se déplace dans un effet, après l'affichage des refus : on l'attend.
  await waitFor(() => expect(document.activeElement).toBe(poste));
  expect(quandEnregistree).not.toHaveBeenCalled();
  // La saisie reste là, à corriger.
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Lumière Verte SA");
});

test("après un refus puis un échec de l'API, le champ corrigé ne garde ni son refus ni son message (#80, point 3)", async () => {
  const enregistrerUneCandidature = vi
    .fn<ReturnType<typeof clientSimule>["enregistrerUneCandidature"]>()
    .mockResolvedValueOnce({ enregistree: false, refus: { poste: ["Indiquez le poste."] } })
    .mockRejectedValueOnce(new Error("panne"));
  afficher(clientSimule({ enregistrerUneCandidature }));

  remplir("Entreprise", "Lumière Verte SA");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));
  const poste = screen.getByLabelText("Poste");
  await waitFor(() => expect(poste.getAttribute("aria-invalid")).toBe("true"));
  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  expect((await screen.findByRole("alert")).textContent).toContain("La candidature n'a pas pu être enregistrée.");
  expect(poste.getAttribute("aria-invalid")).toBeNull();
  expect(poste.getAttribute("aria-describedby")).toBeNull();
  expect(screen.queryByText("Indiquez le poste.")).toBeNull();
  expect(screen.getByLabelText<HTMLInputElement>("Poste").value).toBe("Comptable");
});

test("corriger un champ refusé efface son refus sans déplacer le focus vers un autre champ refusé", async () => {
  const client = clientSimule({
    enregistrerUneCandidature: async () => ({
      enregistree: false,
      refus: { poste: ["Indiquez le poste."], envoyeeLe: ["La date et l'heure d'envoi ne peuvent pas être dans le futur."] },
    }),
  });
  afficher(client);

  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));
  const poste = screen.getByLabelText("Poste");
  await waitFor(() => expect(document.activeElement).toBe(poste));
  remplir("Poste", "Comptable");

  expect(poste.getAttribute("aria-invalid")).toBeNull();
  expect(screen.queryByText("Indiquez le poste.")).toBeNull();
  // L'envoi, qu'on n'a pas touché, garde son refus ; le focus reste où l'on tape.
  expect(screen.getByLabelText("Date et heure d'envoi").getAttribute("aria-invalid")).toBe("true");
  await new Promise((resolve) => setTimeout(resolve, 20));
  expect(document.activeElement).toBe(poste);
});

test("pendant qu'on tape, l'écran propose les entreprises déjà enregistrées, sans tenir compte des accents", async () => {
  afficher();

  remplir("Entreprise", "lumiere");

  const propositions = await screen.findByRole("list", { name: "Entreprises déjà enregistrées :" });
  expect(within(propositions).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual(["Lumière Verte SA"]);
  fireEvent.click(within(propositions).getByRole("button", { name: "Lumière Verte SA" }));
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Lumière Verte SA");
  // Le nom complet choisi, il n'y a plus rien à proposer.
  expect(screen.queryByRole("list", { name: "Entreprises déjà enregistrées :" })).toBeNull();
});

test("les noms proposés s'affichent échappés, jamais comme du HTML", async () => {
  const client = clientSimule({ listerLesEntreprises: async () => [{ id: 1, nom: '<img src="x" onerror="alert(1)"> Atelier' }] });
  const { container } = render(<SaisieDUneCandidature client={client} quandEnregistree={() => {}} maintenant={maintenant} />);

  fireEvent.change(screen.getByLabelText("Entreprise"), { target: { value: "atelier" } });

  expect((await screen.findByRole("button", { name: '<img src="x" onerror="alert(1)"> Atelier' })).textContent).toBe(
    '<img src="x" onerror="alert(1)"> Atelier',
  );
  expect(container.querySelector("img")).toBeNull();
});

test("le bouton attend la réponse de l'API, et un échec se signale sans perdre la saisie", async () => {
  let repondre: (erreur: Error) => void = () => {};
  const enregistrerUneCandidature = vi.fn(() => new Promise<never>((_, rejeter) => (repondre = rejeter)));
  afficher(clientSimule({ enregistrerUneCandidature }));

  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  const bouton = await screen.findByRole("button", { name: "Enregistrement…" });
  expect(bouton.hasAttribute("disabled")).toBe(true);
  fireEvent.click(bouton);
  expect(enregistrerUneCandidature).toHaveBeenCalledOnce();

  repondre(new Error("panne"));
  expect((await screen.findByRole("alert")).textContent).toContain("La candidature n'a pas pu être enregistrée.");
  expect(screen.getByLabelText<HTMLInputElement>("Poste").value).toBe("Comptable");
  expect(screen.getByRole("button", { name: "Enregistrer la candidature" }).hasAttribute("disabled")).toBe(false);
});

test("sans les entreprises enregistrées, la saisie reste possible", async () => {
  const client = clientSimule({ listerLesEntreprises: () => Promise.reject(new Error("panne")) });
  const { quandEnregistree } = afficher(client);

  remplir("Entreprise", "Lumière");
  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledWith(expect.objectContaining(candidatureFictive({ entreprise: { id: 9, nom: "Lumière" } }))));
  expect(screen.queryByRole("list", { name: "Entreprises déjà enregistrées :" })).toBeNull();
});
