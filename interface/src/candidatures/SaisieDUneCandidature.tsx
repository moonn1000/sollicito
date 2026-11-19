import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import type {
  Candidature,
  ChampDeLaSaisie,
  ClientDeLApi,
  EntrepriseConnue,
  RefusDeLaSaisie,
} from "../api/clientDeLApi.ts";
import { heureSaisieDe, LIBELLES_DES_STATUTS, proposerDesEntreprises } from "./affichage.ts";

type Envoi = "pret" | "en-cours" | "echec";

const ORDRE_DES_CHAMPS: readonly ChampDeLaSaisie[] = ["entreprise", "poste", "envoyeeLe"];

/**
 * L'écran de saisie d'une candidature : entreprise, poste, date et heure d'envoi ; le statut de départ est toujours
 * Envoyée. L'API valide chaque champ : l'écran envoie la saisie telle quelle et affiche ses refus à côté du champ
 * concerné. Pendant qu'on tape le nom de l'entreprise, il propose celles déjà enregistrées (ADR-0005).
 */
export function SaisieDUneCandidature({
  client,
  quandEnregistree,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  quandEnregistree: (candidature: Candidature) => void;
  /** L'heure présente, qui remplit d'avance la date et l'heure d'envoi. */
  maintenant?: () => Date;
}) {
  const [entreprise, setEntreprise] = useState("");
  const [poste, setPoste] = useState("");
  const [envoyeeLe, setEnvoyeeLe] = useState(() => heureSaisieDe(maintenant()));
  const [refus, setRefus] = useState<RefusDeLaSaisie>({});
  const [envoi, setEnvoi] = useState<Envoi>("pret");
  const [connues, setConnues] = useState<EntrepriseConnue[]>([]);
  const titre = useRef<HTMLHeadingElement>(null);
  const champs = {
    entreprise: useRef<HTMLInputElement>(null),
    poste: useRef<HTMLInputElement>(null),
    envoyeeLe: useRef<HTMLInputElement>(null),
  };

  useEffect(() => {
    titre.current?.focus();
  }, []);

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

  // Après un refus, le premier champ refusé reçoit le focus. Les références des champs ne changent pas d'un rendu à
  // l'autre : seuls les refus déclenchent ce déplacement.
  useEffect(() => {
    const premier = ORDRE_DES_CHAMPS.find((champ) => (refus[champ]?.length ?? 0) > 0);
    if (premier !== undefined) {
      champs[premier].current?.focus();
    }
  }, [refus]);

  const propositions = useMemo(() => proposerDesEntreprises(connues, entreprise), [connues, entreprise]);

  async function enregistrer(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault();
    if (envoi === "en-cours") {
      return;
    }

    setEnvoi("en-cours");
    try {
      const resultat = await client.enregistrerUneCandidature({ entreprise, poste, envoyeeLe });
      if (resultat.enregistree) {
        quandEnregistree(resultat.candidature);
        return;
      }

      setRefus(resultat.refus);
      setEnvoi("pret");
    } catch {
      setEnvoi("echec");
    }
  }

  return (
    <section aria-labelledby="titre-de-la-saisie">
      <h2 id="titre-de-la-saisie" ref={titre} tabIndex={-1}>
        Nouvelle candidature
      </h2>
      <form className="saisie" noValidate onSubmit={enregistrer}>
        <Champ id="entreprise" libelle="Entreprise" refus={refus.entreprise}>
          {(attributs) => (
            <input
              {...attributs}
              ref={champs.entreprise}
              type="text"
              autoComplete="off"
              value={entreprise}
              onChange={(evenement) => setEntreprise(evenement.target.value)}
            />
          )}
        </Champ>
        {propositions.length > 0 && (
          <div className="propositions">
            <p id="propositions-titre">Entreprises déjà enregistrées :</p>
            <ul aria-labelledby="propositions-titre">
              {propositions.map((proposition) => (
                <li key={proposition.id}>
                  <button type="button" onClick={() => setEntreprise(proposition.nom)}>
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
              onChange={(evenement) => setPoste(evenement.target.value)}
            />
          )}
        </Champ>
        <Champ id="envoyeeLe" libelle="Date et heure d'envoi" refus={refus.envoyeeLe}>
          {(attributs) => (
            <input
              {...attributs}
              ref={champs.envoyeeLe}
              type="datetime-local"
              value={envoyeeLe}
              onChange={(evenement) => setEnvoyeeLe(evenement.target.value)}
            />
          )}
        </Champ>
        <p className="statut-de-depart">
          Statut de départ : <strong>{LIBELLES_DES_STATUTS.Envoyee}</strong>
        </p>
        <div className="actions">
          <button type="submit" disabled={envoi === "en-cours"}>
            {envoi === "en-cours" ? "Enregistrement…" : "Enregistrer la candidature"}
          </button>
        </div>
        {envoi === "echec" && (
          <p className="alerte" role="alert">
            La candidature n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis
            relancez-le.
          </p>
        )}
      </form>
    </section>
  );
}

/** Un champ, son libellé et ses refus, que le champ désigne comme sa description. */
function Champ({
  id,
  libelle,
  refus,
  children,
}: {
  id: ChampDeLaSaisie;
  libelle: string;
  refus: string[] | undefined;
  children: (attributs: { id: string; name: string; "aria-invalid"?: true; "aria-describedby"?: string }) => ReactNode;
}) {
  const refuse = refus !== undefined && refus.length > 0;
  const idDuRefus = `${id}-refus`;
  return (
    <div className={refuse ? "champ champ-refuse" : "champ"}>
      <label htmlFor={id}>{libelle}</label>
      {children({
        id,
        name: id,
        ...(refuse ? { "aria-invalid": true, "aria-describedby": idDuRefus } : {}),
      })}
      {refuse && (
        <p className="refus" id={idDuRefus}>
          {refus.join(" ")}
        </p>
      )}
    </div>
  );
}
