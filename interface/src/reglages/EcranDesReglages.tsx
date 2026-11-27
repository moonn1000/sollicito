import { useEffect, useRef, useState } from "react";
import type { ClientDeLApi, CopieGardee } from "../api/clientDeLApi.ts";
import type { IssueDeLaRestauration, IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { SectionDeLIA } from "../ia/SectionDeLIA.tsx";
import { SectionDesRelances } from "../relances/SectionDesRelances.tsx";
import { SectionDesSauvegardes } from "../sauvegardes/SectionDesSauvegardes.tsx";

/**
 * L'écran des réglages (#26) : la section « Sauvegardes », puis celle des relances et de leur délai par défaut (#14),
 * puis celle de l'IA (#43). Les réglages vivent hors de la base, dans le dossier de données (ADR-0008) ; la clé d'IA,
 * chiffrée, aussi, à part d'eux (ADR-0016).
 */
export function EcranDesReglages({
  client,
  choisirLeDossierDesSauvegardes,
  restaurerUneSauvegarde,
  quandRestauree,
  prendreLeFocus = false,
  allerALIA = false,
}: {
  client: ClientDeLApi;
  /** Ouvre le dialogue de dossier de la fenêtre hôte ; absent hors de la fenêtre. */
  choisirLeDossierDesSauvegardes?: () => Promise<IssueDuChoixDuDossier>;
  /** Demande à la fenêtre hôte de restaurer une copie ; absent hors de la fenêtre (#28). */
  restaurerUneSauvegarde?: (copie: string, quandEnCours: () => void) => Promise<IssueDeLaRestauration>;
  /** Une copie vient de remplacer la base (#28). */
  quandRestauree?: (issue: "restauree" | "restaureeDepuisUneBaseAbimee", copie: CopieGardee) => void;
  /** Vrai quand l'utilisateur vient d'y être mené : son titre reçoit alors le focus. */
  prendreLeFocus?: boolean;
  /** Vrai quand l'utilisateur y vient par la carte « Connecter une IA ? » : la section « IA » reçoit le focus. */
  allerALIA?: boolean;
}) {
  const titre = useRef<HTMLHeadingElement>(null);
  // Un dossier choisi réécrit reglages.json (ADR-0008) : les sections qui en lisent un réglage le relisent (#93, point 12).
  const [reecritures, setReecritures] = useState(0);

  useEffect(() => {
    if (prendreLeFocus) {
      titre.current?.focus();
    }
    // Au montage seulement : le focus suit un changement d'écran.
  }, []);

  const choisirLeDossier =
    choisirLeDossierDesSauvegardes === undefined
      ? undefined
      : async () => {
          const issue = await choisirLeDossierDesSauvegardes();
          if (issue === "choisi") {
            setReecritures((nombre) => nombre + 1);
          }
          return issue;
        };

  return (
    <section aria-labelledby="titre-des-reglages">
      <h2 id="titre-des-reglages" ref={titre} tabIndex={-1}>
        Réglages
      </h2>
      <SectionDesSauvegardes
        client={client}
        choisirLeDossier={choisirLeDossier}
        restaurer={restaurerUneSauvegarde}
        quandRestauree={quandRestauree}
      />
      <SectionDesRelances client={client} reecritures={reecritures} />
      <SectionDeLIA client={client} prendreLeFocus={allerALIA} reecritures={reecritures} />
    </section>
  );
}
