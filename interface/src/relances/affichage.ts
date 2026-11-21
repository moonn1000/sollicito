import type { ContactConnu, EtatDeRelance, FicheDUneCandidature } from "../api/clientDeLApi.ts";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";

/** Les bornes du délai d'une relance, en jours (modèle de domaine, « Relances »). */
export const DELAI_MINIMUM = 1;
export const DELAI_MAXIMUM = 365;

/** Les états d'une relance, en toutes lettres. */
export const LIBELLES_DES_ETATS_DE_RELANCE: Record<EtatDeRelance, string> = {
  AFaire: "À faire",
  Faite: "Faite",
  Abandonnee: "Abandonnée",
};

const FORMAT_DU_JOUR = new Intl.DateTimeFormat("fr-BE", {
  weekday: "long",
  day: "numeric",
  month: "long",
  year: "numeric",
  timeZone: "UTC",
});

const UN_JOUR_EN_MS = 86_400_000;

/**
 * Un jour de l'API, sans heure ni fuseau, en toutes lettres : `2026-04-03` donne « vendredi 3 avril 2026 ». Il se lit
 * en UTC, pour qu'aucun fuseau ne le décale d'un jour.
 */
export function afficherLeJour(jour: string): string {
  return FORMAT_DU_JOUR.format(versDate(jour));
}

/**
 * Le jour local de l'envoi d'une candidature, dans le fuseau du poste : `2026-03-27T22:30:00Z` donne `2026-03-27` à
 * Bruxelles. L'API calcule l'échéance de la même façon ; l'écran ne s'en sert que pour la montrer avant l'envoi.
 */
export function jourDeLEnvoi(envoyeeLe: string): string {
  const envoi = new Date(envoyeeLe);
  const deuxChiffres = (nombre: number) => String(nombre).padStart(2, "0");
  return `${envoi.getFullYear()}-${deuxChiffres(envoi.getMonth() + 1)}-${deuxChiffres(envoi.getDate())}`;
}

/** L'échéance d'une relance : le jour local de l'envoi plus le délai, en jours. */
export function echeancePour(envoyeeLe: string, delai: number): string {
  return versJour(new Date(versDate(jourDeLEnvoi(envoyeeLe)).getTime() + delai * UN_JOUR_EN_MS));
}

/** Le délai d'une relance : les jours qui séparent le jour local de l'envoi de son échéance. */
export function delaiDe(envoyeeLe: string, echeance: string): number {
  return Math.round((versDate(echeance).getTime() - versDate(jourDeLEnvoi(envoyeeLe)).getTime()) / UN_JOUR_EN_MS);
}

/**
 * Le délai que propose l'écran pour une nouvelle relance : le délai par défaut, ou, après une relance close, son délai
 * plus le délai par défaut (7, puis 14, puis 21 jours…), sans dépasser 365 jours. La relance suivante part aussi de la
 * date d'envoi.
 */
export function delaiPropose(fiche: Pick<FicheDUneCandidature, "envoyeeLe" | "relances">, delaiParDefaut: number): number {
  const derniere = [...fiche.relances].reverse().find((relance) => relance.etat !== "AFaire");
  if (derniere === undefined) {
    return delaiParDefaut;
  }

  return Math.min(DELAI_MAXIMUM, Math.max(DELAI_MINIMUM, delaiDe(fiche.envoyeeLe, derniere.echeance) + delaiParDefaut));
}

/**
 * Où tombe une échéance par rapport à aujourd'hui : « échue depuis 3 jours », « échue aujourd'hui », « demain »,
 * « dans 5 jours ».
 */
export function situerLEcheance(echeance: string, aujourdhui: string): string {
  const ecart = Math.round((versDate(echeance).getTime() - versDate(aujourdhui).getTime()) / UN_JOUR_EN_MS);
  if (ecart < -1) {
    return `échue depuis ${-ecart} jours`;
  }

  if (ecart === -1) {
    return "échue depuis hier";
  }

  if (ecart === 0) {
    return "échue aujourd'hui";
  }

  return ecart === 1 ? "demain" : `dans ${ecart} jours`;
}

/** Le destinataire d'une relance, en clair : « Jeanne Martin (jeanne@exemple.invalid) », ou « sans destinataire ». */
export function decrireLeDestinataire(destinataire: ContactConnu | null): string {
  return destinataire === null ? "sans destinataire" : `${destinataire.nom} (${destinataire.email})`;
}

/** Ce que l'écran dit après une demande de brouillon. */
export const MESSAGES_DU_BROUILLON: Record<IssueDuBrouillon, string> = {
  ouvert: "Le brouillon s'est ouvert dans votre messagerie : relisez-le, puis envoyez-le vous-même. Une fois envoyé, marquez la relance comme faite.",
  refuse: "Le brouillon ne s'est pas ouvert : cette relance n'est plus à faire.",
  impossible:
    "Le brouillon ne s'est pas ouvert : Windows n'a pas pu le passer à une messagerie. Vérifiez qu'une messagerie est choisie pour les liens « mailto » dans les applications par défaut de Windows.",
  sansReponse: "Le brouillon ne s'est pas ouvert : la fenêtre de Sollicito n'a pas répondu. Réessayez.",
};

function versDate(jour: string): Date {
  return new Date(`${jour}T00:00:00Z`);
}

function versJour(date: Date): string {
  return date.toISOString().slice(0, 10);
}
