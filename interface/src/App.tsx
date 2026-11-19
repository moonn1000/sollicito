import { useEffect, useState } from "react";
import type { ClientDeLApi } from "./api/clientDeLApi.ts";
import { HorsDeLaFenetre } from "./connexion.ts";

type EtatDeLaConnexion = "connexion" | "prete" | "hors-fenetre" | "injoignable";

const MESSAGES: Record<EtatDeLaConnexion, string> = {
  connexion: "Connexion à l'API locale…",
  prete: "Connectée à l'API locale.",
  "hors-fenetre": "Cette page ne fonctionne que dans la fenêtre de Sollicito.",
  injoignable: "L'API locale ne répond pas. Fermez Sollicito, puis relancez-le.",
};

export function App({ connecter }: { connecter: () => Promise<ClientDeLApi> }) {
  const [etat, setEtat] = useState<EtatDeLaConnexion>("connexion");

  useEffect(() => {
    let affichee = true;
    connecter()
      .then((client) => client.lireEtat())
      .then(
        (reponse) => {
          if (affichee) {
            setEtat(reponse.statut === "pret" ? "prete" : "injoignable");
          }
        },
        (erreur: unknown) => {
          if (affichee) {
            setEtat(erreur instanceof HorsDeLaFenetre ? "hors-fenetre" : "injoignable");
          }
        },
      );
    return () => {
      affichee = false;
    };
  }, [connecter]);

  return (
    <main>
      <h1>Sollicito</h1>
      <p role="status">{MESSAGES[etat]}</p>
    </main>
  );
}
