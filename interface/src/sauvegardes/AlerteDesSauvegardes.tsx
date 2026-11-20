import { useEffect, useRef, useState } from "react";
import type { ClientDeLApi, EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import { ANNONCE_DE_LA_COPIE, texteDeLAlerte } from "./affichage.ts";

/** Le délai entre deux lectures de l'état, tant que la vérification du lancement n'est pas faite. */
export const DELAI_DE_RELECTURE_EN_MS = 1_000;

/** Le nombre de lectures au plus, avant de renoncer : une minute. */
export const RELECTURES_AU_PLUS = 60;

/** Ce que l'alerte dit d'un « Réessayer » que l'API n'a pas mené au bout. */
export const ECHEC_DE_L_ESSAI = "Le nouvel essai n'a pas abouti. Réessayez ; si rien ne change, fermez Sollicito, puis relancez-le.";

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
    let minuterie: ReturnType<typeof setTimeout> | undefined;
    // Au lancement, la vérification des copies se fait en arrière-plan : tant qu'elle n'est pas faite, l'état se relit,
    // pour qu'un dossier débranché se dise sur ce premier écran.
    const lire = (essai: number) => {
      client.lireLesSauvegardes().then(
        (lu) => {
          if (!affichee) {
            return;
          }

          setEtat(lu);
          if (!lu.verifie && essai < RELECTURES_AU_PLUS) {
            minuterie = setTimeout(() => lire(essai + 1), DELAI_DE_RELECTURE_EN_MS);
          }
        },
        // Sans l'état, pas d'alerte : la section des réglages dira l'erreur.
        () => {},
      );
    };
    lire(1);
    return () => {
      affichee = false;
      clearTimeout(minuterie);
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
      setAnnonce(ECHEC_DE_L_ESSAI);
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
      {annonce === ECHEC_DE_L_ESSAI && <p>{ECHEC_DE_L_ESSAI}</p>}
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
