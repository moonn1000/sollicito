import { useEffect, useRef, useState } from "react";
import {
  estIntrouvable,
  type ClientDeLApi,
  type EntrepriseConnue,
  type FicheDUneCandidature as Fiche,
} from "../api/clientDeLApi.ts";
import { afficherLInstant, heureSaisieDe, LIBELLES_DES_STATUTS } from "./affichage.ts";
import { FormulaireDUneCandidature } from "./FormulaireDUneCandidature.tsx";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: Fiche } | { etape: "introuvable" } | { etape: "echec" };

/**
 * L'avertissement d'un nom qui corrigerait celui d'une entreprise enregistrée (ADR-0007) : l'entreprise, sous son nom
 * enregistré, le nom saisi, que l'écran tient de son propre état, et le nombre de candidatures concernées.
 */
type Avertissement = { entreprise: EntrepriseConnue; nomSaisi: string; candidatures: number };

/**
 * L'écran de modification d'une candidature (#25), ouvert depuis sa fiche : son entreprise, son poste et sa date
 * d'envoi, remplis d'avance, la date à la minute, dans le fuseau du poste. Le statut ne se modifie pas ici : il avance
 * depuis la fiche. L'API valide chaque champ avec les règles de la saisie, et la date d'envoi ne suit pas le premier
 * événement en vigueur de l'historique, que l'écran rappelle sous le champ. Un nom de même clé qu'une entreprise
 * enregistrée, écrit autrement, corrige son nom pour toutes ses candidatures : l'écran le dit avant d'enregistrer, et
 * attend l'accord de l'utilisateur, ou qu'il garde le nom enregistré.
 */
export function ModificationDUneCandidature({
  client,
  candidature,
  revenir,
  quandModifiee,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  candidature: number;
  /** Ramène à la fiche, sans rien changer. */
  revenir: () => void;
  /** Reçoit la fiche à jour. */
  quandModifiee: (fiche: Fiche) => void;
  /** L'heure présente : un premier événement à venir ne borne pas l'envoi, que l'API refuse déjà dans le futur. */
  maintenant?: () => Date;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [avertissement, setAvertissement] = useState<Avertissement>();
  // Le choix fait dans l'avertissement, lu par l'envoi qu'il relance : corriger le nom, ou garder le nom enregistré.
  const choix = useRef<"corriger" | "garder">(undefined);
  const titre = useRef<HTMLHeadingElement>(null);
  const titreDeLAvertissement = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    titre.current?.focus();
  }, []);

  useEffect(() => {
    if (avertissement !== undefined) {
      titreDeLAvertissement.current?.focus();
    }
  }, [avertissement]);

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
            // Relancé depuis l'avertissement : avec l'accord de corriger le nom, ou sous le nom enregistré.
            const decision = choix.current;
            choix.current = undefined;
            const envoi = decision === "garder" && avertissement !== undefined ? { ...saisie, entreprise: avertissement.entreprise.nom } : saisie;
            setAvertissement(undefined);
            try {
              const resultat = await client.modifierUneCandidature(candidature, envoi, decision === "corriger");
              switch (resultat.issue) {
                case "modifiee":
                  quandModifiee(resultat.fiche);
                  return { acceptee: true };
                case "refusee":
                  return { acceptee: false, refus: resultat.refus };
                case "nomACorriger":
                  setAvertissement({ entreprise: resultat.entreprise, nomSaisi: envoi.entreprise.trim(), candidatures: resultat.candidatures });
                  return { acceptee: false, refus: {} };
              }
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
          aideDeLEnvoi={premier === undefined ? undefined : aideDeLaDate(premier, maintenant())}
          annuler={revenir}
          quandModifie={() => {
            // Un champ modifié rend l'avertissement caduc, et le choix qu'on y aurait fait avec lui.
            choix.current = undefined;
            setAvertissement(undefined);
          }}
        >
          <p className="statut-de-depart">
            Statut : <strong>{LIBELLES_DES_STATUTS[fiche.statut] ?? fiche.statut}</strong>. Il ne change pas ici : il
            avance depuis la fiche.
          </p>
          {avertissement !== undefined && (
            <section className="avertissement" aria-labelledby="titre-de-l-avertissement" aria-describedby="texte-de-l-avertissement">
              <h3 id="titre-de-l-avertissement" ref={titreDeLAvertissement} tabIndex={-1}>
                Corriger le nom de l'entreprise ?
              </h3>
              <p id="texte-de-l-avertissement">{texteDeLAvertissement(avertissement, fiche.entreprise)}</p>
              <div className="actions">
                <button
                  type="button"
                  onClick={(evenement) => {
                    choix.current = "corriger";
                    evenement.currentTarget.form?.requestSubmit();
                  }}
                >
                  {avertissement.candidatures === 1 ? "Corriger le nom" : `Corriger le nom pour les ${avertissement.candidatures} candidatures`}
                </button>
                <button
                  type="button"
                  className="secondaire"
                  onClick={(evenement) => {
                    choix.current = "garder";
                    evenement.currentTarget.form?.requestSubmit();
                  }}
                >
                  Garder « {avertissement.entreprise.nom} »
                </button>
              </div>
            </section>
          )}
        </FormulaireDUneCandidature>
      )}
    </section>
  );
}

// Ce que l'écran rappelle sous la date d'envoi : le premier événement en vigueur la borne ; à venir, c'est l'instant
// présent qui la borne, l'API refusant un envoi dans le futur.
function aideDeLaDate(premier: string, maintenant: Date): string {
  return Date.parse(premier) <= maintenant.getTime()
    ? `Au plus tard le ${afficherLInstant(premier)}, date du premier événement de l'historique.`
    : `Le premier événement de l'historique, le ${afficherLInstant(premier)}, est à venir : l'envoi ne peut pas être dans le futur.`;
}

// Ce que dit l'avertissement : si la candidature change d'entreprise, quel nom serait corrigé, en quoi, et pour combien
// de candidatures.
function texteDeLAvertissement(avertissement: Avertissement, actuelle: EntrepriseConnue): string {
  const { entreprise, nomSaisi, candidatures } = avertissement;
  const portee = candidatures === 1 ? "pour cette seule candidature" : `pour ses ${candidatures} candidatures, celle-ci comprise`;
  const correction = `Son nom affiché « ${entreprise.nom} » deviendrait « ${nomSaisi} », ${portee}.`;
  return entreprise.id === actuelle.id
    ? `Ce nom est celui de l'entreprise de la candidature, écrit autrement. ${correction}`
    : `La candidature quitterait « ${actuelle.nom} » pour rejoindre l'entreprise enregistrée « ${entreprise.nom} », que ce nom désigne. ${correction}`;
}

// L'instant du premier événement en vigueur de l'historique, quelle que soit sa sorte : un événement marqué comme
// corrigé ne compte plus. Rien s'il n'y en a aucun.
function premierEvenement(fiche: Fiche): string | undefined {
  let premier: string | undefined;
  for (const evenement of fiche.historique) {
    if (evenement.estCorrige) {
      continue;
    }

    if (premier === undefined || Date.parse(evenement.survenuLe) < Date.parse(premier)) {
      premier = evenement.survenuLe;
    }
  }

  return premier;
}
