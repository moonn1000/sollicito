import { vi } from "vitest";
import type { Candidature, ClientDeLApi, EntrepriseConnue } from "../api/clientDeLApi.ts";

/** Une candidature fictive. */
export function candidatureFictive(modifications: Partial<Candidature> = {}): Candidature {
  return {
    id: 1,
    entreprise: { id: 1, nom: "Lumière Verte SA" },
    poste: "Comptable",
    envoyeeLe: "2026-03-27T22:30:00Z",
    statut: "Envoyee",
    ...modifications,
  };
}

/** Des entreprises fictives, déjà enregistrées. */
export const ENTREPRISES_FICTIVES: EntrepriseConnue[] = [
  { id: 1, nom: "Lumière Verte SA" },
  { id: 2, nom: "Brasserie Lune Rousse S.R.L." },
  { id: 3, nom: "Les Amis du Parc ASBL" },
];

/** Le client de l'API, simulé : chaque appel est un espion qui répond ce que le test veut. */
export function clientSimule(modifications: Partial<ClientDeLApi> = {}) {
  return {
    lireEtat: vi.fn<ClientDeLApi["lireEtat"]>(async () => ({ statut: "pret" })),
    listerLesCandidatures: vi.fn<ClientDeLApi["listerLesCandidatures"]>(async () => []),
    listerLesEntreprises: vi.fn<ClientDeLApi["listerLesEntreprises"]>(async () => ENTREPRISES_FICTIVES),
    enregistrerUneCandidature: vi.fn<ClientDeLApi["enregistrerUneCandidature"]>(async (saisie) => ({
      enregistree: true,
      candidature: candidatureFictive({ entreprise: { id: 9, nom: saisie.entreprise }, poste: saisie.poste }),
    })),
    ...modifications,
  } satisfies ClientDeLApi;
}
