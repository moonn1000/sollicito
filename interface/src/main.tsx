import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.tsx";
import { seConnecter } from "./connexion.ts";
import "./styles.css";

const racine = document.getElementById("racine");
if (racine === null) {
  throw new Error("L'élément #racine manque dans index.html.");
}

createRoot(racine).render(
  <StrictMode>
    <App connecter={seConnecter} />
  </StrictMode>,
);
