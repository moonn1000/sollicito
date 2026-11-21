import { useEffect, useRef } from "react";
import type { ClientDeLApi } from "../api/clientDeLApi.ts";
import type { IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { SectionDesRelances } from "../relances/SectionDesRelances.tsx";
import { SectionDesSauvegardes } from "../sauvegardes/SectionDesSauvegardes.tsx";

/**
 * L'écran des réglages (#26) : la section « Sauvegardes », puis celle des relances et de leur délai par défaut (#14). Le
 * fournisseur d'IA (#43) y ajoutera la sienne. Les réglages vivent hors de la base, dans le dossier de données
 * (ADR-0008).
 */
export function EcranDesReglages({
  client,
  choisirLeDossierDesSauvegardes,
  prendreLeFocus = false,
}: {
  client: ClientDeLApi;
  /** Ouvre le dialogue de dossier de la fenêtre hôte ; absent hors de la fenêtre. */
  choisirLeDossierDesSauvegardes?: () => Promise<IssueDuChoixDuDossier>;
  /** Vrai quand l'utilisateur vient d'y être mené : son titre reçoit alors le focus. */
  prendreLeFocus?: boolean;
}) {
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (prendreLeFocus) {
      titre.current?.focus();
    }
    // Au montage seulement : le focus suit un changement d'écran.
  }, []);

  return (
    <section aria-labelledby="titre-des-reglages">
      <h2 id="titre-des-reglages" ref={titre} tabIndex={-1}>
        Réglages
      </h2>
      <SectionDesSauvegardes client={client} choisirLeDossier={choisirLeDossierDesSauvegardes} />
      <SectionDesRelances client={client} />
    </section>
  );
}
