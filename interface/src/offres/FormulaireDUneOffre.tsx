import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type {
  ChampDeLOffre,
  ClientDeLApi,
  EntrepriseConnue,
  FicheDUneOffre,
  OffreSignalee,
  RefusDeLOffre,
  ResultatDeLOffre,
  SaisieDUneOffre,
} from "../api/clientDeLApi.ts";
import { proposerDesEntreprises } from "../candidatures/affichage.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";
import { AIDE_DU_TEXTE, decrireLeDoublon, MESSAGE_DU_DOUBLON, messageDejaEnregistree } from "./affichage.ts";

type Envoi = "pret" | "en-cours" | "echec";

/** Ce qui arrête l'envoi, en plus des refus : le même texte déjà enregistré, ou des doublons possibles. */
type Conflit = { sorte: "dejaEnregistree"; id: number; ajouteeLe: string } | { sorte: "doublonPossible"; doublons: OffreSignalee[] };

const ORDRE_DES_CHAMPS: readonly ChampDeLOffre[] = ["texte", "entreprise", "poste", "lieu", "publieeLe", "adresse"];

/**
 * Les champs d'une offre, tels que sa saisie (#36) et sa correction les présentent : le texte collé, à la saisie
 * seulement, puis l'entreprise, le poste, le lieu, la date de publication et l'adresse de l'annonce. L'API valide
 * chaque champ : le formulaire envoie la saisie telle quelle et affiche ses refus à côté du champ concerné. Il dit
 * aussi, avant tout enregistrement, que le même texte est déjà enregistré (règle 2), ou que des offres de même
 * entreprise et de même poste le sont (règle 3) : l'utilisateur garde les deux, ou renonce. Un refus ou un signal
 * disparaît dès qu'on modifie la saisie, qu'il ne décrit plus. Le texte n'a pas de longueur bornée à l'écran : un
 * collage trop long serait tronqué sans le dire ; l'API le refuse, et l'écran le dit.
 */
