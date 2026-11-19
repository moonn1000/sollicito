import type { EntrepriseConnue, StatutDeCandidature } from "../api/clientDeLApi.ts";

/** Les statuts, en toutes lettres. */
export const LIBELLES_DES_STATUTS: Record<StatutDeCandidature, string> = {
  Envoyee: "Envoyée",
  Entretien: "Entretien",
  Proposition: "Proposition",
  Acceptee: "Acceptée",
  Refusee: "Refusée",
  Retiree: "Retirée",
  SansReponse: "Sans réponse",
};

const FORMAT_DE_L_INSTANT = new Intl.DateTimeFormat("fr-BE", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

/**
 * Un instant de l'API, en UTC, affiché dans le fuseau du poste : `2026-03-27T22:30:00Z` donne « 27/03/2026 23:30 »
 * à Bruxelles. La conversion prend le décalage en vigueur à cet instant, heure d'été comprise.
 */
export function afficherLInstant(instant: string): string {
  return FORMAT_DE_L_INSTANT.format(new Date(instant));
}

/** L'heure locale d'une date, telle qu'un champ `datetime-local` l'écrit : `2026-11-18T14:30`. */
export function heureSaisieDe(date: Date): string {
  const deuxChiffres = (nombre: number) => String(nombre).padStart(2, "0");
  return (
    `${date.getFullYear()}-${deuxChiffres(date.getMonth() + 1)}-${deuxChiffres(date.getDate())}` +
    `T${deuxChiffres(date.getHours())}:${deuxChiffres(date.getMinutes())}`
  );
}

/** Le nombre maximal d'entreprises proposées pendant la saisie. */
export const PROPOSITIONS_AU_PLUS = 5;

/**
 * Les entreprises déjà enregistrées dont le nom contient le texte saisi, sans tenir compte des accents ni des
 * majuscules : « lumiere » propose « Lumière Verte SA ». Ce n'est qu'une aide à la saisie : l'API reconnaît seule
 * l'entreprise, par la clé de son nom.
 */
export function proposerDesEntreprises(connues: readonly EntrepriseConnue[], saisie: string): EntrepriseConnue[] {
  const cherche = simplifier(saisie);
  if (cherche.length === 0) {
    return [];
  }

  return connues
    .filter((entreprise) => entreprise.nom !== saisie.trim() && simplifier(entreprise.nom).includes(cherche))
    .slice(0, PROPOSITIONS_AU_PLUS);
}

function simplifier(texte: string): string {
  return texte.normalize("NFKD").replace(/\p{M}/gu, "").toLowerCase().replace(/\s+/gu, " ").trim();
}
