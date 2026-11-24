import { useState } from "react";
import type { ClientDeLApi, FicheDUneCandidature } from "../api/clientDeLApi.ts";
import { heureSaisieDe, LIBELLES_DES_TYPES_D_ETAPE } from "./affichage.ts";
import { FormulaireDUneEtape } from "./FormulaireDUneEtape.tsx";

/**
 * Noter un entretien ou un échange dans l'historique d'une candidature (#41, absorbée par #12) : un type, une date et
 * une heure, passées ou à venir, et une note facultative. L'étape ne change pas le statut. Comme l'écran de saisie,
 * le formulaire envoie les champs tels quels et affiche les refus de l'API à côté de chacun ; un refus disparaît dès
 * qu'on modifie son champ, ou quand l'envoi suivant échoue (voir `FormulaireDUneEtape`, que la correction d'une étape
 * reprend).
 */
export function NoterUneEtape({
  client,
  candidature,
  quandNotee,
  autresActions = 0,
  prendreLaGarde,
  occupe = false,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  candidature: number;
  /** Reçoit la fiche à jour, l'étape dans son historique. */
  quandNotee: (fiche: FicheDUneCandidature) => void;
  /** Compte les autres actions de la fiche : chacune efface l'annonce de la dernière étape ajoutée, qui serait périmée. */
  autresActions?: number;
  /**
   * Prend la garde de la fiche, qui n'admet qu'une action à la fois, pour la durée d'un envoi ; rend de quoi la lâcher,
   * ou rien si une autre action la tient (#85, point 5). Sans elle, l'envoi ne tient aucune garde.
   */
  prendreLaGarde?: () => (() => void) | undefined;
  /** Vrai tant qu'une action de la fiche est en cours : l'envoi est alors inactif. */
  occupe?: boolean;
  /** L'heure présente, qui remplit d'avance la date et l'heure. */
  maintenant?: () => Date;
}) {
  const [depart] = useState(() => ({ type: "Entretien" as const, survenuLe: heureSaisieDe(maintenant()), note: "" }));
  // L'annonce de la dernière étape ajoutée, et le nombre d'autres actions de la fiche à ce moment : elle ne s'affiche
  // que tant qu'aucune autre action n'a suivi.
  const [annonce, setAnnonce] = useState<{ texte: string; apres: number }>();

  return (
    <section aria-labelledby="titre-de-la-note">
      <h3 id="titre-de-la-note">Noter un entretien ou un échange</h3>
      <FormulaireDUneEtape
        prefixe="etape"
        depart={depart}
        occupe={occupe}
        envoyer={async (saisie) => {
          const lacher = prendreLaGarde === undefined ? () => undefined : prendreLaGarde();
          if (lacher === undefined) {
            // Une autre action tient la fiche : rien ne part, la saisie reste.
            return { acceptee: false, refus: {} };
          }

          try {
            setAnnonce(undefined);
            const resultat = await client.noterUneEtape(candidature, saisie);
            if (resultat.notee) {
              setAnnonce({ texte: `Étape ajoutée à l'historique : ${LIBELLES_DES_TYPES_D_ETAPE[saisie.type]}.`, apres: autresActions });
              quandNotee(resultat.fiche);
              // Le type reste ; la date repart de l'heure présente, et la note se vide.
              return { acceptee: true, repartirDe: { type: saisie.type, survenuLe: heureSaisieDe(maintenant()), note: "" } };
            }

            return { acceptee: false, refus: resultat.refus };
          } finally {
            lacher();
          }
        }}
        libelleDuBouton="Ajouter à l'historique"
        libelleEnCours="Ajout…"
        messageDEchec="L'étape n'a pas pu être ajoutée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le."
        aide="L'étape s'ajoute à l'historique à sa date ; elle ne change pas le statut."
        quandModifie={() => setAnnonce(undefined)}
      >
        {annonce !== undefined && annonce.apres === autresActions && (
          <p className="annonce" role="status">
            {annonce.texte}
          </p>
        )}
      </FormulaireDUneEtape>
    </section>
  );
}
