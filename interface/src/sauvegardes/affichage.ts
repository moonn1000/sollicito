import type { EtatDesSauvegardes, SorteDeCopie } from "../api/clientDeLApi.ts";
import type { IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";

/** Les sortes de copies, en toutes lettres. */
export const LIBELLES_DES_SORTES: Record<SorteDeCopie, string> = {
  DuJour: "Copie du jour",
  AvantMiseAJour: "Avant une mise à jour",
};

/** Ce que dit l'écran après le choix d'un dossier, selon l'issue que l'hôte rend. */
export const ANNONCES_DU_CHOIX: Record<IssueDuChoixDuDossier, string> = {
  choisi: "Dossier des sauvegardes changé : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.",
  annule: "Le dossier des sauvegardes n'a pas changé.",
  refuse: "Sollicito n'a pas pu faire de copie dans ce dossier : le dossier des sauvegardes n'a pas changé.",
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
