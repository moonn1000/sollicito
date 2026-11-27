import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.tsx";
import { seConnecter } from "./connexion.ts";
import {
  canalDeLaFenetre,
  demanderLaRestauration,
  demanderLeBrouillon,
  demanderLeBrouillonRelu,
  demanderLeChoixDuDossier,
  demanderLExport,
  type FormatDExport,
} from "./hote/canalDeLHote.ts";
import "./styles.css";

const racine = document.getElementById("racine");
if (racine === null) {
  throw new Error("L'élément #racine manque dans index.html.");
}

// Le dialogue de dossier des sauvegardes passe par la fenêtre hôte, seule à en donner un chemin (#26). Le brouillon d'une
// relance aussi : seule la fenêtre hôte ouvre la messagerie, à partir des champs que l'API lit en base (#14).
const canal = canalDeLaFenetre();
const choisirLeDossierDesSauvegardes = canal === undefined ? undefined : () => demanderLeChoixDuDossier(canal);
const ouvrirLeBrouillon = canal === undefined ? undefined : (relance: number) => demanderLeBrouillon(canal, relance);
// Le brouillon relu d'un premier jet de l'IA (#47) : la page dépose le texte auprès de l'API, puis l'hôte l'ouvre.
const ouvrirLeBrouillonRelu = canal === undefined ? undefined : (relance: number) => demanderLeBrouillonRelu(canal, relance);
// La restauration d'une copie aussi (#28) : la page n'envoie que son identifiant, et l'hôte demande lui-même le oui.
const restaurerUneSauvegarde =
  canal === undefined ? undefined : (copie: string, quandEnCours: () => void) => demanderLaRestauration(canal, copie, quandEnCours);
// L'export aussi (#49) : la page n'envoie que le format, et l'hôte fait choisir le fichier dans son dialogue.
const exporterLesDonnees =
  canal === undefined ? undefined : (format: FormatDExport, quandEnCours: () => void) => demanderLExport(canal, format, quandEnCours);

createRoot(racine).render(
  <StrictMode>
    <App
      connecter={seConnecter}
      choisirLeDossierDesSauvegardes={choisirLeDossierDesSauvegardes}
      ouvrirLeBrouillon={ouvrirLeBrouillon}
      ouvrirLeBrouillonRelu={ouvrirLeBrouillonRelu}
      restaurerUneSauvegarde={restaurerUneSauvegarde}
      exporterLesDonnees={exporterLesDonnees}
    />
  </StrictMode>,
);
