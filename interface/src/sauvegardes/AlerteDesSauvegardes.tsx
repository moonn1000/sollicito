import { useEffect, useRef, useState } from "react";
import type { ClientDeLApi, EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import { ANNONCE_DE_LA_COPIE, texteDeLAlerte } from "./affichage.ts";

/**
 * L'alerte des sauvegardes, au tableau de bord (#26) : quand les copies ne se font plus, elle dit depuis quand, et quoi
 * faire ; « Réessayer » refait la copie du jour, « Ouvrir les réglages » mène à la section des sauvegardes. Elle lit
 * l'état que l'API tient en mémoire, sans toucher au dossier. Rien ne s'affiche quand tout va bien.
 */
export function AlerteDesSauvegardes({
  client,
  ouvrirLesReglages,
}: {
  client: ClientDeLApi;
  /** Mène aux réglages ; sans lui, l'alerte n'offre que « Réessayer ». */
  ouvrirLesReglages?: () => void;
}) {
  const [etat, setEtat] = useState<EtatDesSauvegardes>();
  const [annonce, setAnnonce] = useState<string>();
  const [enCours, setEnCours] = useState(false);
  const occupe = useRef(false);

  useEffect(() => {
    let affichee = true;
    client.lireLesSauvegardes().then(
      (lu) => {
        if (affichee) {
          setEtat(lu);
        }
      },
      // Sans l'état, pas d'alerte : la section des réglages dira l'erreur.
      () => {},
    );
    return () => {
      affichee = false;
    };
  }, [client]);

  const reessayer = async () => {
    if (occupe.current) {
      return;
    }

    occupe.current = true;
    setEnCours(true);
    try {
      const apres = await client.sauvegarderMaintenant();
      setEtat(apres);
      setAnnonce(texteDeLAlerte(apres) === undefined ? ANNONCE_DE_LA_COPIE : undefined);
    } catch {
      setAnnonce(undefined);
    } finally {
      occupe.current = false;
      setEnCours(false);
    }
  };

  const alerte = etat === undefined ? undefined : texteDeLAlerte(etat);
  if (alerte === undefined) {
    return annonce === undefined ? null : (
      <p className="annonce" role="status">
        {annonce}
      </p>
    );
  }

  return (
    <div className="alerte-des-sauvegardes" role="alert">
      <p>{alerte}</p>
      <div className="actions">
        {etat?.echec !== null && (
          <button type="button" disabled={enCours} onClick={reessayer}>
            Réessayer
          </button>
        )}
        {ouvrirLesReglages !== undefined && (
          <button type="button" className="secondaire" onClick={ouvrirLesReglages}>
            Ouvrir les réglages
          </button>
        )}
      </div>
    </div>
  );
}
