import { useEffect, useLayoutEffect, useRef, useState } from "react";
import type { ClientDeLApi, EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import { ANNONCE_DE_LA_COPIE, texteDeLAlerte } from "./affichage.ts";

/** Le délai entre deux lectures de l'état, tant que la vérification du lancement n'est pas faite. */
export const DELAI_DE_RELECTURE_EN_MS = 1_000;

/** Le nombre de ces lectures rapprochées au plus : une minute. Au-delà, l'état se relit à l'intervalle ordinaire. */
export const RELECTURES_AU_PLUS = 60;

/** Le délai entre deux lectures de l'état, une fois la vérification faite, tant que le tableau de bord est affiché. */
export const INTERVALLE_DE_RELECTURE_EN_MS = 30_000;

/** Ce que l'alerte dit d'un « Réessayer » que l'API n'a pas mené au bout. */
export const ECHEC_DE_L_ESSAI = "Le nouvel essai n'a pas abouti. Réessayez ; si rien ne change, fermez Sollicito, puis relancez-le.";

/**
 * L'alerte des sauvegardes, au tableau de bord (#26) : quand les copies ne se font plus, elle dit depuis quand, et quoi
 * faire ; « Réessayer » refait la copie du jour, « Ouvrir les réglages » mène à la section des sauvegardes. Elle lit
 * l'état que l'API tient en mémoire, sans toucher au dossier, et le relit tant que le tableau de bord est affiché : elle
 * vient quand une copie échoue, et part dès que l'une réussit. Rien ne s'affiche quand tout va bien.
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
  // L'annonce d'un « Réessayer », avec l'alerte à l'écran quand il a répondu : elle ne vaut que sous cette alerte.
  const [annonce, setAnnonce] = useState<{ texte: string; sous: string | undefined }>();
  const [enCours, setEnCours] = useState(false);
  const occupe = useRef(false);
  // Change à chaque « Réessayer » réussi : une lecture partie avant sa réponse rapporterait l'état d'avant lui.
  const essaisReussis = useRef(0);
  // L'alerte que montre l'écran, relue par un « Réessayer » qui répond plus tard.
  const alerteAffichee = useRef<string | undefined>(undefined);

  useEffect(() => {
    let affichee = true;
    let minuterie: ReturnType<typeof setTimeout> | undefined;
    // Au lancement, la vérification des copies se fait en arrière-plan : tant qu'elle n'est pas faite, l'état se relit
    // chaque seconde, pour qu'un dossier débranché se dise sur ce premier écran ; ensuite, à l'intervalle ordinaire.
    // L'état lu remplace l'ancien sans rien effacer entre-temps : l'écran ne clignote pas.
    const lire = (lectures: number) => {
      const essai = essaisReussis.current;
      const relire = (rapprochee: boolean) => {
        if (affichee) {
          minuterie = setTimeout(() => lire(lectures + 1), rapprochee ? DELAI_DE_RELECTURE_EN_MS : INTERVALLE_DE_RELECTURE_EN_MS);
        }
      };
      client.lireLesSauvegardes().then(
        (lu) => {
          if (!affichee) {
            return;
          }

          if (essai === essaisReussis.current) {
            setEtat(lu);
          }

          relire(!lu.verifie && lectures < RELECTURES_AU_PLUS);
        },
        // Sans l'état, l'écran garde ce qu'il montrait : la section des réglages dira l'erreur.
        () => relire(false),
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
      essaisReussis.current += 1;
      setEtat(apres);
      setAnnonce(texteDeLAlerte(apres) === undefined ? { texte: ANNONCE_DE_LA_COPIE, sous: undefined } : undefined);
    } catch {
      // Sous l'alerte affichée quand l'essai répond : une relecture a pu la changer pendant qu'il attendait. Partie
      // entre-temps, elle ne laisse rien à réessayer, et le message ne reste pas seul.
      const sous = alerteAffichee.current;
      setAnnonce(sous === undefined ? undefined : { texte: ECHEC_DE_L_ESSAI, sous });
    } finally {
      occupe.current = false;
      setEnCours(false);
    }
  };

  const alerte = etat === undefined ? undefined : texteDeLAlerte(etat);
  // Avant que l'écran ne se peigne : un essai qui répond ensuite lit l'alerte qu'on voit.
  useLayoutEffect(() => {
    alerteAffichee.current = alerte;
  });

  // Une relecture qui change l'alerte (venue, partie ou autre) emporte l'annonce d'un essai d'avant : elle ne dirait plus
  // ce qui est à l'écran, et ne doit pas revenir avec une alerte qui repart.
  if (annonce !== undefined && annonce.sous !== alerte) {
    setAnnonce(undefined);
  }

  const annonceEnCours = annonce !== undefined && annonce.sous === alerte ? annonce.texte : undefined;
  if (alerte === undefined) {
    return annonceEnCours === undefined ? null : (
      <p className="annonce" role="status">
        {annonceEnCours}
      </p>
    );
  }

  return (
    <div className="alerte-des-sauvegardes" role="alert">
      <p>{alerte}</p>
      {annonceEnCours === ECHEC_DE_L_ESSAI && <p>{ECHEC_DE_L_ESSAI}</p>}
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
