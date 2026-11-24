import type { ComptesDuFichier, LigneDuCompteRendu } from "../api/clientDeLApi.ts";
import { afficherLeJour, jourDeLEnvoi } from "../relances/affichage.ts";
import { decrireLeDoublon } from "./affichage.ts";

/** L'en-tête du format, à recopier en première ligne du fichier (#40, docs/import-csv.md). */
export const EN_TETE_DU_FORMAT = "poste;entreprise;lieu;publieeLe;adresse;texte";

/** Le moment d'un compte rendu : l'aperçu, qui n'écrit rien, ou l'import, qui a écrit. */
export type MomentDuCompteRendu = "apercu" | "import";

// Un nombre et son nom, au singulier ou au pluriel.
function accorder(nombre: number, singulier: string, pluriel: string): string {
  return `${nombre} ${nombre === 1 ? singulier : pluriel}`;
}

/**
 * Les comptes d'un compte rendu, en une ligne chacun, sans les comptes nuls : ce que l'import ferait, à l'aperçu, ou ce
 * qu'il a fait. Le premier dit toujours ce qui s'importe ou s'est importé.
 */
export function decrireLesComptes(comptes: ComptesDuFichier, moment: MomentDuCompteRendu): string[] {
  const lignes =
    moment === "apercu"
      ? [comptes.aImporter === 0 ? "Aucune offre à importer" : accorder(comptes.aImporter, "offre à importer", "offres à importer")]
      : [comptes.importees === 0 ? "Aucune offre importée" : accorder(comptes.importees, "offre importée", "offres importées")];
  if (comptes.dejaEnregistrees > 0) {
    lignes.push(`${accorder(comptes.dejaEnregistrees, "déjà enregistrée", "déjà enregistrées")} : ${comptes.dejaEnregistrees === 1 ? "écartée" : "écartées"}`);
  }

  if (comptes.enDouble > 0) {
    lignes.push(`${comptes.enDouble} en double dans le fichier : ${comptes.enDouble === 1 ? "écartée" : "écartées"}`);
  }

  if (comptes.doublonsPossibles > 0) {
    lignes.push(
      moment === "apercu"
        ? comptes.doublonsPossibles === 1
          ? "1 doublon possible : importé seulement si vous le gardez"
          : `${comptes.doublonsPossibles} doublons possibles : importés seulement si vous les gardez`
        : `${accorder(comptes.doublonsPossibles, "doublon possible non gardé", "doublons possibles non gardés")} : ${comptes.doublonsPossibles === 1 ? "écarté" : "écartés"}`,
    );
  }

  if (comptes.invalides > 0) {
    lignes.push(`${accorder(comptes.invalides, "ligne invalide", "lignes invalides")} : ${comptes.invalides === 1 ? "non importée" : "non importées"}`);
  }

  if (comptes.lignesVides > 0) {
    lignes.push(comptes.lignesVides === 1 ? "1 ligne vide ignorée" : `${comptes.lignesVides} lignes vides ignorées`);
  }

  return lignes;
}

/** L'annonce d'un import fait : « 9 offres importées. » */
export function annonceDeLImport(comptes: ComptesDuFichier): string {
  return comptes.importees === 0 ? "Aucune offre importée." : `${accorder(comptes.importees, "offre importée", "offres importées")}.`;
}

/** Le sort d'une ligne, en quelques mots, à l'aperçu ou à l'import. */
export function decrireLeSort(ligne: LigneDuCompteRendu, moment: MomentDuCompteRendu): string {
  switch (ligne.sort) {
    case "aImporter":
      return "À importer";
    case "importee":
      return ligne.doublons !== null || ligne.lignesDuFichier !== null ? "Importée : doublon possible gardé" : "Importée";
    case "dejaEnregistree":
      return ligne.trouvee === null
        ? "Déjà enregistrée : écartée"
        : `Déjà enregistrée : ajoutée le ${afficherLeJour(jourDeLEnvoi(ligne.trouvee.ajouteeLe))}, écartée`;
    case "enDouble":
      return ligne.ligneDOrigine === null ? "En double dans le fichier : écartée" : `En double : la même offre qu'à la ligne ${ligne.ligneDOrigine}, écartée`;
    case "doublonPossible":
      return moment === "apercu" ? "Doublon possible" : "Écartée : doublon possible non gardé";
    case "invalide":
      return "Invalide : non importée";
  }
}

/** Les offres et les lignes dont une ligne est le doublon possible, une par entrée. */
export function decrireLesDoublons(ligne: LigneDuCompteRendu): string[] {
  return [
    ...(ligne.doublons ?? []).map(decrireLeDoublon),
    ...(ligne.lignesDuFichier ?? []).map((numero) => `la ligne ${numero} du fichier`),
  ];
}

/**
 * Les motifs d'une ligne invalide, un par colonne refusée : « colonne publieeLe : La date … ». Le motif d'une ligne mal
 * formée se dit seul. Aucun ne reprend le contenu de la ligne.
 */
export function decrireLesMotifs(ligne: LigneDuCompteRendu): string[] {
  return Object.entries(ligne.refus ?? {}).flatMap(([colonne, motifs]) =>
    motifs.map((motif) => (colonne === "ligne" ? motif : `colonne ${colonne} : ${motif}`)),
  );
}
