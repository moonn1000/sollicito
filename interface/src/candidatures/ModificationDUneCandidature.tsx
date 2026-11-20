import { useEffect, useRef, useState } from "react";
import { ErreurDeLApi, type ClientDeLApi, type FicheDUneCandidature as Fiche } from "../api/clientDeLApi.ts";
import { afficherLInstant, heureSaisieDe, LIBELLES_DES_STATUTS } from "./affichage.ts";
import { FormulaireDUneCandidature } from "./FormulaireDUneCandidature.tsx";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: Fiche } | { etape: "introuvable" } | { etape: "echec" };

/**
 * L'écran de modification d'une candidature (#25), ouvert depuis sa fiche : son entreprise, son poste et sa date
 * d'envoi, remplis d'avance, la date à la minute, dans le fuseau du poste. Le statut ne se modifie pas ici : il avance
 * depuis la fiche. L'API valide chaque champ avec les règles de la saisie, et la date d'envoi ne suit pas le premier
 * événement de l'historique, que l'écran rappelle sous le champ.
 */
export function ModificationDUneCandidature({
  client,
  candidature,
  revenir,
  quandModifiee,
}: {
  client: ClientDeLApi;
  candidature: number;
  /** Ramène à la fiche, sans rien changer. */
  revenir: () => void;
  /** Reçoit la fiche à jour. */
  quandModifiee: (fiche: Fiche) => void;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    titre.current?.focus();
  }, []);

  useEffect(() => {
    let active = true;
    client.lireLaFiche(candidature).then(
      (fiche) => {
        if (active) {
          setChargement({ etape: "prete", fiche });
        }
      },
      (erreur: unknown) => {
        if (active) {
          setChargement({ etape: estIntrouvable(erreur) ? "introuvable" : "echec" });
        }
      },
    );
    return () => {
      active = false;
    };
  }, [client, candidature]);

  const fiche = chargement.etape === "prete" ? chargement.fiche : undefined;
  const premier = fiche === undefined ? undefined : premierEvenement(fiche);

  return (
    <section aria-labelledby="titre-de-la-modification">
      <h2 id="titre-de-la-modification" ref={titre} tabIndex={-1}>
        Modifier la candidature
      </h2>
      {chargement.etape === "chargement" && <p>Chargement de la candidature…</p>}
      {chargement.etape === "introuvable" && (
        <>
          <p className="alerte" role="alert">
            Cette candidature n'existe pas, ou plus.
          </p>
          <p>
            <button type="button" className="lien" onClick={revenir}>
              Revenir à la fiche
            </button>
          </p>
        </>
      )}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          La candidature n'a pas pu être chargée. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {fiche !== undefined && (
        <FormulaireDUneCandidature
          client={client}
          depart={{ entreprise: fiche.entreprise.nom, poste: fiche.poste, envoyeeLe: heureSaisieDe(new Date(fiche.envoyeeLe)) }}
          envoyer={async (saisie) => {
            try {
              const resultat = await client.modifierUneCandidature(candidature, saisie);
              if (resultat.modifiee) {
                quandModifiee(resultat.fiche);
                return { acceptee: true };
              }

              return { acceptee: false, refus: resultat.refus };
            } catch (erreur) {
              // Supprimée entre-temps : il n'y a plus rien à modifier, l'écran le dit à la place du formulaire.
              if (estIntrouvable(erreur)) {
                setChargement({ etape: "introuvable" });
              }

              throw erreur;
            }
          }}
          libelleDuBouton="Enregistrer les modifications"
          libelleEnCours="Enregistrement…"
          messageDEchec="La candidature n'a pas pu être modifiée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le."
          aideDeLEnvoi={
            premier === undefined ? undefined : `Au plus tard le ${afficherLInstant(premier)}, date du premier événement de l'historique.`
          }
          annuler={revenir}
        >
          <p className="statut-de-depart">
            Statut : <strong>{LIBELLES_DES_STATUTS[fiche.statut] ?? fiche.statut}</strong>. Il ne change pas ici : il
            avance depuis la fiche.
          </p>
        </FormulaireDUneCandidature>
      )}
    </section>
  );
}

// L'instant du premier événement de l'historique, quelle que soit sa sorte ; rien s'il n'y en a aucun.
function premierEvenement(fiche: Fiche): string | undefined {
  let premier: string | undefined;
  for (const evenement of fiche.historique) {
    if (premier === undefined || Date.parse(evenement.survenuLe) < Date.parse(premier)) {
      premier = evenement.survenuLe;
    }
  }

  return premier;
}

function estIntrouvable(erreur: unknown): boolean {
  return erreur instanceof ErreurDeLApi && erreur.statut === 404;
}
