import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.tsx";
import { seConnecter } from "./connexion.ts";
import { canalDeLaFenetre, demanderLeChoixDuDossier } from "./hote/canalDeLHote.ts";
import "./styles.css";

const racine = document.getElementById("racine");
if (racine === null) {
  throw new Error("L'élément #racine manque dans index.html.");
}

// Le dialogue de dossier des sauvegardes passe par la fenêtre hôte, seule à en donner un chemin (#26).
const canal = canalDeLaFenetre();
const choisirLeDossierDesSauvegardes = canal === undefined ? undefined : () => demanderLeChoixDuDossier(canal);

createRoot(racine).render(
  <StrictMode>
    <App connecter={seConnecter} choisirLeDossierDesSauvegardes={choisirLeDossierDesSauvegardes} />
  </StrictMode>,
);
