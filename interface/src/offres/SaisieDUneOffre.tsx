import { useEffect, useRef } from "react";
import type { ClientDeLApi, FicheDUneOffre } from "../api/clientDeLApi.ts";
import { FormulaireDUneOffre } from "./FormulaireDUneOffre.tsx";

const VIDE = { texte: "", entreprise: "", poste: "", lieu: "", publieeLe: "", adresse: "" };

/**
 * L'écran « Coller une offre » (#36) : le texte d'une offre trouvée ailleurs, gardé tel quel, et ses champs, que
 * l'utilisateur remplit lui-même, ou fait proposer par l'IA réglée, sur un clic (#46) ; la proposition se relit et se
 * corrige avant l'enregistrement. L'API valide et dédoublonne : le même texte recollé mène à l'offre enregistrée ; un
 * doublon possible se garde, ou s'abandonne.
 */
export function SaisieDUneOffre({
  client,
  quandEnregistree,
  renoncer,
}: {
  client: ClientDeLApi;
  /** Reçoit la fiche de l'offre que l'API vient d'enregistrer. */
  quandEnregistree: (fiche: FicheDUneOffre) => void;
  /** Renonce à l'offre, devant un doublon possible. */
  renoncer: () => void;
}) {
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    titre.current?.focus();
  }, []);

  return (
    <section aria-labelledby="titre-de-l-offre-collee">
      <h2 id="titre-de-l-offre-collee" ref={titre} tabIndex={-1}>
        Coller une offre
      </h2>
      <FormulaireDUneOffre
        client={client}
        depart={VIDE}
        avecLeTexte
        proposerAvecLIA
        envoyer={(saisie, garderLesDeux) => client.collerUneOffre(saisie, garderLesDeux)}
        quandEnregistree={quandEnregistree}
        renoncer={renoncer}
        libelleDuBouton="Enregistrer l'offre"
        libelleEnCours="Enregistrement…"
        libelleDuRenoncement="Renoncer à la nouvelle"
        messageDEchec="L'offre n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le."
      />
    </section>
  );
}
