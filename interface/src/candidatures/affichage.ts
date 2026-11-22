import type { EntrepriseConnue, EvenementDeLHistorique, StatutDeCandidature, TypeDEtape } from "../api/clientDeLApi.ts";
import { afficherLeJour } from "../relances/affichage.ts";

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

/** Les types d'étape, en toutes lettres, dans l'ordre où la fiche les propose. */
export const LIBELLES_DES_TYPES_D_ETAPE: Record<TypeDEtape, string> = {
  Appel: "Appel",
  Entretien: "Entretien",
  Echange: "Échange (courriel, message)",
  Autre: "Autre",
};

/** « d'Envoyée », « de Refusée » : la préposition, élidée devant une voyelle, suivie du statut. */
export function deStatut(statut: StatutDeCandidature): string {
  const libelle = LIBELLES_DES_STATUTS[statut];
  return /^[AEIOUÉ]/u.test(libelle) ? `d'${libelle}` : `de ${libelle}`;
}

/**
 * Ce que dit un événement de l'historique, en une phrase : « Statut passé d'Envoyée à Entretien », « Correction :
 * retour de Refusée à Entretien », « Entretien », « Correction : Entretien » pour une étape corrigée (#25), « Relance
 * faite, à Jeanne Martin : échéance du vendredi 3 avril 2026 » (#14). La note d'une étape s'affiche à part.
 */
export function decrireLEvenement(evenement: EvenementDeLHistorique): string {
  if (evenement.sorte === "etape") {
    const type = LIBELLES_DES_TYPES_D_ETAPE[evenement.type] ?? evenement.type;
    return evenement.corrige === null ? type : `Correction : ${type}`;
  }

  if (evenement.sorte === "clotureDeRelance") {
    const issue = evenement.etat === "Faite" ? "faite" : "abandonnée";
    const destinataire = evenement.destinataire === null ? "sans destinataire" : `à ${evenement.destinataire.nom}`;
    return `Relance ${issue}, ${destinataire} : échéance du ${afficherLeJour(evenement.echeance)}`;
  }

  const vers = LIBELLES_DES_STATUTS[evenement.nouveau];
  return evenement.corrige === null
    ? `Statut passé ${deStatut(evenement.ancien)} à ${vers}`
    : `Correction : retour ${deStatut(evenement.ancien)} à ${vers}`;
}

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
 * Parmi les entreprises connues, celles qu'une candidature ou une offre désigne encore (#36), celles dont le nom
 * contient le texte saisi, sans tenir compte des accents ni des majuscules : « lumiere » propose « Lumière Verte SA ».
 * Ce n'est qu'une aide à la saisie : l'API reconnaît seule l'entreprise, par la clé de son nom.
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
