import { useEffect, useRef, useState } from "react";
import type { Candidature, ClientDeLApi } from "../api/clientDeLApi.ts";
import { heureSaisieDe, LIBELLES_DES_STATUTS } from "./affichage.ts";
import { FormulaireDUneCandidature } from "./FormulaireDUneCandidature.tsx";

/**
 * L'écran de saisie d'une candidature : entreprise, poste, date et heure d'envoi ; le statut de départ est toujours
 * Envoyée. L'API valide chaque champ : l'écran envoie la saisie telle quelle et affiche ses refus à côté du champ
 * concerné (voir `FormulaireDUneCandidature`, que la modification d'une candidature reprend).
 */
export function SaisieDUneCandidature({
  client,
  quandEnregistree,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  quandEnregistree: (candidature: Candidature) => void;
  /** L'heure présente, qui remplit d'avance la date et l'heure d'envoi. */
  maintenant?: () => Date;
}) {
  const [depart] = useState(() => ({ entreprise: "", poste: "", envoyeeLe: heureSaisieDe(maintenant()) }));
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    titre.current?.focus();
  }, []);

  return (
    <section aria-labelledby="titre-de-la-saisie">
      <h2 id="titre-de-la-saisie" ref={titre} tabIndex={-1}>
        Nouvelle candidature
      </h2>
      <FormulaireDUneCandidature
        client={client}
        depart={depart}
        envoyer={async (saisie) => {
          const resultat = await client.enregistrerUneCandidature(saisie);
          if (resultat.enregistree) {
            quandEnregistree(resultat.candidature);
            return { acceptee: true };
          }

          return { acceptee: false, refus: resultat.refus };
        }}
        libelleDuBouton="Enregistrer la candidature"
        libelleEnCours="Enregistrement…"
        messageDEchec="La candidature n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le."
      >
        <p className="statut-de-depart">
          Statut de départ : <strong>{LIBELLES_DES_STATUTS.Envoyee}</strong>
        </p>
      </FormulaireDUneCandidature>
    </section>
  );
}
