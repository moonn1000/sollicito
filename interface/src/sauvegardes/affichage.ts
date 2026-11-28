import type { EtatDesSauvegardes, SorteDeCopie } from "../api/clientDeLApi.ts";
import type { IssueDeLaRestauration, IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";

/** Les sortes de copies, en toutes lettres. */
export const LIBELLES_DES_SORTES: Record<SorteDeCopie, string> = {
  DuJour: "Copie du jour",
  DuJourDUneAutreInstallation: "Copie du jour d'une autre installation",
  AvantRestauration: "Avant une restauration ou un import",
  AvantMiseAJour: "Avant une mise à jour",
};

const MOIS = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

/**
 * Un instant en toutes lettres, à l'heure du poste, comme le dialogue de l'hôte l'écrit : « 18 novembre 2026 à 21:04 »,
 * « 1er décembre 2026 à 09:05 ».
 */
export function afficherLaDateDUneCopie(instant: string): string {
  const date = new Date(instant);
  const deuxChiffres = (nombre: number) => String(nombre).padStart(2, "0");
  const jour = date.getDate() === 1 ? "1er" : String(date.getDate());
  return `${jour} ${MOIS[date.getMonth()]} ${date.getFullYear()} à ${deuxChiffres(date.getHours())}:${deuxChiffres(date.getMinutes())}`;
}

/** Ce que dit l'écran pendant une restauration : en attente du dialogue de l'hôte, puis en cours. */
export const ATTENTE_DE_LA_CONFIRMATION = "Confirmez la restauration dans la fenêtre de Sollicito.";
export const RESTAURATION_EN_COURS = "Restauration en cours : ne fermez pas Sollicito.";

/** L'annonce d'une restauration réussie, sur le tableau de bord. */
export function annonceDeLaRestauration(issue: "restauree" | "restaureeDepuisUneBaseAbimee", faiteLe: string): string {
  const retrouvee = `Base restaurée : vous retrouvez la copie du ${afficherLaDateDUneCopie(faiteLe)}.`;
  return issue === "restauree"
    ? `${retrouvee} Votre base d'avant est gardée dans la copie « Avant une restauration ou un import ».`
    : `${retrouvee} Les fichiers de votre base abîmée sont gardés tels quels dans le dossier des sauvegardes par défaut.`;
}

/** Ce que dit l'écran d'une restauration qui n'a pas changé la base. */
export const ANNONCES_DE_LA_RESTAURATION: Record<Exclude<IssueDeLaRestauration, "restauree" | "restaureeDepuisUneBaseAbimee">, string> = {
  annulee: "Restauration annulée : votre base n'a pas changé.",
  abimee: "Cette copie est abîmée : Sollicito ne l'a pas restaurée. Votre base n'a pas changé.",
  etrangere: "Ce fichier n'est pas une copie de Sollicito que cette version sait lire : il n'a pas été restauré. Votre base n'a pas changé.",
  plusRecente:
    "Cette copie vient d'une version plus récente de Sollicito, que celle-ci ne sait pas lire : elle n'a pas été restaurée. Votre base n'a pas changé.",
  introuvable: "Cette copie est introuvable : son support a peut-être été débranché. Votre base n'a pas changé.",
  changee: "Cette copie a changé depuis l'affichage de la liste : choisissez-la de nouveau. Votre base n'a pas changé.",
  baseChangee: "L'état de votre base a changé depuis votre confirmation : demandez de nouveau la restauration. Votre base n'a pas changé.",
  occupee: "Sollicito est occupé : réessayez dans un instant. Votre base n'a pas changé.",
  copieDAvantImpossible:
    "Sollicito n'a pas pu copier votre base actuelle : il ne l'a pas remplacée. Vérifiez la place libre sur le disque, puis réessayez.",
  impossible: "La restauration n'a pas pu se faire : votre base n'a pas changé. Vérifiez la place libre sur le disque, puis réessayez.",
};

/** Ce que dit l'écran après le choix d'un dossier, selon l'issue que l'hôte rend. */
export const ANNONCES_DU_CHOIX: Record<IssueDuChoixDuDossier, string> = {
  choisi: "Dossier des sauvegardes changé : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.",
  annule: "Le dossier des sauvegardes n'a pas changé.",
  refuse: "Sollicito n'a pas pu faire de copie dans ce dossier : le dossier des sauvegardes n'a pas changé.",
  occupe: "Sollicito est occupé : réessayez dans un instant. Le dossier des sauvegardes n'a pas changé.",
};

/** L'annonce d'un retour au dossier par défaut réussi. */
export const ANNONCE_DU_RETOUR =
  "Retour au dossier par défaut : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.";

/** L'annonce d'une copie du jour refaite à la demande. */
export const ANNONCE_DE_LA_COPIE = "Copie du jour faite.";

/**
 * Ce que l'alerte des sauvegardes dit, ou rien si tout va bien : depuis quand aucune copie n'a réussi, et quoi faire ; ou
 * un réglage du dossier qui n'a pas pu être relu.
 */
export function texteDeLAlerte(etat: EtatDesSauvegardes): string | undefined {
  if (etat.echec !== null) {
    const depuis =
      etat.derniereCopie !== null
        ? `Aucune copie de votre base n'a réussi depuis le ${afficherLInstant(etat.derniereCopie)}`
        : `Aucune copie de votre base n'a pu se faire depuis le ${afficherLInstant(etat.echec.depuis)}`;
    return etat.echec.motif === "DossierIntrouvable"
      ? `${depuis} : le dossier des sauvegardes est introuvable. Rebranchez son support, ou choisissez un autre dossier.`
      : `${depuis} : Sollicito n'a pas pu y écrire de copie. Vérifiez la place libre sur ce support, ou choisissez un autre dossier.`;
  }

  if (etat.reglageIllisible) {
    return "Le réglage du dossier des sauvegardes n'a pas pu être relu : les copies vont dans le dossier par défaut. Choisissez de nouveau votre dossier.";
  }

  return undefined;
}
