// Les champs de texte courts et les notes s'arrêtent à 10 000 caractères (#98, point 3) : bien au-delà de toute limite de
// l'API, si bien qu'un texte collé trop long garde son refus lisible, et assez bas pour qu'aucune saisie n'atteigne la
// borne des corps, 256 Kio (BornesDesCorpsTests). Cette borne n'arrête que la frappe et le collage : HTML ne l'applique
// pas à une valeur posée par le code, et jsdom non plus. Les derniers tests montrent que les formulaires préremplis ne
// coupent rien à l'envoi.
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { FicheDUneCandidature } from "../candidatures/FicheDUneCandidature.tsx";
import { ModificationDUneCandidature } from "../candidatures/ModificationDUneCandidature.tsx";
import { NoterUneEtape } from "../candidatures/NoterUneEtape.tsx";
import { SaisieDUneCandidature } from "../candidatures/SaisieDUneCandidature.tsx";
import { ModificationDUneOffre } from "../offres/ModificationDUneOffre.tsx";
import { SaisieDUneOffre } from "../offres/SaisieDUneOffre.tsx";
import { ContactsDeLEntreprise } from "../relances/ContactsDeLEntreprise.tsx";
import { clientSimule, contactFictif, etapeFictive, ficheFictive, offreFictive } from "../tests/clientSimule.ts";

// Une valeur existante d'un caractère de plus que la borne.
const LONGUE = "a".repeat(10_001);

const longueurDe = (libelle: string) => screen.getByLabelText(libelle).getAttribute("maxlength");

test("les champs de texte et la note s'arrêtent à 10 000 caractères ; le texte d'une offre n'a pas de borne", () => {
  render(<SaisieDUneCandidature client={clientSimule()} quandEnregistree={vi.fn()} />);
  expect(["Entreprise", "Poste"].map(longueurDe)).toEqual(["10000", "10000"]);
  cleanup();

  render(<NoterUneEtape client={clientSimule()} candidature={7} quandNotee={vi.fn()} />);
  expect(longueurDe("Note (facultative)")).toBe("10000");
  cleanup();

  render(<ContactsDeLEntreprise client={clientSimule()} entreprise={{ id: 1, nom: "Lumière Verte SA" }} contacts={[]} remplacer={vi.fn()} quandChanges={vi.fn()} />);
  fireEvent.click(screen.getByRole("button", { name: "Ajouter un contact" }));
  expect(["Nom", "Adresse électronique"].map(longueurDe)).toEqual(["10000", "10000"]);
  cleanup();

  render(<SaisieDUneOffre client={clientSimule()} quandEnregistree={vi.fn()} renoncer={vi.fn()} />);
  expect(["Entreprise", "Poste", "Lieu", "Adresse de l'annonce"].map(longueurDe)).toEqual(["10000", "10000", "10000", "10000"]);
  expect(longueurDe("Texte de l'offre")).toBeNull();
});

test("la correction d'une offre renvoie entières des valeurs plus longues que la borne", async () => {
  const adresse = `https://emploi.exemple.invalid/${LONGUE}`;
  const client = clientSimule({ lireUneOffre: async () => offreFictive({ entreprise: { id: 1, nom: LONGUE }, poste: LONGUE, lieu: LONGUE, adresse }) });
  render(<ModificationDUneOffre client={client} offre={3} revenir={vi.fn()} quandModifiee={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer les modifications" }));

  await waitFor(() => expect(client.corrigerUneOffre).toHaveBeenCalledOnce());
  expect(client.corrigerUneOffre).toHaveBeenCalledWith(3, { entreprise: LONGUE, poste: LONGUE, lieu: LONGUE, publieeLe: "2026-11-17", adresse }, false);
});

test("la modification d'une candidature renvoie entières des valeurs plus longues que la borne", async () => {
  const client = clientSimule({ lireLaFiche: async () => ficheFictive({ entreprise: { id: 1, nom: LONGUE }, poste: LONGUE }) });
  render(<ModificationDUneCandidature client={client} candidature={7} revenir={vi.fn()} quandModifiee={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer les modifications" }));

  await waitFor(() => expect(client.modifierUneCandidature).toHaveBeenCalledOnce());
  expect(client.modifierUneCandidature).toHaveBeenCalledWith(7, { entreprise: LONGUE, poste: LONGUE, envoyeeLe: expect.any(String) }, false);
});

test("la correction d'une étape renvoie entière une note plus longue que la borne", async () => {
  const client = clientSimule({ lireLaFiche: async () => ficheFictive({ historique: [etapeFictive({ id: 2, note: LONGUE })] }) });
  render(<FicheDUneCandidature client={client} candidature={7} revenir={vi.fn()} modifier={vi.fn()} quandSupprimee={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: /^Corriger l'étape/u }));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la correction" }));

  await waitFor(() => expect(client.corrigerUneEtape).toHaveBeenCalledOnce());
  expect(client.corrigerUneEtape).toHaveBeenCalledWith(7, 2, expect.objectContaining({ note: LONGUE }));
});

test("la correction d'un contact renvoie entiers un nom et une adresse plus longs que la borne", async () => {
  const email = `${LONGUE}@exemple.invalid`;
  const client = clientSimule();
  render(
    <ContactsDeLEntreprise
      client={client}
      entreprise={{ id: 1, nom: "Lumière Verte SA" }}
      contacts={[contactFictif({ nom: LONGUE, email })]}
      remplacer={vi.fn()}
      quandChanges={vi.fn()}
    />,
  );

  fireEvent.click(screen.getByRole("button", { name: /^Modifier le contact/u }));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer le contact" }));

  await waitFor(() => expect(client.modifierUnContact).toHaveBeenCalledOnce());
  expect(client.modifierUnContact).toHaveBeenCalledWith(4, { nom: LONGUE, email });
});
