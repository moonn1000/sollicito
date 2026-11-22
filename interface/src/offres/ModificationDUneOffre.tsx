import { useEffect, useRef, useState } from "react";
import { estIntrouvable, type ClientDeLApi, type FicheDUneOffre } from "../api/clientDeLApi.ts";
import { FormulaireDUneOffre } from "./FormulaireDUneOffre.tsx";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: FicheDUneOffre } | { etape: "introuvable" } | { etape: "echec" };

/**
 * La correction d'une offre (ADR-0013) : ses champs, préremplis, jamais son texte, qui ne change plus après l'import.
 * Une entreprise ou un poste changés refont la comparaison des doublons possibles : l'utilisateur garde les deux, ou ne
 * corrige pas. Aucune entreprise n'est renommée.
 */
export function ModificationDUneOffre({
  client,
  offre,
  revenir,
  quandModifiee,
  ouvrirUneOffre,
}: {
  client: ClientDeLApi;
  offre: number;
  /** Revient à la fiche de l'offre, sans rien changer. */
  revenir: () => void;
  /** Reçoit la fiche corrigée. */
  quandModifiee: (fiche: FicheDUneOffre) => void;
  /** Ouvre la fiche d'un doublon possible. */
  ouvrirUneOffre: (offre: number) => void;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    let affichee = true;
    client.lireUneOffre(offre).then(
      (fiche) => {
        if (affichee) {
          setChargement({ etape: "prete", fiche });
        }
      },
      (erreur: unknown) => {
        if (affichee) {
          setChargement({ etape: estIntrouvable(erreur) ? "introuvable" : "echec" });
        }
      },
    );
    return () => {
      affichee = false;
    };
  }, [client, offre]);

  useEffect(() => {
    titre.current?.focus();
  }, [chargement.etape]);

  return (
    <section aria-labelledby="titre-de-la-correction-de-l-offre">
      <h2 id="titre-de-la-correction-de-l-offre" ref={titre} tabIndex={-1}>
        Modifier l'offre
      </h2>
      {chargement.etape === "chargement" && <p role="status">Chargement de l'offre…</p>}
      {chargement.etape === "introuvable" && <p className="alerte" role="alert">Cette offre n'existe pas.</p>}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          L'offre n'a pas pu être lue : fermez Sollicito, puis relancez-le.
        </p>
      )}
      {chargement.etape === "prete" && (
        <FormulaireDUneOffre
          client={client}
          depart={{
            texte: "",
            entreprise: chargement.fiche.entreprise?.nom ?? "",
            poste: chargement.fiche.poste,
            lieu: chargement.fiche.lieu ?? "",
            publieeLe: chargement.fiche.publieeLe ?? "",
            adresse: chargement.fiche.adresse ?? "",
          }}
          avecLeTexte={false}
          envoyer={({ entreprise, poste, lieu, publieeLe, adresse }, garderLesDeux) =>
            client.corrigerUneOffre(offre, { entreprise, poste, lieu, publieeLe, adresse }, garderLesDeux)
          }
          quandEnregistree={quandModifiee}
          ouvrirUneOffre={ouvrirUneOffre}
          renoncer={revenir}
          libelleDuBouton="Enregistrer les modifications"
          libelleEnCours="Enregistrement…"
          libelleDuRenoncement="Ne pas modifier"
          messageDEchec="L'offre n'a pas pu être modifiée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le."
          annuler={revenir}
        />
      )}
      {chargement.etape !== "prete" && (
        <div className="actions">
          <button type="button" className="secondaire" onClick={revenir}>
            Revenir
          </button>
        </div>
      )}
    </section>
  );
}
