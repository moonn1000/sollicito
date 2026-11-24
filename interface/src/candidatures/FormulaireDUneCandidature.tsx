import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import type {
  ChampDeLaSaisie,
  ClientDeLApi,
  EntrepriseConnue,
  RefusDeLaSaisie,
  SaisieDUneCandidature,
} from "../api/clientDeLApi.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";
import { proposerDesEntreprises } from "./affichage.ts";

type Envoi = "pret" | "en-cours" | "echec";

const ORDRE_DES_CHAMPS: readonly ChampDeLaSaisie[] = ["entreprise", "poste", "envoyeeLe"];

/** Ce que rend l'envoi du formulaire : accepté, l'écran qui l'a ouvert passe à la suite ; ou les refus de ses champs. */
export type ReponseAuFormulaire = { acceptee: true } | { acceptee: false; refus: RefusDeLaSaisie };

/**
 * Les champs d'une candidature, tels que sa saisie (#11) et sa modification (#25) les présentent : entreprise, poste,
 * date et heure d'envoi. L'API valide chaque champ : le formulaire envoie la saisie telle quelle et affiche ses refus à
 * côté du champ concerné. Un refus disparaît dès qu'on modifie son champ, ou quand l'envoi suivant échoue : il ne
 * décrit plus ce qui est saisi (#80, point 3). Seule une réponse qui porte des refus déplace le focus, vers le premier
 * champ refusé. Pendant qu'on tape le nom de l'entreprise, il propose celles déjà enregistrées (ADR-0005).
 */
export function FormulaireDUneCandidature({
  client,
  depart,
  envoyer,
  libelleDuBouton,
  libelleEnCours,
  messageDEchec,
  aideDeLEnvoi,
  annuler,
  quandModifie,
  children,
}: {
  client: ClientDeLApi;
  /** Les valeurs qui remplissent d'avance les champs. */
  depart: SaisieDUneCandidature;
  /** Envoie la saisie à l'API ; une erreur levée se signale par `messageDEchec`. */
  envoyer: (saisie: SaisieDUneCandidature) => Promise<ReponseAuFormulaire>;
  libelleDuBouton: string;
  libelleEnCours: string;
  /** Le message d'un envoi qui échoue. */
  messageDEchec: string;
  /** Ce qu'il faut savoir de la date et de l'heure d'envoi avant de les changer. */
  aideDeLEnvoi?: string;
  /** Quitte le formulaire sans rien envoyer ; sans elle, pas de bouton « Annuler ». */
  annuler?: () => void;
  /** Prévenu à chaque champ modifié. */
  quandModifie?: () => void;
  /** Ce qui s'affiche après les champs, avant les boutons. */
  children?: ReactNode;
}) {
  const [entreprise, setEntreprise] = useState(depart.entreprise);
  const [poste, setPoste] = useState(depart.poste);
  const [envoyeeLe, setEnvoyeeLe] = useState(depart.envoyeeLe);
  const [refus, setRefus] = useState<RefusDeLaSaisie>({});
  // Compte les réponses de l'API qui portent des refus : chacune, et elle seule, déplace le focus.
  const [refusRecus, setRefusRecus] = useState(0);
  const [envoi, setEnvoi] = useState<Envoi>("pret");
  const [connues, setConnues] = useState<EntrepriseConnue[]>([]);
  // Lu aussitôt, sans attendre le rendu : deux envois rapprochés n'en font qu'un.
  const enCours = useRef(false);
  const champs = {
    entreprise: useRef<HTMLInputElement>(null),
    poste: useRef<HTMLInputElement>(null),
    envoyeeLe: useRef<HTMLInputElement>(null),
  };

  useEffect(() => {
    let affiche = true;
    // Les propositions ne sont qu'une aide : si elles ne viennent pas, la saisie reste possible.
    client.listerLesEntreprises().then(
      (entreprises) => {
        if (affiche) {
          setConnues(entreprises);
        }
      },
      () => undefined,
    );
    return () => {
      affiche = false;
    };
  }, [client]);

  // Après une réponse qui porte des refus, le premier champ refusé reçoit le focus. Effacer le refus d'un champ qu'on
  // corrige ne le déplace pas : seules les réponses de l'API le font.
  useEffect(() => {
    const premier = ORDRE_DES_CHAMPS.find((champ) => (refus[champ]?.length ?? 0) > 0);
    if (premier !== undefined) {
      champs[premier].current?.focus();
    }
  }, [refusRecus]);

  // Modifier un champ efface ses refus, qui portaient sur l'ancienne valeur.
  const modifier = (champ: ChampDeLaSaisie, changer: (valeur: string) => void) => (valeur: string) => {
    changer(valeur);
    quandModifie?.();
    setRefus((avant) => sansLeChamp(avant, champ));
  };

  const propositions = useMemo(() => proposerDesEntreprises(connues, entreprise), [connues, entreprise]);

  async function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault();
    if (enCours.current) {
      return;
    }

    enCours.current = true;
    setEnvoi("en-cours");
    try {
      const reponse = await envoyer({ entreprise, poste, envoyeeLe });
      if (reponse.acceptee) {
        return;
      }

      setRefus(reponse.refus);
      setRefusRecus((nombre) => nombre + 1);
      setEnvoi("pret");
    } catch {
      // Les refus venaient d'un envoi précédent : ils ne disent rien de celui qui vient d'échouer.
      setRefus({});
      setEnvoi("echec");
    } finally {
      enCours.current = false;
    }
  }

  return (
    <form className="saisie" noValidate onSubmit={soumettre}>
      <Champ id="entreprise" libelle="Entreprise" refus={refus.entreprise}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champs.entreprise}
            type="text"
            autoComplete="off"
            value={entreprise}
            onChange={(evenement) => modifier("entreprise", setEntreprise)(evenement.target.value)}
          />
        )}
      </Champ>
      {propositions.length > 0 && (
        <div className="propositions">
          <p id="propositions-titre">Entreprises déjà enregistrées :</p>
          <ul aria-labelledby="propositions-titre">
            {propositions.map((proposition) => (
              <li key={proposition.id}>
                <button type="button" onClick={() => modifier("entreprise", setEntreprise)(proposition.nom)}>
                  {proposition.nom}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
      <Champ id="poste" libelle="Poste" refus={refus.poste}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champs.poste}
            type="text"
            autoComplete="off"
            value={poste}
            onChange={(evenement) => modifier("poste", setPoste)(evenement.target.value)}
          />
        )}
      </Champ>
      <Champ id="envoyeeLe" libelle="Date et heure d'envoi" aide={aideDeLEnvoi} refus={refus.envoyeeLe}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champs.envoyeeLe}
            type="datetime-local"
            value={envoyeeLe}
            onChange={(evenement) => modifier("envoyeeLe", setEnvoyeeLe)(evenement.target.value)}
          />
        )}
      </Champ>
      {children}
      <div className="actions">
        <button type="submit" disabled={envoi === "en-cours"}>
          {envoi === "en-cours" ? libelleEnCours : libelleDuBouton}
        </button>
        {annuler !== undefined && (
          <button type="button" className="secondaire" disabled={envoi === "en-cours"} onClick={annuler}>
            Annuler
          </button>
        )}
      </div>
      {envoi === "echec" && (
        <p className="alerte" role="alert">
          {messageDEchec}
        </p>
      )}
    </form>
  );
}
