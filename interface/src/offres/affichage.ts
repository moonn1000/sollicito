import type { FicheDUneOffre, OffreSignalee, SourceDOffre } from "../api/clientDeLApi.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";
import { afficherLeJour, jourDeLEnvoi } from "../relances/affichage.ts";

/** Les sources d'une offre, en toutes lettres. */
export const LIBELLES_DES_SOURCES: Record<SourceDOffre, string> = {
  TexteColle: "Texte collé",
};

/** L'aide du texte collé. */
export const AIDE_DU_TEXTE =
  "Collez l'offre telle que vous l'avez trouvée : elle est gardée telle quelle, 50 000 caractères au plus. Remplissez ensuite ses champs vous-même.";

/** Le message d'un doublon possible, avant tout enregistrement (règle 3). */
export const MESSAGE_DU_DOUBLON = "Doublon possible : une offre du même poste, dans la même entreprise, est déjà enregistrée.";

/** L'annonce d'une offre abandonnée devant un doublon possible. */
export const ANNONCE_DU_RENONCEMENT = "Offre abandonnée : rien n'a été enregistré.";

/** L'annonce d'une offre enregistrée ou corrigée : son poste, et son entreprise si elle en a une. */
export function annonceDeLOffre(verbe: "enregistrée" | "modifiée", fiche: Pick<FicheDUneOffre, "poste" | "entreprise">): string {
  return fiche.entreprise === null ? `Offre ${verbe} : ${fiche.poste}.` : `Offre ${verbe} : ${fiche.poste}, chez ${fiche.entreprise.nom}.`;
}

/** Le message du même texte recollé (règle 2), avec le jour, en heure locale, où l'offre a été collée. */
export function messageDejaEnregistree(ajouteeLe: string): string {
  return `Cette offre est déjà enregistrée : vous l'avez collée le ${afficherLeJour(jourDeLEnvoi(ajouteeLe))}. Rien n'a été ajouté.`;
}

/** Une offre signalée comme doublon possible, en une ligne : poste, entreprise, lieu, date d'ajout. */
export function decrireLeDoublon(doublon: OffreSignalee): string {
  const lieu = doublon.lieu === null ? "" : ` (${doublon.lieu})`;
  return `${doublon.poste}, chez ${doublon.entreprise.nom}${lieu}, ajoutée le ${afficherLInstant(doublon.ajouteeLe)}`;
}
