import { useEffect, useState } from "react";
import type { ClientDeLApi, EtatDeLIA } from "../api/clientDeLApi.ts";

/** Un fournisseur d'IA en ligne, qui s'appelle par la clé de l'utilisateur (#48, ADR-0017). */
export type FournisseurEnLigne = "Claude" | "OpenAI";

/**
 * Le fournisseur prêt à répondre, ou rien : Claude ou OpenAI choisi, son réglage lisible, et sa clé enregistrée et
 * lisible. Le modèle local (Ollama) n'est pas encore branché : #45 l'ajoutera ici. Un écran s'en sert pour montrer, ou
 * non, le bouton qui demande à l'IA (#46, #47). Prête ici, l'IA peut encore échouer à l'appel : l'API refait ce choix à
 * chaque demande, et refuse sans requête une IA qui ne l'est plus.
 */
export function fournisseurPret(etat: EtatDeLIA): FournisseurEnLigne | undefined {
  if (!etat.reglageLisible || etat.cle !== "Enregistree") {
    return undefined;
  }

  return etat.fournisseur === "Claude" || etat.fournisseur === "OpenAI" ? etat.fournisseur : undefined;
}

/** La société chez qui l'utilisateur a son compte, pour un fournisseur en ligne : « chez Anthropic », « chez OpenAI ». */
export function societeDuFournisseur(fournisseur: FournisseurEnLigne): "Anthropic" | "OpenAI" {
  return fournisseur === "Claude" ? "Anthropic" : "OpenAI";
}

/**
 * Ce qu'un écran sait de l'IA, lue à son ouverture : la lecture en cours ; prête, avec son fournisseur ; pas prête ; ou
 * inconnue, quand l'état n'a pas pu être lu. Inconnue, un écran montre son bouton : le clic dira ce qu'il en est.
 */
export type DisponibiliteDeLIA =
  | { sorte: "lecture" }
  | { sorte: "prete"; fournisseur: FournisseurEnLigne }
  | { sorte: "absente" }
  | { sorte: "inconnue" };

/**
 * Lit l'état de l'IA une fois, à l'ouverture de l'écran, sans jamais appeler l'IA : `GET /api/reglages/ia`, sur le
 * poste. Chaque lecture déchiffre la clé enregistrée pour dire si elle se relit (ADR-0016), comme l'ouverture des
 * réglages.
 */
export function useDisponibiliteDeLIA(client: ClientDeLApi): DisponibiliteDeLIA {
  const [disponibilite, setDisponibilite] = useState<DisponibiliteDeLIA>({ sorte: "lecture" });

  useEffect(() => {
    let affiche = true;
    client.lireLEtatDeLIA().then(
      (etat) => {
        if (affiche) {
          const fournisseur = fournisseurPret(etat);
          setDisponibilite(fournisseur === undefined ? { sorte: "absente" } : { sorte: "prete", fournisseur });
        }
      },
      () => {
        if (affiche) {
          setDisponibilite({ sorte: "inconnue" });
        }
      },
    );
    return () => {
      affiche = false;
    };
  }, [client]);

  return disponibilite;
}
