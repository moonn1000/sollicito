import { useEffect, useRef, useState } from "react";
import {
  ErreurDeLApi,
  type ClientDeLApi,
  type EvenementDeLHistorique,
  type FicheDUneCandidature as Fiche,
  type ResultatDUneAction,
  type StatutDeCandidature,
} from "../api/clientDeLApi.ts";
import { afficherLInstant, decrireLEvenement, LIBELLES_DES_STATUTS } from "./affichage.ts";
import { NoterUneEtape } from "./NoterUneEtape.tsx";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: Fiche } | { etape: "introuvable" } | { etape: "echec" };

/** Ce que dit la fiche après une action : une annonce, ou une alerte. */
type Message = { sorte: "annonce" | "alerte"; texte: string };

const REESSAYER = "Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";

const PAS_RELUE = "La fiche n'a pas pu être relue : fermez Sollicito, puis relancez-le.";

/** La confirmation de la suppression : ouverte, ou fermée, après une annulation qui rend le focus au bouton. */
type Confirmation = "fermee" | "ouverte" | "annulee";

/**
 * La fiche d'une candidature (#12), ouverte depuis le tableau de bord : son résumé, son statut et les seuls passages
 * que le diagramme d'états permet, que l'API donne avec la fiche ; la correction du dernier changement ; une note
 * d'entretien ou d'échange ; l'historique, du plus ancien au plus récent. Elle mène à la modification de la
 * candidature, et la supprime après une confirmation affichée dans l'interface, jamais par une boîte du navigateur
 * (#25). Les textes s'affichent comme du texte, jamais comme du HTML : React les échappe.
 */
