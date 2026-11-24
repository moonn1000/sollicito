import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import type { ChampDeLEtape, RefusDeLEtape, SaisieDUneEtape, TypeDEtape } from "../api/clientDeLApi.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";
import { LIBELLES_DES_TYPES_D_ETAPE } from "./affichage.ts";

type Envoi = "pret" | "en-cours" | "echec";

const ORDRE_DES_CHAMPS: readonly ChampDeLEtape[] = ["type", "survenuLe", "note"];

const TYPES = Object.keys(LIBELLES_DES_TYPES_D_ETAPE) as TypeDEtape[];

/**
 * Ce que rend l'envoi du formulaire : accepté, et, pour reprendre une saisie neuve, les valeurs d'où repartir ; ou les
 * refus de ses champs.
 */
export type ReponseALEtape = { acceptee: true; repartirDe?: SaisieDUneEtape } | { acceptee: false; refus: RefusDeLEtape };

/**
 * Les champs d'une étape, tels que la note d'une étape (#12) et sa correction (#25) les présentent : un type, une date
 * et une heure, une note facultative. Comme l'écran de saisie, le formulaire envoie les champs tels quels et affiche
 * les refus de l'API à côté de chacun ; un refus disparaît dès qu'on modifie son champ, ou quand l'envoi suivant
 * échoue. Seule une réponse qui porte des refus déplace le focus, vers le premier champ refusé.
 */
export function FormulaireDUneEtape({
  prefixe,
  depart,
  envoyer,
  occupe = false,
  libelleDuBouton,
  libelleEnCours,
  messageDEchec,
  aide,
  annuler,
  quandModifie,
  premierChamp,
  children,
}: {
  /** Le début des identifiants des champs : deux formulaires d'une même page n'en partagent aucun. */
  prefixe: string;
  /** Les valeurs qui remplissent d'avance les champs. */
  depart: SaisieDUneEtape;
  /** Envoie la saisie à l'API ; une erreur levée se signale par `messageDEchec`. */
  envoyer: (saisie: SaisieDUneEtape) => Promise<ReponseALEtape>;
  /** Vrai tant qu'une autre action tient l'écran : l'envoi est alors inactif (la fiche n'en admet qu'une à la fois). */
  occupe?: boolean;
  libelleDuBouton: string;
  libelleEnCours: string;
  /** Le message d'un envoi qui échoue. */
  messageDEchec: string;
  /** Ce qu'il faut savoir avant d'envoyer. */
  aide: string;
  /** Quitte le formulaire sans rien envoyer ; sans elle, pas de bouton « Annuler ». */
  annuler?: () => void;
  /** Prévenu à chaque champ modifié. */
  quandModifie?: () => void;
  /** Reçoit le premier champ, pour y mettre le focus à l'ouverture. */
  premierChamp?: (champ: HTMLSelectElement | null) => void;
  /** Ce qui s'affiche après les boutons, par exemple une annonce. */
  children?: ReactNode;
}) {
  const [type, setType] = useState<TypeDEtape>(depart.type);
  const [survenuLe, setSurvenuLe] = useState(depart.survenuLe);
  const [note, setNote] = useState(depart.note);
  const [refus, setRefus] = useState<RefusDeLEtape>({});
  // Compte les réponses de l'API qui portent des refus : chacune, et elle seule, déplace le focus.
  const [refusRecus, setRefusRecus] = useState(0);
  const [envoi, setEnvoi] = useState<Envoi>("pret");
  const enCours = useRef(false);
  const champs = {
    type: useRef<HTMLSelectElement>(null),
    survenuLe: useRef<HTMLInputElement>(null),
    note: useRef<HTMLTextAreaElement>(null),
  };

  useEffect(() => {
    premierChamp?.(champs.type.current);
  }, []);

  useEffect(() => {
    const premier = ORDRE_DES_CHAMPS.find((champ) => (refus[champ]?.length ?? 0) > 0);
    if (premier !== undefined) {
      champs[premier].current?.focus();
    }
  }, [refusRecus]);

  // Modifier un champ efface ses refus, qui portaient sur l'ancienne valeur.
  const modifier = <Valeur,>(champ: ChampDeLEtape, changer: (valeur: Valeur) => void) => (valeur: Valeur) => {
    changer(valeur);
    quandModifie?.();
    setRefus((avant) => sansLeChamp(avant, champ));
  };

  async function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault();
    if (enCours.current) {
      return;
    }

    enCours.current = true;
    setEnvoi("en-cours");
    try {
      const reponse = await envoyer({ type, survenuLe, note });
      if (reponse.acceptee) {
        if (reponse.repartirDe !== undefined) {
          setType(reponse.repartirDe.type);
          setSurvenuLe(reponse.repartirDe.survenuLe);
          setNote(reponse.repartirDe.note);
          setRefus({});
          setEnvoi("pret");
        }

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
      <Champ id={`${prefixe}-type`} libelle="Type" refus={refus.type}>
        {(attributs) => (
          <select
            {...attributs}
            ref={champs.type}
            value={type}
            onChange={(evenement) => modifier("type", setType)(evenement.target.value as TypeDEtape)}
          >
            {TYPES.map((valeur) => (
              <option key={valeur} value={valeur}>
                {LIBELLES_DES_TYPES_D_ETAPE[valeur]}
              </option>
            ))}
          </select>
        )}
      </Champ>
      <Champ id={`${prefixe}-survenuLe`} libelle="Date et heure" refus={refus.survenuLe}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champs.survenuLe}
            type="datetime-local"
            value={survenuLe}
            onChange={(evenement) => modifier("survenuLe", setSurvenuLe)(evenement.target.value)}
          />
        )}
      </Champ>
      <Champ id={`${prefixe}-note`} libelle="Note (facultative)" refus={refus.note}>
        {(attributs) => (
          <textarea
            {...attributs}
            ref={champs.note}
            rows={4}
            value={note}
            onChange={(evenement) => modifier("note", setNote)(evenement.target.value)}
          />
        )}
      </Champ>
      <p className="aide">{aide}</p>
      <div className="actions">
        <button type="submit" disabled={envoi === "en-cours" || occupe}>
          {envoi === "en-cours" ? libelleEnCours : libelleDuBouton}
        </button>
        {annuler !== undefined && (
          <button type="button" className="secondaire" disabled={envoi === "en-cours"} onClick={annuler}>
            Annuler
          </button>
        )}
      </div>
      {children}
      {envoi === "echec" && (
        <p className="alerte" role="alert">
          {messageDEchec}
        </p>
      )}
    </form>
  );
}
