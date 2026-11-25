import type { CandidatureDeLOffre, FicheDUneOffre, OffreListee, OffreSignalee, SourceDOffre } from "../api/clientDeLApi.ts";
import { afficherLInstant, LIBELLES_DES_STATUTS, simplifier } from "../candidatures/affichage.ts";
import { afficherLeJour, jourDeLEnvoi } from "../relances/affichage.ts";

/** Les sources d'une offre, en toutes lettres. */
export const LIBELLES_DES_SOURCES: Record<SourceDOffre, string> = {
  TexteColle: "Texte collé",
  FichierCsv: "Fichier CSV",
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

/**
 * Une offre signalée comme doublon possible, en une ligne : poste, entreprise, lieu, date d'ajout ; et, si elle a déjà
 * sa candidature, son statut (#38).
 */
export function decrireLeDoublon(doublon: OffreSignalee): string {
  const lieu = doublon.lieu === null ? "" : ` (${doublon.lieu})`;
  const postulee = doublon.candidature === null ? "" : ` ; déjà postulée : ${LIBELLES_DES_STATUTS[doublon.candidature.statut] ?? doublon.candidature.statut}`;
  return `${doublon.poste}, chez ${doublon.entreprise.nom}${lieu}, ajoutée le ${afficherLInstant(doublon.ajouteeLe)}${postulee}`;
}

/** Ce que la fiche d'une offre dit de sa candidature (#38) : « Candidature envoyée le 18/11/2026 10:00 : Envoyée. » */
export function decrireLaCandidature(candidature: CandidatureDeLOffre): string {
  return `Candidature envoyée le ${afficherLInstant(candidature.envoyeeLe)} : ${LIBELLES_DES_STATUTS[candidature.statut] ?? candidature.statut}.`;
}

/** L'annonce d'une candidature faite par « Postuler » (#38). */
export function annonceDeLaPostulation(candidature: { poste: string; entreprise: { nom: string } }): string {
  return `Candidature enregistrée depuis l'offre : ${candidature.poste}, chez ${candidature.entreprise.nom}.`;
}

/** Les périodes du filtre par date d'ajout (#38) : toutes, ou les derniers jours, aujourd'hui compris. */
export type PeriodeDAjout = "toutes" | "7" | "30" | "90";

/** Les périodes, dans l'ordre où le filtre les propose. */
export const PERIODES_D_AJOUT: readonly PeriodeDAjout[] = ["toutes", "7", "30", "90"];

/** Les périodes, en toutes lettres. */
export const LIBELLES_DES_PERIODES: Record<PeriodeDAjout, string> = {
  toutes: "Toutes",
  "7": "Ces 7 derniers jours",
  "30": "Ces 30 derniers jours",
  "90": "Ces 90 derniers jours",
};

/**
 * Les filtres de la liste des offres (#38). Ils ne servent que dans la page, sur la liste déjà lue : rien de la
 * recherche ne quitte l'interface, ni dans une adresse, ni dans un corps.
 */
export interface FiltresDesOffres {
  /** Le texte cherché, tel que tapé. */
  texte: string;
  /** Une source, ou toutes. */
  source: SourceDOffre | "toutes";
  periode: PeriodeDAjout;
}

/** Aucun filtre : toutes les offres. */
export const AUCUN_FILTRE: FiltresDesOffres = { texte: "", source: "toutes", periode: "toutes" };

/** La longueur la plus grande du texte cherché. */
export const RECHERCHE_AU_PLUS = 200;

/** Vrai si un filtre au moins est posé, texte de blancs compris : « Effacer les filtres » le remet à zéro. */
export function filtreActif(filtres: FiltresDesOffres): boolean {
  return filtres.texte.length > 0 || filtres.source !== "toutes" || filtres.periode !== "toutes";
}

/**
 * Les offres que gardent les filtres, dans l'ordre de la liste :
 * - le texte : chacun de ses mots, sans accents, majuscules ni blancs, se trouve dans le poste, l'entreprise ou le lieu ;
 * - la source : celle choisie, ou toutes ;
 * - la période : le jour local d'ajout est l'un des derniers jours choisis, aujourd'hui compris.
 */
export function filtrerLesOffres(offres: readonly OffreListee[], filtres: FiltresDesOffres, maintenant: Date): OffreListee[] {
  const mots = simplifier(filtres.texte).split(" ").filter((mot) => mot.length > 0);
  const depuis = filtres.periode === "toutes" ? undefined : premierJour(maintenant, Number(filtres.periode));
  return offres.filter((offre) => {
    if (filtres.source !== "toutes" && offre.source !== filtres.source) {
      return false;
    }

    if (depuis !== undefined && jourDeLEnvoi(offre.ajouteeLe) < depuis) {
      return false;
    }

    const champs = [offre.poste, offre.entreprise?.nom ?? "", offre.lieu ?? ""].map(simplifier);
    return mots.every((mot) => champs.some((champ) => champ.includes(mot)));
  });
}

// Le premier jour local d'une période de n jours qui finit aujourd'hui : « 7 derniers jours » commence six jours avant.
function premierJour(maintenant: Date, jours: number): string {
  const debut = new Date(maintenant.getFullYear(), maintenant.getMonth(), maintenant.getDate() - (jours - 1), 12);
  return jourDeLEnvoi(debut.toISOString());
}

/** Le bouton qui demande à l'IA de proposer les champs d'une offre collée (#46), et son libellé pendant la demande. */
export const LIBELLE_DE_LA_PROPOSITION = "Proposer les champs avec l'IA";
export const LIBELLE_DE_LA_PROPOSITION_EN_COURS = "L'IA lit l'offre…";

/** Le bouton qui retire, des champs, ce que l'IA a proposé et que l'utilisateur n'a pas modifié. */
export const LIBELLE_DU_RETRAIT_DE_LA_PROPOSITION = "Retirer la proposition";

/** La marque d'un champ que l'IA a rempli, tant que l'utilisateur ne l'a pas modifié. */
export const MARQUE_DE_L_IA = "Proposé par l'IA : relisez-le.";

/** Ce que l'écran dit à la place du bouton quand aucune IA n'est prête. */
export const SANS_IA_PRETE =
  "Sans IA prête, remplissez les champs vous-même. Pour vous faire aider, choisissez une IA et enregistrez sa clé dans les réglages, section « IA ».";

/** Une demande que l'API locale n'a pas reçue, ou à laquelle elle n'a pas répondu. */
export const PROPOSITION_IMPOSSIBLE =
  "La proposition n'a pas pu être demandée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";

/** L'aide sous le bouton : ce que fait l'IA, et où part le texte. */
export function aideDeLaProposition(societe: "Anthropic" | "OpenAI"): string {
  return `L'IA lit le texte collé et propose l'entreprise, le poste, le lieu, la date de publication et l'adresse de l'annonce. Le texte part chez ${societe}, avec votre clé ; rien n'est enregistré sans votre confirmation.`;
}

/**
 * Ce que l'écran dit d'une proposition : combien de champs l'IA a remplis, ou qu'elle n'a rien trouvé, ou que tous les
 * champs trouvés étaient déjà remplis. Les champs que l'utilisateur avait remplis ne sont jamais remplacés.
 */
export function messageDeLaProposition(trouves: number, remplis: number, dejaRemplis: number): string {
  if (trouves === 0) {
    return "L'IA n'a trouvé aucun de ces champs dans le texte : remplissez-les vous-même.";
  }

  if (remplis === 0) {
    return "Tous les champs étaient déjà remplis : rien n'a été changé.";
  }

  const message =
    remplis === 1
      ? "L'IA a proposé 1 champ : relisez-le, et corrigez-le au besoin, avant d'enregistrer l'offre."
      : `L'IA a proposé ${remplis} champs : relisez-les, et corrigez-les au besoin, avant d'enregistrer l'offre.`;
  return dejaRemplis > 0 ? `${message} Les champs que vous aviez remplis n'ont pas été changés.` : message;
}
