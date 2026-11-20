import { useEffect, useRef } from "react";
import type { ClientDeLApi } from "../api/clientDeLApi.ts";
import type { IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { SectionDesSauvegardes } from "../sauvegardes/SectionDesSauvegardes.tsx";

/**
 * L'écran des réglages (#26) : sa première section, « Sauvegardes ». Le délai des relances (#14) et le fournisseur d'IA
 * (#43) y ajouteront la leur. Les réglages vivent hors de la base, dans le dossier de données (ADR-0008).
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
    </section>
  );
}
