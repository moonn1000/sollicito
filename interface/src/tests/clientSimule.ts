import { vi } from "vitest";
import type {
  Candidature,
  ChangementDeStatutLu,
  ClientDeLApi,
  EntrepriseConnue,
  EtapeLue,
  FicheDUneCandidature,
} from "../api/clientDeLApi.ts";

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

/** La fiche fictive d'une candidature neuve, au statut Envoyée, sans historique. */
export function ficheFictive(modifications: Partial<FicheDUneCandidature> = {}): FicheDUneCandidature {
  return {
    ...candidatureFictive(),
    passagesPermis: ["Entretien", "Refusee", "Retiree", "SansReponse"],
    correction: null,
    historique: [],
    ...modifications,
  };
}

/** Un changement de statut fictif : un passage d'Envoyée à Entretien, le 18/11/2026 à 14:30 à Bruxelles. */
export function changementFictif(modifications: Partial<ChangementDeStatutLu> = {}): ChangementDeStatutLu {
  return {
    sorte: "changementDeStatut",
    id: 1,
    survenuLe: "2026-11-18T13:30:00Z",
    ancien: "Envoyee",
    nouveau: "Entretien",
    corrige: null,
    estCorrige: false,
    ...modifications,
  };
}

/** Une étape fictive : un appel, le 10/11/2026 à 10:00 à Bruxelles, sans note. */
export function etapeFictive(modifications: Partial<EtapeLue> = {}): EtapeLue {
  return {
    sorte: "etape",
    id: 2,
    survenuLe: "2026-11-10T09:00:00Z",
    type: "Appel",
    note: null,
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
    lireLaFiche: vi.fn<ClientDeLApi["lireLaFiche"]>(async (candidature) => ficheFictive({ id: candidature })),
    changerLeStatut: vi.fn<ClientDeLApi["changerLeStatut"]>(async (candidature, statut) => ({
      faite: true,
      fiche: ficheFictive({ id: candidature, statut, passagesPermis: [] }),
    })),
    corrigerLeDernierChangement: vi.fn<ClientDeLApi["corrigerLeDernierChangement"]>(async (candidature) => ({
      faite: true,
      fiche: ficheFictive({ id: candidature }),
    })),
    noterUneEtape: vi.fn<ClientDeLApi["noterUneEtape"]>(async (candidature) => ({
      notee: true,
      fiche: ficheFictive({ id: candidature }),
    })),
    modifierUneCandidature: vi.fn<ClientDeLApi["modifierUneCandidature"]>(async (candidature, saisie) => ({
      modifiee: true,
      fiche: ficheFictive({ id: candidature, entreprise: { id: 9, nom: saisie.entreprise.trim() }, poste: saisie.poste.trim() }),
    })),
    supprimerUneCandidature: vi.fn<ClientDeLApi["supprimerUneCandidature"]>(async () => undefined),
    ...modifications,
  } satisfies ClientDeLApi;
}
