import { vi } from "vitest";
import type {
  Candidature,
  ChangementDeStatutLu,
  ClientDeLApi,
  ClotureDeRelanceLue,
  ContactConnu,
  EntrepriseConnue,
  EtapeLue,
  EtatDesSauvegardes,
  FicheDUneCandidature,
  RelanceLue,
  RelanceSignalee,
} from "../api/clientDeLApi.ts";

/** Un dossier de données fictif, sous un compte fictif. */
export const DOSSIER_PAR_DEFAUT_FICTIF = "C:\\Users\\Compte.Fictif\\AppData\\Local\\Sollicito\\Sauvegardes";

/** L'état fictif de sauvegardes qui vont bien : le dossier par défaut, une copie du jour, le 18/11/2026 à 14:30. */
export function sauvegardesFictives(modifications: Partial<EtatDesSauvegardes> = {}): EtatDesSauvegardes {
  return {
    dossier: DOSSIER_PAR_DEFAUT_FICTIF,
    parDefaut: true,
    dossierParDefaut: DOSSIER_PAR_DEFAUT_FICTIF,
    derniereCopie: "2026-11-18T13:30:00Z",
    copies: [{ nom: "sollicito-1a2b3c4d-2026-11-18.db", sorte: "DuJour", faiteLe: "2026-11-18T13:30:00Z" }],
    reglageIllisible: false,
    echec: null,
    verifie: true,
    ...modifications,
  };
}

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
    relances: [],
    ...modifications,
  };
}

/** Un contact fictif de Lumière Verte SA. */
export function contactFictif(modifications: Partial<ContactConnu> = {}): ContactConnu {
  return { id: 4, nom: "Jeanne Martin", email: "jeanne.martin@exemple.invalid", ...modifications };
}

/** Une relance fictive, à faire le vendredi 3 avril 2026, sans destinataire. */
export function relanceFictive(modifications: Partial<RelanceLue> = {}): RelanceLue {
  return { id: 7, echeance: "2026-04-03", etat: "AFaire", destinataire: null, ...modifications };
}

/** La clôture fictive d'une relance faite, le 3 avril 2026 à 10:00 à Bruxelles. */
export function clotureFictive(modifications: Partial<ClotureDeRelanceLue> = {}): ClotureDeRelanceLue {
  return {
    sorte: "clotureDeRelance",
    id: 3,
    survenuLe: "2026-04-03T08:00:00Z",
    relance: 7,
    etat: "Faite",
    echeance: "2026-04-03",
    destinataire: contactFictif(),
    ...modifications,
  };
}

/** Une relance fictive que signale le tableau de bord. */
export function relanceSignaleeFictive(modifications: Partial<RelanceSignalee> = {}): RelanceSignalee {
  return {
    id: 7,
    echeance: "2026-11-17",
    candidature: { id: 1, poste: "Comptable", entreprise: { id: 1, nom: "Lumière Verte SA" } },
    destinataire: null,
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
    corrige: null,
    estCorrige: false,
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
    corrigerUneEtape: vi.fn<ClientDeLApi["corrigerUneEtape"]>(async (candidature) => ({
      issue: "faite",
      fiche: ficheFictive({ id: candidature }),
    })),
    modifierUneCandidature: vi.fn<ClientDeLApi["modifierUneCandidature"]>(async (candidature, saisie) => ({
      issue: "modifiee",
      fiche: ficheFictive({ id: candidature, entreprise: { id: 9, nom: saisie.entreprise.trim() }, poste: saisie.poste.trim() }),
    })),
    supprimerUneCandidature: vi.fn<ClientDeLApi["supprimerUneCandidature"]>(async () => undefined),
    lireLesSauvegardes: vi.fn<ClientDeLApi["lireLesSauvegardes"]>(async () => sauvegardesFictives()),
    sauvegarderMaintenant: vi.fn<ClientDeLApi["sauvegarderMaintenant"]>(async () => sauvegardesFictives()),
    revenirAuDossierParDefaut: vi.fn<ClientDeLApi["revenirAuDossierParDefaut"]>(async () => ({ issue: "fait", etat: sauvegardesFictives() })),
    listerLesContacts: vi.fn<ClientDeLApi["listerLesContacts"]>(async () => []),
    ajouterUnContact: vi.fn<ClientDeLApi["ajouterUnContact"]>(async (_entreprise, saisie) => ({
      issue: "enregistre",
      contact: contactFictif({ id: 9, nom: saisie.nom.trim(), email: saisie.email.trim() }),
    })),
    modifierUnContact: vi.fn<ClientDeLApi["modifierUnContact"]>(async (contact, saisie) => ({
      issue: "enregistre",
      contact: { id: contact, nom: saisie.nom.trim(), email: saisie.email.trim() },
    })),
    supprimerUnContact: vi.fn<ClientDeLApi["supprimerUnContact"]>(async () => undefined),
    planifierUneRelance: vi.fn<ClientDeLApi["planifierUneRelance"]>(async (candidature) => ({
      issue: "faite",
      fiche: ficheFictive({ id: candidature, relances: [relanceFictive()] }),
    })),
    changerLaRelance: vi.fn<ClientDeLApi["changerLaRelance"]>(async (candidature) => ({
      issue: "faite",
      fiche: ficheFictive({ id: candidature, relances: [relanceFictive()] }),
    })),
    marquerLaRelanceFaite: vi.fn<ClientDeLApi["marquerLaRelanceFaite"]>(async (candidature) => ({
      faite: true,
      fiche: ficheFictive({ id: candidature, relances: [relanceFictive({ etat: "Faite" })], historique: [clotureFictive()] }),
    })),
    abandonnerLaRelance: vi.fn<ClientDeLApi["abandonnerLaRelance"]>(async (candidature) => ({
      faite: true,
      fiche: ficheFictive({ id: candidature, relances: [relanceFictive({ etat: "Abandonnee" })], historique: [clotureFictive({ etat: "Abandonnee" })] }),
    })),
    listerLesRelances: vi.fn<ClientDeLApi["listerLesRelances"]>(async () => ({ aujourdhui: "2026-11-18", echues: [], deLaSemaine: [] })),
    lireLeReglageDesRelances: vi.fn<ClientDeLApi["lireLeReglageDesRelances"]>(async () => ({
      delaiParDefaut: 7,
      delaiLisible: true,
      fichierLisible: true,
    })),
    changerLeDelaiParDefaut: vi.fn<ClientDeLApi["changerLeDelaiParDefaut"]>(async (jours) => ({
      issue: "change",
      reglage: { delaiParDefaut: jours, delaiLisible: true, fichierLisible: true },
    })),
    ...modifications,
  } satisfies ClientDeLApi;
}
