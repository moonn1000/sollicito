import { useEffect, useRef, useState } from "react";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { MESSAGES_DU_BROUILLON } from "./affichage.ts";

/**
 * « Préparer le brouillon » d'une relance (#14, ADR-0011) : la page demande à la fenêtre hôte d'ouvrir le brouillon dans
 * la messagerie de l'utilisateur, en ne lui donnant que l'identifiant de la relance. La page n'ouvre jamais rien
 * elle-même, et Sollicito n'envoie rien : c'est l'utilisateur qui envoie le brouillon. L'issue s'affiche sous le bouton.
 */
export function PreparerLeBrouillon({
  relance,
  ouvrirLeBrouillon,
  description,
  desactive = false,
  apres,
}: {
  relance: number;
  /** Demande à la fenêtre hôte d'ouvrir le brouillon, et rend l'issue. */
  ouvrirLeBrouillon: (relance: number) => Promise<IssueDuBrouillon>;
  /** Le nom accessible du bouton, qui dit de quelle relance il s'agit. */
  description: string;
  desactive?: boolean;
  /** Prévenu de l'issue, par exemple pour relire une relance qui n'est plus à faire. */
  apres?: (issue: IssueDuBrouillon) => void;
}) {
  const [enCours, setEnCours] = useState(false);
  const [issue, setIssue] = useState<IssueDuBrouillon>();
  const affiche = useRef(true);
  const enCoursDEnvoi = useRef(false);

  useEffect(() => {
    affiche.current = true;
    return () => {
      affiche.current = false;
    };
  }, []);

  async function preparer() {
    if (enCoursDEnvoi.current) {
      return;
    }

    enCoursDEnvoi.current = true;
    setEnCours(true);
    setIssue(undefined);
    let recue: IssueDuBrouillon;
    try {
      recue = await ouvrirLeBrouillon(relance);
    } catch {
      recue = "sansReponse";
    } finally {
      enCoursDEnvoi.current = false;
    }

    if (affiche.current) {
      setEnCours(false);
      setIssue(recue);
      apres?.(recue);
    }
  }

  return (
    <div className="brouillon">
      <button type="button" disabled={desactive || enCours} aria-label={description} onClick={() => void preparer()}>
        {enCours ? "Ouverture du brouillon…" : "Préparer le brouillon"}
      </button>
      {issue === "ouvert" && (
        <p className="annonce" role="status">
          {MESSAGES_DU_BROUILLON.ouvert}
        </p>
      )}
      {issue !== undefined && issue !== "ouvert" && (
        <p className="alerte" role="alert">
          {MESSAGES_DU_BROUILLON[issue]}
        </p>
      )}
    </div>
  );
}