export function FicheDUneCandidature({
  client,
  candidature,
  revenir,
  modifier,
  quandSupprimee,
  annonce,
  maintenant,
}: {
  client: ClientDeLApi;
  candidature: number;
  /** Mène au tableau de bord. */
  revenir: () => void;
  /** Mène à l'écran de modification de la candidature. */
  modifier: () => void;
  /** Reçoit la fiche de la candidature que l'API vient de supprimer. */
  quandSupprimee: (fiche: Fiche) => void;
  /** Ce qui vient de se passer, par exemple une modification enregistrée ; jusqu'à la prochaine action. */
  annonce?: string;
  /** L'heure présente, qui remplit d'avance la date et l'heure d'une note. */
  maintenant?: () => Date;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [enCours, setEnCours] = useState(false);
  const [message, setMessage] = useState<Message>();
  const [annonceDeDepart, setAnnonceDeDepart] = useState(annonce);
  const [confirmation, setConfirmation] = useState<Confirmation>("fermee");
  const [suppression, setSuppression] = useState<"pret" | "en-cours" | "echec">("pret");
  // Compte les actions terminées : chacune ramène le focus sur le titre du statut, car le bouton cliqué a pu disparaître.
  const [actionsTerminees, setActionsTerminees] = useState(0);
  const titre = useRef<HTMLHeadingElement>(null);
  const titreDuStatut = useRef<HTMLHeadingElement>(null);
  const titreDeLaConfirmation = useRef<HTMLHeadingElement>(null);
  const boutonSupprimer = useRef<HTMLButtonElement>(null);
  const enCoursDAction = useRef(false);
  const affichee = useRef(true);

  useEffect(() => {
    affichee.current = true;
    titre.current?.focus();
    return () => {
      affichee.current = false;
    };
  }, []);

  useEffect(() => {
    if (actionsTerminees > 0) {
      titreDuStatut.current?.focus();
    }
  }, [actionsTerminees]);

  // Ouverte, la confirmation prend le focus sur sa question ; annulée, elle le rend au bouton qui l'a ouverte.
  useEffect(() => {
    if (confirmation === "ouverte") {
      titreDeLaConfirmation.current?.focus();
    } else if (confirmation === "annulee") {
      boutonSupprimer.current?.focus();
    }
  }, [confirmation]);

  // Une fiche reçue ne remplace la fiche montrée que si elle n'est pas plus ancienne. L'historique se fait par ajouts
  // seulement : une note et un changement de statut lancés ensemble peuvent répondre dans le désordre, et la réponse la
  // plus récente est celle qui porte le plus d'événements.
  const montrer = (recue: Fiche) =>
    setChargement((avant) =>
      avant.etape === "prete" && avant.fiche.historique.length > recue.historique.length ? avant : { etape: "prete", fiche: recue },
    );

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

  async function agir(faire: () => Promise<ResultatDUneAction>, annonce: (fiche: Fiche) => string, echec: string) {
    if (enCoursDAction.current) {
      return;
    }

    enCoursDAction.current = true;
    setEnCours(true);
    setMessage(undefined);
    setAnnonceDeDepart(undefined);
    try {
      const resultat = await faire();
      if (!affichee.current) {
        return;
      }

      if (resultat.faite) {
        montrer(resultat.fiche);
        setMessage({ sorte: "annonce", texte: annonce(resultat.fiche) });
      } else {
        // Refusée par l'API : la fiche montrée n'était plus à jour. On la relit, et le motif reste affiché.
        const relue = await relire();
        setMessage({ sorte: "alerte", texte: relue ? resultat.motif : `${resultat.motif} ${PAS_RELUE}` });
      }
    } catch (erreur) {
      if (!affichee.current) {
        return;
      }

      if (estIntrouvable(erreur)) {
        setChargement({ etape: "introuvable" });
      } else {
        setMessage({ sorte: "alerte", texte: `${echec} ${REESSAYER}` });
      }
    } finally {
      enCoursDAction.current = false;
      if (affichee.current) {
        setEnCours(false);
        setActionsTerminees((nombre) => nombre + 1);
      }
    }
  }

  // Relit la fiche ; rend faux si elle n'a pas pu être relue.
  async function relire(): Promise<boolean> {
    try {
      const fiche = await client.lireLaFiche(candidature);
      if (affichee.current) {
        montrer(fiche);
      }
      return true;
    } catch (erreur) {
      if (affichee.current && estIntrouvable(erreur)) {
        setChargement({ etape: "introuvable" });
      }
      return false;
    }
  }

  const changerLeStatut = (statut: StatutDeCandidature) =>
    agir(
      () => client.changerLeStatut(candidature, statut),
      (fiche) => `Statut changé : ${LIBELLES_DES_STATUTS[fiche.statut]}.`,
      "Le statut n'a pas pu être changé.",
    );

  const corriger = (changement: number) =>
    agir(
      () => client.corrigerLeDernierChangement(candidature, changement),
      (fiche) => `Dernier changement corrigé : retour à ${LIBELLES_DES_STATUTS[fiche.statut]}.`,
      "Le changement n'a pas pu être corrigé.",
    );

  const demanderLaSuppression = () => {
    setMessage(undefined);
    setAnnonceDeDepart(undefined);
    setSuppression("pret");
    setConfirmation("ouverte");
  };

  const annulerLaSuppression = () => {
    if (!enCoursDAction.current) {
      setConfirmation("annulee");
    }
  };

  // Supprime la candidature, une fois la suppression confirmée : la fiche passe la main au tableau de bord.
  async function supprimer(supprimee: Fiche) {
    if (enCoursDAction.current) {
      return;
    }

    enCoursDAction.current = true;
    setEnCours(true);
    setSuppression("en-cours");
    try {
      await client.supprimerUneCandidature(candidature);
      if (affichee.current) {
        quandSupprimee(supprimee);
      }
    } catch (erreur) {
      if (!affichee.current) {
        return;
      }

      if (estIntrouvable(erreur)) {
        setChargement({ etape: "introuvable" });
      } else {
        setSuppression("echec");
      }
    } finally {
      enCoursDAction.current = false;
      if (affichee.current) {
        setEnCours(false);
      }
    }
  }

  const fiche = chargement.etape === "prete" ? chargement.fiche : undefined;
  const correction = fiche?.correction ?? null;

  return (
    <section aria-labelledby="titre-de-la-fiche" className="fiche">
      <h2 id="titre-de-la-fiche" ref={titre} tabIndex={-1}>
        Fiche de la candidature
      </h2>
      {annonceDeDepart !== undefined && chargement.etape === "prete" && (
        <p className="annonce" role="status">
          {annonceDeDepart}
        </p>
      )}
      <p>
        <button type="button" className="lien" onClick={revenir}>
          Revenir au tableau de bord
        </button>
      </p>
      {chargement.etape === "chargement" && <p>Chargement de la fiche…</p>}
      {chargement.etape === "introuvable" && (
        <p className="alerte" role="alert">
          Cette candidature n'existe pas, ou plus.
        </p>
      )}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          La fiche n'a pas pu être chargée. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {fiche !== undefined && (
        <>
          <dl className="resume">
            <dt>Entreprise</dt>
            <dd>{fiche.entreprise.nom}</dd>
            <dt>Poste</dt>
            <dd>{fiche.poste}</dd>
            <dt>Envoyée le</dt>
            <dd>
              <time dateTime={fiche.envoyeeLe}>{afficherLInstant(fiche.envoyeeLe)}</time>
            </dd>
            <dt>Statut</dt>
            <dd>{LIBELLES_DES_STATUTS[fiche.statut] ?? fiche.statut}</dd>
          </dl>
          <div className="actions-de-la-fiche">
            <button type="button" className="secondaire" disabled={enCours} onClick={modifier}>
              Modifier la candidature
            </button>
            <button
              type="button"
              ref={boutonSupprimer}
              className="secondaire danger"
              disabled={enCours}
              aria-expanded={confirmation === "ouverte"}
              aria-controls={confirmation === "ouverte" ? "confirmation-de-la-suppression" : undefined}
              onClick={demanderLaSuppression}
            >
              Supprimer la candidature
            </button>
          </div>
          {confirmation === "ouverte" && (
            <section
              id="confirmation-de-la-suppression"
              className="confirmation"
              aria-labelledby="titre-de-la-suppression"
              aria-describedby="texte-de-la-suppression"
              onKeyDown={(evenement) => {
                if (evenement.key === "Escape") {
                  annulerLaSuppression();
                }
              }}
            >
              <h3 id="titre-de-la-suppression" ref={titreDeLaConfirmation} tabIndex={-1}>
                Supprimer cette candidature ?
              </h3>
              <p id="texte-de-la-suppression">La suppression est définitive : elle emporte tout l'historique de la candidature.</p>
              <div className="actions">
                <button type="button" className="danger" disabled={enCours} onClick={() => void supprimer(fiche)}>
                  {suppression === "en-cours" ? "Suppression…" : "Supprimer définitivement"}
                </button>
                <button type="button" className="secondaire" disabled={enCours} onClick={annulerLaSuppression}>
                  Annuler
                </button>
              </div>
              {suppression === "echec" && (
                <p className="alerte" role="alert">
                  La candidature n'a pas pu être supprimée. Réessayez ; si l'erreur revient, fermez Sollicito, puis
                  relancez-le.
                </p>
              )}
            </section>
          )}
          <section aria-labelledby="titre-du-statut">
            <h3 id="titre-du-statut" ref={titreDuStatut} tabIndex={-1}>
              Faire avancer la candidature
            </h3>
            {fiche.passagesPermis.length > 0 ? (
              <div className="passages" role="group" aria-labelledby="passages-titre">
                <p id="passages-titre">Passer au statut :</p>
                <ul>
                  {fiche.passagesPermis.map((statut) => (
                    <li key={statut}>
                      <button type="button" disabled={enCours} onClick={() => void changerLeStatut(statut)}>
                        {LIBELLES_DES_STATUTS[statut] ?? statut}
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ) : (
              <p>La candidature est close : son statut ne change plus.</p>
            )}
            {correction !== null && (
              <div className="correction">
                <button type="button" className="secondaire" disabled={enCours} onClick={() => void corriger(correction.changement)}>
                  Corriger le dernier changement : revenir à {LIBELLES_DES_STATUTS[correction.retourA]}
                </button>
                <p className="aide">Le changement corrigé reste dans l'historique, marqué comme corrigé.</p>
              </div>
            )}
            {message?.sorte === "annonce" && (
              <p className="annonce" role="status">
                {message.texte}
              </p>
            )}
            {message?.sorte === "alerte" && (
              <p className="alerte" role="alert">
                {message.texte}
              </p>
            )}
          </section>
          <NoterUneEtape
            client={client}
            candidature={candidature}
            maintenant={maintenant}
            autresActions={actionsTerminees}
            quandNotee={(fiche) => {
              setMessage(undefined);
              setAnnonceDeDepart(undefined);
              montrer(fiche);
            }}
          />
          <Historique envoyeeLe={fiche.envoyeeLe} evenements={fiche.historique} />
        </>
      )}
    </section>
  );
}

/** L'historique, du plus ancien au plus récent, dans l'ordre que rend l'API ; il commence à l'envoi. */
function Historique({ envoyeeLe, evenements }: { envoyeeLe: string; evenements: EvenementDeLHistorique[] }) {
  return (
    <section aria-labelledby="titre-de-l-historique">
      <h3 id="titre-de-l-historique">Historique</h3>
      <ol className="historique">
        <li>
          <time dateTime={envoyeeLe}>{afficherLInstant(envoyeeLe)}</time>
          <span>Candidature envoyée</span>
        </li>
        {evenements.map((evenement) => (
          <li key={evenement.id} className={evenement.sorte === "etape" ? "etape" : undefined}>
            <time dateTime={evenement.survenuLe}>{afficherLInstant(evenement.survenuLe)}</time>
            <span>{decrireLEvenement(evenement)}</span>
            {evenement.sorte === "changementDeStatut" && evenement.estCorrige && <span className="marque">corrigé</span>}
            {evenement.sorte === "etape" && evenement.note !== null && <p className="note">{evenement.note}</p>}
          </li>
        ))}
      </ol>
    </section>
  );
}

function estIntrouvable(erreur: unknown): boolean {
  return erreur instanceof ErreurDeLApi && erreur.statut === 404;
}
