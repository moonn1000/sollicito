import { useEffect, useRef, useState, type FormEvent } from "react";
import { estIntrouvable, type Candidature, type ClientDeLApi, type FicheDUneOffre } from "../api/clientDeLApi.ts";
import { heureSaisieDe, LIBELLES_DES_STATUTS } from "../candidatures/affichage.ts";
import { Champ } from "../formulaires/Champ.tsx";
import { decrireLeDoublon } from "./affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "prete"; fiche: FicheDUneOffre } | { etape: "introuvable" } | { etape: "echec" };

/** Ce qui arrête « Postuler », en plus d'une date refusée : l'état de l'offre, ou un envoi qui échoue. */
type Arret =
  | { sorte: "dejaPostulee"; candidature: number }
  | { sorte: "entrepriseManquante" }
  | { sorte: "posteARevoir" }
  | { sorte: "echec" };

/** Les messages des refus que l'état de l'offre explique, ceux de l'API. */
const MESSAGE_DEJA_POSTULEE = "Cette offre a déjà sa candidature : rien n'a été ajouté.";
const MESSAGE_ENTREPRISE_MANQUANTE = "Indiquez d'abord l'entreprise de l'offre : la candidature la reprend.";
const MESSAGE_POSTE_A_REVOIR = "Le poste de l'offre ne peut pas être repris tel quel : corrigez l'offre, puis postulez.";

/**
 * L'écran « Postuler à cette offre » (#38, ADR-0014) : il rappelle le poste, l'entreprise et le lieu de l'offre, et
 * demande la seule date et heure d'envoi, préremplie à l'instant présent ; la candidature part d'Envoyée. L'API reprend
 * l'entreprise et le poste de l'offre, lus en base : l'écran ne les envoie pas. Un doublon possible déjà postulé se
 * montre avant l'envoi, sans bloquer. Une offre qui a déjà sa candidature, ou qui n'a pas d'entreprise, le dit, et mène
 * à la candidature, ou à la correction de l'offre.
 */
