import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import type {
  ChampDeLOffre,
  ChampsProposes,
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
import { AIDE_DU_TEXTE, decrireLeDoublon, MARQUE_DE_L_IA, MESSAGE_DU_DOUBLON, messageDejaEnregistree } from "./affichage.ts";
import { ApercuDUneOffre } from "./ApercuDUneOffre.tsx";
import { PropositionDeLIA } from "./PropositionDeLIA.tsx";

type Envoi = "pret" | "en-cours" | "echec";

/** Ce qui arrête l'envoi, en plus des refus : le même texte déjà enregistré, ou des doublons possibles. */
type Conflit = { sorte: "dejaEnregistree"; id: number; ajouteeLe: string } | { sorte: "doublonPossible"; doublons: OffreSignalee[] };

/** Un champ que l'IA peut proposer : tous, sauf le texte. */
type ChampProposable = Exclude<ChampDeLOffre, "texte">;

const ORDRE_DES_CHAMPS: readonly ChampDeLOffre[] = ["texte", "entreprise", "poste", "lieu", "publieeLe", "adresse"];

const CHAMPS_PROPOSABLES: readonly ChampProposable[] = ["entreprise", "poste", "lieu", "publieeLe", "adresse"];

/**
 * Les champs d'une offre, tels que sa saisie (#36) et sa correction les présentent : le texte collé, à la saisie
 * seulement, puis l'entreprise, le poste, le lieu, la date de publication et l'adresse de l'annonce. L'API valide
 * chaque champ : le formulaire envoie la saisie telle quelle et affiche ses refus à côté du champ concerné. Il dit
 * aussi, avant tout enregistrement, que le même texte est déjà enregistré (règle 2), ou que des offres de même
 * entreprise et de même poste le sont (règle 3) : l'utilisateur garde les deux, ou renonce. L'autre offre s'ouvre sur
 * place, dans un aperçu, sans quitter le formulaire : le texte collé et les champs restent (suites de #36, #98). Un
 * refus ou un signal disparaît dès qu'on modifie la saisie, qu'il ne décrit plus. Le texte n'a pas de longueur bornée à
 * l'écran : un collage trop long serait tronqué sans le dire ; l'API le refuse, et l'écran le dit. À la saisie, l'IA
 * peut proposer les champs, sur un clic (#46) : elle ne remplit que les champs vides, chacun marqué jusqu'à ce que
 * l'utilisateur le modifie, et rien ne s'enregistre avant l'envoi du formulaire.
 */
export function FormulaireDUneOffre({
  client,
  depart,
  avecLeTexte,
  proposerAvecLIA = false,
  envoyer,
  quandEnregistree,
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
  /** Vrai à la saisie : l'IA réglée peut proposer les champs à partir du texte, sur un clic (#46). */
  proposerAvecLIA?: boolean;
  /** Envoie la saisie à l'API, avec ou sans l'accord de garder un doublon possible ; une erreur levée se signale. */
  envoyer: (saisie: SaisieDUneOffre, garderLesDeux: boolean) => Promise<ResultatDeLOffre>;
  /** Reçoit la fiche de l'offre enregistrée. */
  quandEnregistree: (fiche: FicheDUneOffre) => void;
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
  // L'autre offre ouverte sur place, dans l'aperçu : celle du même texte, ou un doublon possible, et le signal qui l'a
  // montrée. Elle ne s'affiche que sous ce signal-là : un autre signal, ou aucun, la referme d'office, sans attendre un
  // effet qu'un clic rapide devancerait.
  const [apercu, setApercu] = useState<{ signal: Conflit; offre: number }>();
  const [envoi, setEnvoi] = useState<Envoi>("pret");
  const [connues, setConnues] = useState<EntrepriseConnue[]>([]);
  // Les valeurs que l'IA a mises dans des champs vides (#46) : un champ reste marqué tant qu'il garde la sienne.
  const [proposes, setProposes] = useState<Partial<Record<ChampProposable, string>>>({});
  // La saisie du dernier rendu, lue quand la proposition arrive : seuls les champs encore vides la reçoivent.
  const saisieCourante = useRef(saisie);
  useEffect(() => {
    saisieCourante.current = saisie;
  }, [saisie]);
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

  // Vrai si l'aperçu de cette offre est ouvert sous le signal affiché.
  const ouverte = (offre: number) => apercu !== undefined && apercu.signal === conflit && apercu.offre === offre;

  const propositions = useMemo(() => proposerDesEntreprises(connues, saisie.entreprise), [connues, saisie.entreprise]);

  // Modifier un champ efface ses refus et le signal, qui portaient sur l'ancienne saisie.
  const modifier = (champ: ChampDeLOffre) => (valeur: string) => {
    setSaisie((avant) => ({ ...avant, [champ]: valeur }));
    setRefus((avant) => sansLeChamp(avant, champ));
    setConflit(undefined);
  };

  // La proposition de l'IA, dans les seuls champs encore vides : rien de ce que l'utilisateur a saisi n'est remplacé.
  const appliquerLaProposition = (champs: ChampsProposes) => {
    const remplis: Partial<Record<ChampProposable, string>> = {};
    let dejaRemplis = 0;
    for (const champ of CHAMPS_PROPOSABLES) {
      const valeur = champs[champ];
      if (valeur === null) {
        continue;
      }

      if (saisieCourante.current[champ].trim() === "") {
        remplis[champ] = valeur;
      } else {
        dejaRemplis++;
      }
    }

    const nombre = Object.keys(remplis).length;
    if (nombre > 0) {
      setSaisie((avant) => {
        const apres = { ...avant };
        for (const champ of CHAMPS_PROPOSABLES) {
          const valeur = remplis[champ];
          if (valeur !== undefined && apres[champ].trim() === "") {
            apres[champ] = valeur;
          }
        }

        return apres;
      });
      setProposes((avant) => ({ ...avant, ...remplis }));
      setRefus((avant) => CHAMPS_PROPOSABLES.reduce((reste, champ) => (champ in remplis ? sansLeChamp(reste, champ) : reste), avant));
      setConflit(undefined);
    }

    return { remplis: nombre, dejaRemplis };
  };

  // Les champs proposés que l'utilisateur n'a pas modifiés.
  const champsMarques = CHAMPS_PROPOSABLES.filter((champ) => proposes[champ] !== undefined && saisie[champ] === proposes[champ]);

  // Retire la proposition : les champs proposés que l'utilisateur n'a pas modifiés redeviennent vides.
  const retirerLaProposition = () => {
    const aRetirer = proposes;
    setSaisie((avant) => {
      const apres = { ...avant };
      for (const champ of CHAMPS_PROPOSABLES) {
        if (aRetirer[champ] !== undefined && apres[champ] === aRetirer[champ]) {
          apres[champ] = "";
        }
      }

      return apres;
    });
    setProposes({});
    setConflit(undefined);
  };

  // Le texte refusé par l'API avant toute demande à l'IA : son refus, sous son champ, qui reçoit le focus.
  const quandLeTexteEstRefuse = (refusDuTexte: string[]) => {
    setConflit(undefined);
    setRefus((avant) => ({ ...avant, texte: refusDuTexte }));
    setRefusRecus((nombre) => nombre + 1);
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

  const champDeTexte = (champ: ChampProposable, libelle: string, type: "text" | "date" | "url", aide?: string) => (
    <Champ
      id={`offre-${champ}`}
      libelle={libelle}
      aide={aide}
      marque={champsMarques.includes(champ) ? MARQUE_DE_L_IA : undefined}
      refus={refus[champ]}
    >
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
      {avecLeTexte && proposerAvecLIA && (
        <PropositionDeLIA
          client={client}
          texte={saisie.texte}
          appliquer={appliquerLaProposition}
          quandTexteRefuse={quandLeTexteEstRefuse}
          retirer={champsMarques.length > 0 ? retirerLaProposition : undefined}
        />
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
      {/* Seul le titre d'un signal est une alerte : l'aperçu d'une autre offre, ouvert sous lui, jusqu'à 50 000 caractères,
          ne se lit pas à voix haute de lui-même (#98, point 20). */}
      {conflit?.sorte === "dejaEnregistree" && (
        <div className="signal-de-l-offre">
          <div role="alert">
            <h3 ref={titreDuConflit} tabIndex={-1}>
              {messageDejaEnregistree(conflit.ajouteeLe)}
            </h3>
          </div>
          {ouverte(conflit.id) ? (
            <ApercuDUneOffre client={client} offre={conflit.id} masquer={() => setApercu(undefined)} />
          ) : (
            <div className="actions">
              <button type="button" onClick={() => setApercu({ signal: conflit, offre: conflit.id })}>
                Voir l'offre enregistrée
              </button>
            </div>
          )}
        </div>
      )}
      {conflit?.sorte === "doublonPossible" && (
        <div className="signal-de-l-offre">
          <div role="alert">
            <h3 ref={titreDuConflit} tabIndex={-1}>
              {MESSAGE_DU_DOUBLON}
            </h3>
          </div>
          <ul>
            {conflit.doublons.map((doublon) => (
              <li key={doublon.id}>
                <span>{decrireLeDoublon(doublon)}</span>{" "}
                {ouverte(doublon.id) ? (
                  <ApercuDUneOffre client={client} offre={doublon.id} masquer={() => setApercu(undefined)} />
                ) : (
                  <button type="button" className="secondaire" onClick={() => setApercu({ signal: conflit, offre: doublon.id })}>
                    Voir cette offre
                  </button>
                )}
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
