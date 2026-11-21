import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { expect, test, vi } from "vitest";
import type { ContactConnu } from "../api/clientDeLApi.ts";
import { clientSimule, contactFictif } from "../tests/clientSimule.ts";
import { ContactsDeLEntreprise } from "./ContactsDeLEntreprise.tsx";

const ENTREPRISE = { id: 1, nom: "Lumière Verte SA" };

function Hote({ client, depart, quandChanges }: { client: ReturnType<typeof clientSimule>; depart: ContactConnu[]; quandChanges: () => void }) {
  const [contacts, setContacts] = useState<ContactConnu[]>(depart);
  return <ContactsDeLEntreprise client={client} entreprise={ENTREPRISE} contacts={contacts} remplacer={setContacts} quandChanges={quandChanges} />;
}

function rendre(depart: ContactConnu[] = [], client = clientSimule()) {
  const quandChanges = vi.fn();
  render(<Hote client={client} depart={depart} quandChanges={quandChanges} />);
  return { client, quandChanges };
}

test("sans contact, la section le dit ; un contact s'ajoute par son nom et son adresse", async () => {
  const { client } = rendre();
  expect(screen.getByText("Aucun contact pour cette entreprise.")).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Ajouter un contact" }));
  fireEvent.change(screen.getByLabelText("Nom"), { target: { value: "Paul Lambert" } });
  fireEvent.change(screen.getByLabelText("Adresse électronique"), { target: { value: "paul@exemple.invalid" } });
  fireEvent.click(screen.getByRole("button", { name: "Ajouter le contact" }));

  expect((await screen.findByRole("status")).textContent).toBe("Contact ajouté : Paul Lambert.");
  expect(client.ajouterUnContact).toHaveBeenCalledWith(1, { nom: "Paul Lambert", email: "paul@exemple.invalid" });
  expect(screen.getByRole("listitem").textContent).toContain("paul@exemple.invalid");
});

test("les refus de l'API s'affichent à côté de leur champ, sans rien ajouter", async () => {
  const client = clientSimule({
    ajouterUnContact: vi.fn(async () => ({
      issue: "refuse" as const,
      refus: { email: ["L'adresse électronique s'écrit nom@domaine.be : une seule adresse, sans espace ni accent, sans virgule, point-virgule, « ? » ni « % »."] },
    })),
  });
  rendre([], client);
  fireEvent.click(screen.getByRole("button", { name: "Ajouter un contact" }));
  fireEvent.change(screen.getByLabelText("Nom"), { target: { value: "Paul" } });
  fireEvent.change(screen.getByLabelText("Adresse électronique"), { target: { value: "paul@exemple.invalid,autre@exemple.invalid" } });
  fireEvent.click(screen.getByRole("button", { name: "Ajouter le contact" }));

  expect(await screen.findByText(/^L'adresse électronique s'écrit nom@domaine.be/u)).toBeDefined();
  expect(screen.getByLabelText("Adresse électronique").getAttribute("aria-invalid")).toBe("true");
  expect(document.activeElement).toBe(screen.getByLabelText("Adresse électronique"));
  expect(screen.queryByRole("listitem")).toBeNull();
});

test("un contact se corrige ; ses relances l'afficheront ainsi", async () => {
  const { client, quandChanges } = rendre([contactFictif()]);

  fireEvent.click(screen.getByRole("button", { name: "Modifier le contact : Jeanne Martin" }));
  expect((screen.getByLabelText("Nom") as HTMLInputElement).value).toBe("Jeanne Martin");
  fireEvent.change(screen.getByLabelText("Nom"), { target: { value: "Jeanne Dupont" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer le contact" }));

  expect((await screen.findByRole("status")).textContent).toBe("Contact modifié : Jeanne Dupont.");
  expect(client.modifierUnContact).toHaveBeenCalledWith(4, { nom: "Jeanne Dupont", email: "jeanne.martin@exemple.invalid" });
  expect(quandChanges).toHaveBeenCalledOnce();
});

test("un contact se supprime après une confirmation dans la page, jamais par une boîte du navigateur", async () => {
  const confirmer = vi.spyOn(window, "confirm");
  const { client, quandChanges } = rendre([contactFictif(), contactFictif({ id: 5, nom: "Paul Lambert", email: "paul@exemple.invalid" })]);

  fireEvent.click(screen.getByRole("button", { name: "Supprimer le contact : Jeanne Martin" }));
  const confirmation = screen.getByRole("group");
  expect(within(confirmation).getByText(/n'est plus le destinataire d'aucune relance, faites comprises/u)).toBeDefined();
  fireEvent.click(within(confirmation).getByRole("button", { name: "Annuler" }));
  expect(client.supprimerUnContact).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Supprimer le contact : Jeanne Martin" }));
  fireEvent.click(screen.getByRole("button", { name: "Supprimer définitivement" }));

  expect((await screen.findByRole("status")).textContent).toBe("Contact supprimé : Jeanne Martin.");
  expect(client.supprimerUnContact).toHaveBeenCalledWith(4);
  expect(screen.getAllByRole("listitem").map((contact) => contact.textContent)).toEqual([expect.stringContaining("Paul Lambert")]);
  expect(quandChanges).toHaveBeenCalledOnce();
  expect(confirmer).not.toHaveBeenCalled();
  confirmer.mockRestore();
});

test("Échap ferme la confirmation sans rien supprimer", () => {
  const { client } = rendre([contactFictif()]);

  fireEvent.click(screen.getByRole("button", { name: "Supprimer le contact : Jeanne Martin" }));
  fireEvent.keyDown(screen.getByRole("group"), { key: "Escape" });

  expect(screen.queryByRole("group")).toBeNull();
  expect(client.supprimerUnContact).not.toHaveBeenCalled();
});

test("le nom et l'adresse d'un contact s'affichent échappés, jamais comme du HTML", () => {
  const { container } = render(
    <ContactsDeLEntreprise
      client={clientSimule()}
      entreprise={{ id: 1, nom: "<b>Lumière</b>" }}
      contacts={[contactFictif({ nom: '<img src="x" onerror="alert(1)">', email: "<script>alert(2)</script>" })]}
      remplacer={() => {}}
      quandChanges={() => {}}
    />,
  );

  expect(screen.getByRole("heading").textContent).toBe("Contacts de <b>Lumière</b>");
  expect(screen.getByRole("listitem").textContent).toContain('<img src="x" onerror="alert(1)">');
  expect(container.querySelector("img, b, script")).toBeNull();
});

test("des contacts qui ne se lisent pas le disent", () => {
  render(<ContactsDeLEntreprise client={clientSimule()} entreprise={ENTREPRISE} contacts="echec" remplacer={() => {}} quandChanges={() => {}} />);

  expect(screen.getByRole("alert").textContent).toBe("Les contacts n'ont pas pu être chargés. Fermez Sollicito, puis relancez-le.");
  expect(screen.queryByRole("button", { name: "Ajouter un contact" })).toBeNull();
});
