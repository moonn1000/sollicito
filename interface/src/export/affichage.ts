import type { FormatDExport, IssueDeLExport } from "../hote/canalDeLHote.ts";

/** Ce que contient l'export, et ce qu'il ne contient jamais (#49). */
export const CONTENU_DE_L_EXPORT =
  "L'export rassemble toutes les données de votre base : entreprises, contacts, offres, candidatures, leur historique et leurs relances. Il ne contient ni votre clé d'IA ni vos réglages.";

/** L'avertissement, toujours visible : l'export n'est pas chiffré, et part là où son dossier l'envoie. */
export const AVERTISSEMENT_DE_L_EXPORT =
  "L'export n'est pas chiffré : toute personne qui accède au fichier peut le lire. Enregistré dans un dossier synchronisé, comme OneDrive, il part là où ce dossier l'envoie.";

/** Ce que donne chaque forme de l'export. */
export const FORMES_DE_L_EXPORT = "JSON : un seul fichier, qui garde chaque valeur telle quelle.";

/** Ce que dit l'écran pendant un export : en attente du dialogue de l'hôte, puis en cours. */
export const ATTENTE_DU_CHOIX = "Choisissez où enregistrer l'export dans la fenêtre de Sollicito.";
export const EXPORT_EN_COURS = "Export en cours : ne fermez pas Sollicito.";

/** Les boutons, un par forme. */
export const BOUTONS_DE_L_EXPORT: Record<FormatDExport, string> = {
  json: "Exporter en JSON…",
};

const EXTENSIONS: Record<FormatDExport, string> = {
  json: ".json",
};

/** Vrai pour une issue qui s'annonce, et faux pour un échec, qui s'affiche en alerte. */
export function estUneAnnonce(issue: IssueDeLExport): boolean {
  return issue === "exporte" || issue === "annule";
}

/** Ce que dit l'écran d'un export, selon l'issue que l'hôte rend. */
export function texteDeLExport(issue: IssueDeLExport, format: FormatDExport): string {
  switch (issue) {
    case "exporte":
      return "Export enregistré dans le fichier choisi.";
    case "annule":
      return "Export annulé : rien n'a été enregistré.";
    case "occupe":
      return "Sollicito est occupé : réessayez dans un instant. Rien n'a été enregistré.";
    case "existe":
      return "Un fichier du même nom est apparu entre-temps : Sollicito ne l'a pas remplacé. Exportez de nouveau.";
    case "extension":
      return `Le nom du fichier doit finir par ${EXTENSIONS[format]} : rien n'a été enregistré. Exportez de nouveau sans changer l'extension.`;
    case "dossierDeDonnees":
      return "Ce dossier est celui des données de Sollicito : choisissez-en un autre. Rien n'a été enregistré.";
    case "accesRefuse":
      return "Sollicito n'a pas le droit d'écrire dans ce dossier, ou de remplacer ce fichier : rien n'a été enregistré. Choisissez un autre dossier.";
    case "placeManquante":
      return "La place manque sur ce disque : rien n'a été enregistré. Libérez de la place, ou choisissez un autre support.";
    case "dossierIntrouvable":
      return "Ce dossier est introuvable : son support a peut-être été débranché. Rien n'a été enregistré.";
    case "impossible":
      return "L'export n'a pas pu se faire : rien n'a été enregistré. Le fichier est peut-être ouvert dans un autre programme ; réessayez, ou choisissez un autre nom.";
  }
}