export function PostulerAUneOffre({
  client,
  offre,
  revenir,
  quandEnregistree,
  ouvrirLaCandidature,
  indiquerLEntreprise,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  offre: number;
  /** Ramène à la fiche de l'offre, sans rien enregistrer. */
  revenir: () => void;
  /** Reçoit la candidature que l'API vient d'enregistrer. */
  quandEnregistree: (candidature: Candidature) => void;
  /** Ouvre la fiche de la candidature de cet identifiant. */
  ouvrirLaCandidature: (candidature: number) => void;
  /** Ouvre la correction de l'offre, pour lui donner son entreprise. */
  indiquerLEntreprise: () => void;
  /** L'heure présente, qui remplit d'avance la date et l'heure d'envoi. */
  maintenant?: () => Date;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [envoyeeLe, setEnvoyeeLe] = useState(() => heureSaisieDe(maintenant()));
  const [refus, setRefus] = useState<string[]>();
  const [refusRecus, setRefusRecus] = useState(0);
  const [arret, setArret] = useState<Arret>();
  const [enCours, setEnCours] = useState(false);
  // Lu aussitôt, sans attendre le rendu : deux envois rapprochés n'en font qu'un.
  const envoiEnCours = useRef(false);
  const titre = useRef<HTMLHeadingElement>(null);
  const champ = useRef<HTMLInputElement>(null);
  const titreDeLArret = useRef<HTMLParagraphElement>(null);

  useEffect(() => {
    titre.current?.focus();
  }, [chargement.etape]);

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

  // Une réponse qui porte un refus de la date lui rend le focus ; un arrêt prend le focus sur son message.
  useEffect(() => {
    if (refusRecus > 0) {
      champ.current?.focus();
    }
  }, [refusRecus]);

  useEffect(() => {
    if (arret !== undefined) {
      titreDeLArret.current?.focus();
    }
  }, [arret]);

  async function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault();
    if (envoiEnCours.current) {
      return;
    }

    envoiEnCours.current = true;
    setEnCours(true);
    setArret(undefined);
    try {
      const resultat = await client.postuler(offre, envoyeeLe);
      switch (resultat.issue) {
        case "enregistree":
          quandEnregistree(resultat.candidature);
          return;
        case "refusee":
          setRefus(resultat.refus);
          setRefusRecus((nombre) => nombre + 1);
          break;
        case "dejaPostulee":
          setRefus(undefined);
          setArret({ sorte: "dejaPostulee", candidature: resultat.candidature });
          break;
        case "entrepriseManquante":
        case "posteARevoir":
          setRefus(undefined);
          setArret({ sorte: resultat.issue });
          break;
      }
    } catch (erreur) {
      setRefus(undefined);
      if (estIntrouvable(erreur)) {
        // Disparue entre-temps : il n'y a plus d'offre à qui postuler, l'écran le dit à la place du formulaire.
        setChargement({ etape: "introuvable" });
      } else {
        setArret({ sorte: "echec" });
      }
    } finally {
      envoiEnCours.current = false;
      setEnCours(false);
    }
  }

  if (chargement.etape !== "prete") {
    return (
      <section aria-labelledby="titre-de-la-postulation">
        <h2 id="titre-de-la-postulation" ref={titre} tabIndex={-1}>
          Postuler à cette offre
        </h2>
        {chargement.etape === "chargement" && <p role="status">Chargement de l'offre…</p>}
        {chargement.etape === "introuvable" && (
          <p className="alerte" role="alert">
            Cette offre n'existe pas, ou plus.
          </p>
        )}
        {chargement.etape === "echec" && (
          <p className="alerte" role="alert">
            L'offre n'a pas pu être lue : fermez Sollicito, puis relancez-le.
          </p>
        )}
        <div className="actions">
          <button type="button" className="secondaire" onClick={revenir}>
            Annuler
          </button>
        </div>
      </section>
    );
  }

  const { fiche } = chargement;
  const dejaPostulees = fiche.doublonsPossibles.filter((doublon) => doublon.candidature !== null);
  // L'état lu avec la fiche, ou celui qu'une réponse de l'API vient de dire.
  const candidatureConnue = arret?.sorte === "dejaPostulee" ? arret.candidature : fiche.candidature?.id;
  const sansEntreprise = arret?.sorte === "entrepriseManquante" || (fiche.entreprise === null && candidatureConnue === undefined);
  return (
    <section aria-labelledby="titre-de-la-postulation" className="postuler">
      <h2 id="titre-de-la-postulation" ref={titre} tabIndex={-1}>
        Postuler à cette offre
      </h2>
      <dl className="resume">
        <dt>Poste</dt>
        <dd>{fiche.poste}</dd>
        <dt>Entreprise</dt>
        <dd>{fiche.entreprise?.nom ?? "Entreprise non indiquée"}</dd>
        {fiche.lieu !== null && (
          <>
            <dt>Lieu</dt>
            <dd>{fiche.lieu}</dd>
          </>
        )}
      </dl>
      {dejaPostulees.length > 0 && (
        <section aria-labelledby="titre-des-offres-semblables" className="doublons-possibles">
          <h3 id="titre-des-offres-semblables">Vous avez déjà postulé à une offre semblable :</h3>
          <ul>
            {dejaPostulees.map((doublon) => (
              <li key={doublon.id}>{decrireLeDoublon(doublon)}</li>
            ))}
          </ul>
        </section>
      )}
      {candidatureConnue !== undefined ? (
        <div className="signal-de-l-offre" role="alert">
          <p ref={titreDeLArret} tabIndex={-1}>
            {MESSAGE_DEJA_POSTULEE}
          </p>
          <div className="actions">
            <button type="button" onClick={() => ouvrirLaCandidature(candidatureConnue)}>
              Voir la candidature
            </button>
            <button type="button" className="secondaire" onClick={revenir}>
              Annuler
            </button>
          </div>
        </div>
      ) : sansEntreprise ? (
        <div className="signal-de-l-offre" role="alert">
          <p ref={titreDeLArret} tabIndex={-1}>
            {MESSAGE_ENTREPRISE_MANQUANTE}
          </p>
          <div className="actions">
            <button type="button" onClick={indiquerLEntreprise}>
              Indiquer l'entreprise
            </button>
            <button type="button" className="secondaire" onClick={revenir}>
              Annuler
            </button>
          </div>
        </div>
      ) : (
        <form className="saisie" noValidate onSubmit={soumettre}>
          <Champ id="postuler-envoyeeLe" libelle="Date et heure d'envoi" refus={refus}>
            {(attributs) => (
              <input
                {...attributs}
                ref={champ}
                type="datetime-local"
                value={envoyeeLe}
                onChange={(evenement) => {
                  setEnvoyeeLe(evenement.target.value);
                  setRefus(undefined);
                }}
              />
            )}
          </Champ>
          <p className="statut-de-depart">
            Statut de départ : <strong>{LIBELLES_DES_STATUTS.Envoyee}</strong>
          </p>
          {arret?.sorte === "posteARevoir" && (
            <p className="alerte" role="alert" ref={titreDeLArret} tabIndex={-1}>
              {MESSAGE_POSTE_A_REVOIR}
            </p>
          )}
          <div className="actions">
            <button type="submit" disabled={enCours}>
              {enCours ? "Enregistrement…" : "Enregistrer la candidature"}
            </button>
            <button type="button" className="secondaire" onClick={revenir}>
              Annuler
            </button>
          </div>
          {arret?.sorte === "echec" && (
            <p className="alerte" role="alert" ref={titreDeLArret} tabIndex={-1}>
              La candidature n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.
            </p>
          )}
        </form>
      )}
    </section>
  );
}