export function FormulaireDUneOffre({
  client,
  depart,
  avecLeTexte,
  envoyer,
  quandEnregistree,
  ouvrirUneOffre,
  renoncer,
  libelleDuBouton,
  libelleEnCours,
  libelleDuRenoncement,
  messageDEchec,
  annuler,
}: {
  client: ClientDeLApi;
  /** Les valeurs qui remplissent d'avance les champs. */
  depart: SaisieDUneOffre;
  /** Vrai à la saisie : le texte se colle. La correction ne le montre pas, et ne l'envoie pas. */
  avecLeTexte: boolean;
  /** Envoie la saisie à l'API, avec ou sans l'accord de garder un doublon possible ; une erreur levée se signale. */
  envoyer: (saisie: SaisieDUneOffre, garderLesDeux: boolean) => Promise<ResultatDeLOffre>;
  /** Reçoit la fiche de l'offre enregistrée. */
  quandEnregistree: (fiche: FicheDUneOffre) => void;
  /** Ouvre la fiche d'une autre offre, celle du même texte ou un doublon possible. */
  ouvrirUneOffre: (offre: number) => void;
  /** Renonce, devant un doublon possible : rien n'est enregistré. */
  renoncer: () => void;
  libelleDuBouton: string;
  libelleEnCours: string;
  /** Le bouton qui renonce, devant un doublon possible. */
  libelleDuRenoncement: string;
  /** Le message d'un envoi qui échoue. */
  messageDEchec: string;
  /** Quitte le formulaire sans rien envoyer ; sans elle, pas de bouton « Annuler ». */
  annuler?: () => void;
}) {
  const [saisie, setSaisie] = useState<SaisieDUneOffre>(depart);
  const [refus, setRefus] = useState<RefusDeLOffre>({});
  const [refusRecus, setRefusRecus] = useState(0);
  const [conflit, setConflit] = useState<Conflit>();
  const [envoi, setEnvoi] = useState<Envoi>("pret");
  const [connues, setConnues] = useState<EntrepriseConnue[]>([]);
  // Lu aussitôt, sans attendre le rendu : deux envois rapprochés n'en font qu'un.
  const enCours = useRef(false);
  const titreDuConflit = useRef<HTMLHeadingElement>(null);
  const champs = {
    texte: useRef<HTMLTextAreaElement>(null),
    entreprise: useRef<HTMLInputElement>(null),
    poste: useRef<HTMLInputElement>(null),
    lieu: useRef<HTMLInputElement>(null),
    publieeLe: useRef<HTMLInputElement>(null),
    adresse: useRef<HTMLInputElement>(null),
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

  // Après une réponse qui porte des refus, le premier champ refusé reçoit le focus.
  useEffect(() => {
    const premier = ORDRE_DES_CHAMPS.find((champ) => (refus[champ]?.length ?? 0) > 0);
    if (premier !== undefined) {
      champs[premier].current?.focus();
    }
  }, [refusRecus]);

  // Un signal reçu prend le focus sur son titre : il dit pourquoi rien n'est enregistré.
  useEffect(() => {
    if (conflit !== undefined) {
      titreDuConflit.current?.focus();
    }
  }, [conflit]);

  const propositions = useMemo(() => proposerDesEntreprises(connues, saisie.entreprise), [connues, saisie.entreprise]);

  // Modifier un champ efface ses refus et le signal, qui portaient sur l'ancienne saisie.
  const modifier = (champ: ChampDeLOffre) => (valeur: string) => {
    setSaisie((avant) => ({ ...avant, [champ]: valeur }));
    setRefus((avant) => sansLeChamp(avant, champ));
    setConflit(undefined);
  };

  async function transmettre(garderLesDeux: boolean) {
    if (enCours.current) {
      return;
    }

    enCours.current = true;
    setEnvoi("en-cours");
    try {
      const resultat = await envoyer(saisie, garderLesDeux);
      switch (resultat.issue) {
        case "enregistree":
          quandEnregistree(resultat.fiche);
          return;
        case "refusee":
          setConflit(undefined);
          setRefus(resultat.refus);
          setRefusRecus((nombre) => nombre + 1);
          break;
        case "dejaEnregistree":
          setRefus({});
          setConflit({ sorte: "dejaEnregistree", id: resultat.offre.id, ajouteeLe: resultat.offre.ajouteeLe });
          break;
        case "doublonPossible":
          setRefus({});
          setConflit({ sorte: "doublonPossible", doublons: resultat.doublons });
          break;
      }

      setEnvoi("pret");
    } catch {
      // Les refus et le signal venaient d'un envoi précédent : ils ne disent rien de celui qui vient d'échouer.
      setRefus({});
      setConflit(undefined);
      setEnvoi("echec");
    } finally {
      enCours.current = false;
    }
  }

  function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault();
    void transmettre(false);
  }

  const champDeTexte = (champ: Exclude<ChampDeLOffre, "texte">, libelle: string, type: "text" | "date" | "url", aide?: string) => (
    <Champ id={`offre-${champ}`} libelle={libelle} aide={aide} refus={refus[champ]}>
      {(attributs) => (
        <input
          {...attributs}
          ref={champs[champ]}
          type={type}
          autoComplete="off"
          value={saisie[champ]}
          onChange={(evenement) => modifier(champ)(evenement.target.value)}
        />
      )}
    </Champ>
  );

  return (
    <form className="saisie saisie-d-une-offre" noValidate onSubmit={soumettre}>
      {avecLeTexte && (
        <Champ id="offre-texte" libelle="Texte de l'offre" aide={AIDE_DU_TEXTE} refus={refus.texte}>
          {(attributs) => (
            <textarea
              {...attributs}
              ref={champs.texte}
              rows={12}
              spellCheck={false}
              value={saisie.texte}
              onChange={(evenement) => modifier("texte")(evenement.target.value)}
            />
          )}
        </Champ>
      )}
      {champDeTexte("entreprise", "Entreprise", "text", "Facultative.")}
      {propositions.length > 0 && (
        <div className="propositions">
          <p id="propositions-de-l-offre">Entreprises déjà enregistrées :</p>
          <ul aria-labelledby="propositions-de-l-offre">
            {propositions.map((proposition) => (
              <li key={proposition.id}>
                <button type="button" onClick={() => modifier("entreprise")(proposition.nom)}>
                  {proposition.nom}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
      {champDeTexte("poste", "Poste", "text")}
      {champDeTexte("lieu", "Lieu", "text", "Facultatif.")}
      {champDeTexte("publieeLe", "Date de publication", "date", "Facultative.")}
      {champDeTexte("adresse", "Adresse de l'annonce", "url", "Facultative : elle commence par http:// ou https://.")}
      {conflit?.sorte === "dejaEnregistree" && (
        <div className="signal-de-l-offre" role="alert">
          <h3 ref={titreDuConflit} tabIndex={-1}>
            {messageDejaEnregistree(conflit.ajouteeLe)}
          </h3>
          <div className="actions">
            <button type="button" onClick={() => ouvrirUneOffre(conflit.id)}>
              Voir l'offre enregistrée
            </button>
          </div>
        </div>
      )}
      {conflit?.sorte === "doublonPossible" && (
        <div className="signal-de-l-offre" role="alert">
          <h3 ref={titreDuConflit} tabIndex={-1}>
            {MESSAGE_DU_DOUBLON}
          </h3>
          <ul>
            {conflit.doublons.map((doublon) => (
              <li key={doublon.id}>
                <span>{decrireLeDoublon(doublon)}</span>{" "}
                <button type="button" className="secondaire" onClick={() => ouvrirUneOffre(doublon.id)}>
                  Voir cette offre
                </button>
              </li>
            ))}
          </ul>
          <div className="actions">
            <button type="button" disabled={envoi === "en-cours"} onClick={() => void transmettre(true)}>
              Garder les deux
            </button>
            <button type="button" className="secondaire" onClick={renoncer}>
              {libelleDuRenoncement}
            </button>
          </div>
        </div>
      )}
      <div className="actions">
        <button type="submit" disabled={envoi === "en-cours"}>
          {envoi === "en-cours" ? libelleEnCours : libelleDuBouton}
        </button>
        {annuler !== undefined && (
          <button type="button" className="secondaire" onClick={annuler}>
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
