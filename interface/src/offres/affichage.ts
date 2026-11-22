import type { CandidatureDeLOffre, FicheDUneOffre, OffreListee, OffreSignalee, SourceDOffre } from "../api/clientDeLApi.ts";
import { afficherLInstant, LIBELLES_DES_STATUTS, simplifier } from "../candidatures/affichage.ts";
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

/** Vrai si un filtre au moins restreint la liste. */
export function filtreActif(filtres: FiltresDesOffres): boolean {
  return simplifier(filtres.texte).length > 0 || filtres.source !== "toutes" || filtres.periode !== "toutes";
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
