import { useEffect, useRef, useState } from "react";
import { estIntrouvable, type ClientDeLApi, type FicheDUneOffre } from "../api/clientDeLApi.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";
import { LienExterne } from "../liens/LienExterne.tsx";
import { afficherLeJour } from "../relances/affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: FicheDUneOffre } | { etape: "introuvable" } | { etape: "echec" };

/**
 * L'aperçu d'une autre offre, sur place, dans la saisie ou la correction d'une offre (suites de #36, #98 ; #38) : celle
 * du même texte, ou un doublon possible. La saisie reste telle quelle, texte collé compris : on compare les deux, puis on
 * garde les deux ou on renonce. Les textes s'affichent comme du texte, jamais comme du HTML : React les échappe.
 */
export function ApercuDUneOffre({ client, offre, masquer }: { client: ClientDeLApi; offre: number; masquer: () => void }) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    let affiche = true;
    setChargement({ etape: "chargement" });
    client.lireUneOffre(offre).then(
      (fiche) => {
        if (affiche) {
          setChargement({ etape: "prete", fiche });
        }
      },
      (erreur: unknown) => {
        if (affiche) {
          setChargement({ etape: estIntrouvable(erreur) ? "introuvable" : "echec" });
        }
      },
    );
    return () => {
      affiche = false;
    };
  }, [client, offre]);

  useEffect(() => {
    titre.current?.focus();
  }, [chargement.etape]);

  const fiche = chargement.etape === "prete" ? chargement.fiche : undefined;
  return (
    <section aria-labelledby="titre-de-l-apercu" className="apercu-d-une-offre">
      <h4 id="titre-de-l-apercu" ref={titre} tabIndex={-1}>
        {fiche?.poste ?? "Offre"}
      </h4>
      {chargement.etape === "chargement" && <p role="status">Chargement de l'offre…</p>}
      {chargement.etape === "introuvable" && <p className="alerte">Cette offre n'existe pas.</p>}
      {chargement.etape === "echec" && <p className="alerte">L'offre n'a pas pu être lue : fermez Sollicito, puis relancez-le.</p>}
      {fiche !== undefined && (
        <>
          <dl className="resume">
            <dt>Entreprise</dt>
            <dd>{fiche.entreprise?.nom ?? "Entreprise non indiquée"}</dd>
            {fiche.lieu !== null && (
              <>
                <dt>Lieu</dt>
                <dd>{fiche.lieu}</dd>
              </>
            )}
            {fiche.publieeLe !== null && (
              <>
                <dt>Publiée le</dt>
                <dd>{afficherLeJour(fiche.publieeLe)}</dd>
              </>
            )}
            <dt>Ajoutée le</dt>
            <dd>{afficherLInstant(fiche.ajouteeLe)}</dd>
            {fiche.adresse !== null && (
              <>
                <dt>Annonce</dt>
                <dd>
                  <LienExterne adresse={fiche.adresse} />
                </dd>
              </>
            )}
          </dl>
          {fiche.texte !== null && <div className="texte-de-l-offre">{fiche.texte}</div>}
        </>
      )}
      <div className="actions">
        <button type="button" className="secondaire" onClick={masquer}>
          Masquer cette offre
        </button>
      </div>
    </section>
  );
}
