import { useEffect, useRef, useState, type FormEvent } from "react";
import type {
  ChampDeLEtape,
  ClientDeLApi,
  FicheDUneCandidature,
  RefusDeLEtape,
  TypeDEtape,
} from "../api/clientDeLApi.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";
import { heureSaisieDe, LIBELLES_DES_TYPES_D_ETAPE } from "./affichage.ts";

type Envoi = "pret" | "en-cours" | "echec";

const ORDRE_DES_CHAMPS: readonly ChampDeLEtape[] = ["type", "survenuLe", "note"];

const TYPES = Object.keys(LIBELLES_DES_TYPES_D_ETAPE) as TypeDEtape[];

/**
 * Noter un entretien ou un échange dans l'historique d'une candidature (#41, absorbée par #12) : un type, une date et
 * une heure, passées ou à venir, et une note facultative. L'étape ne change pas le statut. Comme l'écran de saisie,
 * le formulaire envoie les champs tels quels et affiche les refus de l'API à côté de chacun ; un refus disparaît dès
 * qu'on modifie son champ, ou quand l'envoi suivant échoue.
 */
export function NoterUneEtape({
  client,
  candidature,
  quandNotee,
  autresActions = 0,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  candidature: number;
  /** Reçoit la fiche à jour, l'étape dans son historique. */
  quandNotee: (fiche: FicheDUneCandidature) => void;
  /** Compte les autres actions de la fiche : chacune efface l'annonce de la dernière étape ajoutée, qui serait périmée. */
  autresActions?: number;
  /** L'heure présente, qui remplit d'avance la date et l'heure. */
  maintenant?: () => Date;
}) {
  const [type, setType] = useState<TypeDEtape>("Entretien");
  const [survenuLe, setSurvenuLe] = useState(() => heureSaisieDe(maintenant()));
  const [note, setNote] = useState("");
  const [refus, setRefus] = useState<RefusDeLEtape>({});
  // Compte les réponses de l'API qui portent des refus : chacune, et elle seule, déplace le focus.
  const [refusRecus, setRefusRecus] = useState(0);
  const [envoi, setEnvoi] = useState<Envoi>("pret");
  // L'annonce de la dernière étape ajoutée, et le nombre d'autres actions de la fiche à ce moment : elle ne s'affiche
  // que tant qu'aucune autre action n'a suivi.
  const [annonce, setAnnonce] = useState<{ texte: string; apres: number }>();
  const enCours = useRef(false);
  const champs = {
    type: useRef<HTMLSelectElement>(null),
    survenuLe: useRef<HTMLInputElement>(null),
    note: useRef<HTMLTextAreaElement>(null),
  };

  useEffect(() => {
    const premier = ORDRE_DES_CHAMPS.find((champ) => (refus[champ]?.length ?? 0) > 0);
    if (premier !== undefined) {
      champs[premier].current?.focus();
    }
  }, [refusRecus]);

  // Modifier un champ efface ses refus, qui portaient sur l'ancienne valeur.
  const modifier = <Valeur,>(champ: ChampDeLEtape, changer: (valeur: Valeur) => void) => (valeur: Valeur) => {
    changer(valeur);
    setAnnonce(undefined);
    setRefus((avant) => sansLeChamp(avant, champ));
  };

  async function noter(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault();
    if (enCours.current) {
      return;
    }

    enCours.current = true;
    setEnvoi("en-cours");
    setAnnonce(undefined);
    try {
      const resultat = await client.noterUneEtape(candidature, { type, survenuLe, note });
      if (resultat.notee) {
        setRefus({});
        setNote("");
        setSurvenuLe(heureSaisieDe(maintenant()));
        setEnvoi("pret");
        setAnnonce({ texte: `Étape ajoutée à l'historique : ${LIBELLES_DES_TYPES_D_ETAPE[type]}.`, apres: autresActions });
        quandNotee(resultat.fiche);
        return;
      }

      setRefus(resultat.refus);
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
    <section aria-labelledby="titre-de-la-note">
      <h3 id="titre-de-la-note">Noter un entretien ou un échange</h3>
      <form className="saisie" noValidate onSubmit={noter}>
        <Champ id="etape-type" libelle="Type" refus={refus.type}>
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
        <Champ id="etape-survenuLe" libelle="Date et heure" refus={refus.survenuLe}>
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
        <Champ id="etape-note" libelle="Note (facultative)" refus={refus.note}>
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
        <p className="aide">L'étape s'ajoute à l'historique à sa date ; elle ne change pas le statut.</p>
        <div className="actions">
          <button type="submit" disabled={envoi === "en-cours"}>
            {envoi === "en-cours" ? "Ajout…" : "Ajouter à l'historique"}
          </button>
        </div>
        {annonce !== undefined && annonce.apres === autresActions && (
          <p className="annonce" role="status">
            {annonce.texte}
          </p>
        )}
        {envoi === "echec" && (
          <p className="alerte" role="alert">
            L'étape n'a pas pu être ajoutée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.
          </p>
        )}
      </form>
    </section>
  );
}
