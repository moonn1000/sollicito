import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.tsx";
import { seConnecter } from "./connexion.ts";
import { canalDeLaFenetre, demanderLeBrouillon, demanderLeChoixDuDossier } from "./hote/canalDeLHote.ts";
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

createRoot(racine).render(
  <StrictMode>
    <App connecter={seConnecter} choisirLeDossierDesSauvegardes={choisirLeDossierDesSauvegardes} ouvrirLeBrouillon={ouvrirLeBrouillon} />
  </StrictMode>,
);
