import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  estIntrouvable,
  type ClientDeLApi,
  type ContactConnu,
  type FicheDUneCandidature,
  type RefusDeLaRelance,
  type ResultatDeLaRelance,
  type ResultatDUneAction,
} from "../api/clientDeLApi.ts";
import { Champ, sansLeChamp } from "../formulaires/Champ.tsx";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { afficherLeJour, decrireLeDestinataire, DELAI_MAXIMUM, DELAI_MINIMUM, delaiDe, delaiPropose, echeancePour } from "./affichage.ts";
import { PremierJetParLIA } from "./PremierJetParLIA.tsx";
import { PreparerLeBrouillon } from "./PreparerLeBrouillon.tsx";

/** Le délai d'usine, quand le réglage ne se lit pas. */
const DELAI_D_USINE = 7;

const DELAI_HORS_BORNES = "Le délai d'une relance compte de 1 à 365 jours.";

const REESSAYER = "Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";

type Message = { sorte: "annonce" | "alerte"; texte: string };

type Formulaire = { sorte: "ferme" } | { sorte: "planifier" } | { sorte: "changer"; relance: number };

/**
 * La relance d'une candidature, dans sa fiche (#14) : la planifier, sur une candidature en cours qui n'en a pas d'autre à
 * faire ; ouvrir son brouillon dans la messagerie, par la fenêtre hôte ; la changer, la marquer faite ou l'abandonner.
 * L'échéance part de la date d'envoi : le délai proposé est celui par défaut, ou, après une relance close, son délai plus
 * celui par défaut. Le destinataire est un contact de l'entreprise, proposé d'office s'il est le seul.
 */
export function SuiviDesRelances({
  client,
  fiche,
  contacts,
  ouvrirLeBrouillon,
  ouvrirLeBrouillonRelu,
  montrer,
  relire,
}: {
  client: ClientDeLApi;
  fiche: FicheDUneCandidature;
  /** Les contacts de l'entreprise de la candidature ; rien tant qu'ils ne sont pas lus. */
  contacts: ContactConnu[] | undefined;
  /** Demande à la fenêtre hôte d'ouvrir un brouillon ; absent hors de la fenêtre. */
  ouvrirLeBrouillon?: (relance: number) => Promise<IssueDuBrouillon>;
  /** Demande à la fenêtre hôte d'ouvrir le brouillon relu d'un premier jet (#47) ; absent hors de la fenêtre. */
  ouvrirLeBrouillonRelu?: (relance: number) => Promise<IssueDuBrouillon>;
  /** Montre la fiche à jour que l'API vient de rendre. */
  montrer: (fiche: FicheDUneCandidature) => void;
  /** Relit la fiche ; rend faux si elle n'a pas pu être relue. */
  relire: () => Promise<boolean>;
}) {
  // Rien tant que le réglage n'est pas lu : un formulaire déjà ouvert attend alors son délai (#93, point 7).
  const [delaiParDefaut, setDelaiParDefaut] = useState<number>();
  const [formulaire, setFormulaire] = useState<Formulaire>({ sorte: "ferme" });
  const [message, setMessage] = useState<Message>();
  const [enCours, setEnCours] = useState(false);
  const affichee = useRef(true);
  const enCoursDAction = useRef(false);
  const titre = useRef<HTMLHeadingElement>(null);
  const [actionsTerminees, setActionsTerminees] = useState(0);

  useEffect(() => {
    affichee.current = true;
    return () => {
      affichee.current = false;
    };
  }, []);

  useEffect(() => {
    if (actionsTerminees > 0) {
      titre.current?.focus();
    }
  }, [actionsTerminees]);

  useEffect(() => {
    let active = true;
    client.lireLeReglageDesRelances().then(
      (reglage) => {
        if (active) {
          setDelaiParDefaut(reglage.delaiParDefaut);
        }
      },
      () => {
        // Le délai d'usine reste proposé : l'API applique de toute façon le sien.
        if (active) {
          setDelaiParDefaut(DELAI_D_USINE);
        }
      },
    );
    return () => {
      active = false;
    };
  }, [client]);

  const aFaire = fiche.relances.find((relance) => relance.etat === "AFaire");
  const close = fiche.passagesPermis.length === 0;

  async function agir(faire: () => Promise<ResultatDUneAction>, annonce: string, echec: string) {
    if (enCoursDAction.current) {
      return;
    }

    enCoursDAction.current = true;
    setEnCours(true);
    setMessage(undefined);
    try {
      const resultat = await faire();
      if (!affichee.current) {
        return;
      }

      if (resultat.faite) {
        montrer(resultat.fiche);
        setFormulaire({ sorte: "ferme" });
        setMessage({ sorte: "annonce", texte: annonce });
      } else {
        await relire();
        setMessage({ sorte: "alerte", texte: resultat.motif });
      }
    } catch (erreur) {
      if (affichee.current) {
        setMessage({ sorte: "alerte", texte: estIntrouvable(erreur) ? "Cette candidature n'existe pas, ou plus." : `${echec} ${REESSAYER}` });
      }
    } finally {
      enCoursDAction.current = false;
      if (affichee.current) {
        setEnCours(false);
        setActionsTerminees((nombre) => nombre + 1);
      }
    }
  }

  const relanceDecrite = (echeance: string) => `relance du ${afficherLeJour(echeance)}`;

  return (
    <section aria-labelledby="titre-de-la-relance">
      <h3 id="titre-de-la-relance" ref={titre} tabIndex={-1}>
        Relance
      </h3>
      {aFaire !== undefined && (
        <>
          <p>
            À faire le <time dateTime={aFaire.echeance}>{afficherLeJour(aFaire.echeance)}</time>, {decrireLeDestinataire(aFaire.destinataire)}.
          </p>
          <div className="actions">
            {ouvrirLeBrouillon !== undefined && (
              <PreparerLeBrouillon
                relance={aFaire.id}
                ouvrirLeBrouillon={ouvrirLeBrouillon}
                description={`Préparer le brouillon de la ${relanceDecrite(aFaire.echeance)}`}
                desactive={enCours}
                apres={(issue) => {
                  if (issue === "refuse") {
                    void relire();
                  }
                }}
              />
            )}
            <button
              type="button"
              className="secondaire"
              disabled={enCours || formulaire.sorte !== "ferme"}
              onClick={() => {
                setMessage(undefined);
                setFormulaire({ sorte: "changer", relance: aFaire.id });
              }}
            >
              Changer la relance
            </button>
            <button
              type="button"
              disabled={enCours}
              onClick={() =>
                void agir(() => client.marquerLaRelanceFaite(fiche.id, aFaire.id), "Relance marquée faite : elle entre dans l'historique.", "La relance n'a pas pu être marquée faite.")
              }
            >
              Marquer faite
            </button>
            <button
              type="button"
              className="secondaire"
              disabled={enCours}
              onClick={() =>
                void agir(() => client.abandonnerLaRelance(fiche.id, aFaire.id), "Relance abandonnée : elle entre dans l'historique.", "La relance n'a pas pu être abandonnée.")
              }
            >
              Abandonner
            </button>
          </div>
          <p className="aide">
            Le brouillon s'ouvre dans votre messagerie : c'est vous qui l'envoyez. Faite ou abandonnée, une relance l'est pour de bon.
          </p>
          {ouvrirLeBrouillonRelu !== undefined && (
            <PremierJetParLIA
              key={aFaire.id}
              client={client}
              candidature={fiche.id}
              relance={aFaire.id}
              ouvrirLeBrouillonRelu={ouvrirLeBrouillonRelu}
              desactive={enCours}
              apres={() => void relire()}
            />
          )}
        </>
      )}
      {aFaire === undefined && close && <p>La candidature est close : elle ne reçoit plus de relance.</p>}
      {aFaire === undefined && !close && formulaire.sorte === "ferme" && (
        <>
          <p>Aucune relance à faire.</p>
          <button
            type="button"
            disabled={enCours}
            onClick={() => {
              setMessage(undefined);
              setFormulaire({ sorte: "planifier" });
            }}
          >
            Planifier une relance
          </button>
        </>
      )}
      {formulaire.sorte !== "ferme" && (
        <FormulaireDUneRelance
          key={formulaire.sorte === "changer" ? `changer-${formulaire.relance}` : "planifier"}
          envoyeeLe={fiche.envoyeeLe}
          contacts={contacts}
          delaiDeDepart={
            formulaire.sorte === "changer" && aFaire !== undefined
              ? delaiDe(fiche.envoyeeLe, aFaire.echeance)
              : delaiParDefaut === undefined
                ? undefined
                : delaiPropose(fiche, delaiParDefaut)
          }
          destinataireDeDepart={
            formulaire.sorte === "changer"
              ? (aFaire?.destinataire?.id ?? null)
              : contacts === undefined
                ? undefined
                : contacts.length === 1
                  ? contacts[0]!.id
                  : null
          }
          libelleDuBouton={formulaire.sorte === "changer" ? "Enregistrer la relance" : "Planifier la relance"}
          envoyer={async (saisie) => {
            const resultat: ResultatDeLaRelance =
              formulaire.sorte === "changer"
                ? await client.changerLaRelance(fiche.id, formulaire.relance, saisie)
                : await client.planifierUneRelance(fiche.id, saisie);
            if (!affichee.current || resultat.issue === "refusee") {
              return resultat.issue === "refusee" ? resultat.refus : {};
            }

            if (resultat.issue === "faite") {
              montrer(resultat.fiche);
              setMessage({ sorte: "annonce", texte: formulaire.sorte === "changer" ? "Relance changée." : "Relance planifiée." });
            } else {
              await relire();
              setMessage({ sorte: "alerte", texte: resultat.motif });
            }

            setFormulaire({ sorte: "ferme" });
            setActionsTerminees((nombre) => nombre + 1);
            return {};
          }}
          annuler={() => setFormulaire({ sorte: "ferme" })}
        />
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
  );
}

/** Un destinataire en valeur de la liste : « » pour personne ; rien tant qu'il n'est pas connu. */
function enValeurDeListe(destinataire: number | null | undefined): string | undefined {
  return destinataire === undefined ? undefined : destinataire === null ? "" : String(destinataire);
}

/**
 * Le délai et le destinataire d'une relance. L'échéance qui en résulte s'affiche avant l'envoi ; l'API la calcule
 * elle-même. Un délai hors des bornes est refusé avant l'envoi ; les refus de l'API s'affichent à côté de leur champ.
 */
function FormulaireDUneRelance({
  envoyeeLe,
  contacts,
  delaiDeDepart,
  destinataireDeDepart,
  libelleDuBouton,
  envoyer,
  annuler,
}: {
  envoyeeLe: string;
  contacts: ContactConnu[] | undefined;
  /** Le délai proposé ; absent tant que le réglage n'est pas lu. */
  delaiDeDepart: number | undefined;
  /** Le destinataire proposé, ou personne ; absent tant que les contacts ne sont pas lus. */
  destinataireDeDepart: number | null | undefined;
  libelleDuBouton: string;
  /** Envoie la saisie ; rend les refus de ses champs, aucun si elle est acceptée. */
  envoyer: (saisie: { delai: number; destinataire: number | null }) => Promise<RefusDeLaRelance>;
  annuler: () => void;
}) {
  // Chaque proposition se pose dès qu'elle est connue, même après l'ouverture du formulaire, puis ne bouge plus : une
  // saisie de l'utilisateur ne se remplace jamais.
  const [delaiPose, setDelai] = useState(delaiDeDepart?.toString());
  const [destinatairePose, setDestinataire] = useState(enValeurDeListe(destinataireDeDepart));
  if (delaiPose === undefined && delaiDeDepart !== undefined) {
    setDelai(String(delaiDeDepart));
  }

  if (destinatairePose === undefined && destinataireDeDepart !== undefined) {
    setDestinataire(enValeurDeListe(destinataireDeDepart));
  }

  const delai = delaiPose ?? String(DELAI_D_USINE);
  const destinataire = destinatairePose ?? "";
  const [refus, setRefus] = useState<RefusDeLaRelance>({});
  const [envoi, setEnvoi] = useState<"pret" | "en-cours" | "echec">("pret");
  const envoiEnCours = useRef(false);
  const champDuDelai = useRef<HTMLInputElement>(null);

  useEffect(() => {
    champDuDelai.current?.focus();
  }, []);

  const jours = /^\d{1,3}$/u.test(delai.trim()) ? Number(delai.trim()) : Number.NaN;
  const delaiAdmis = Number.isInteger(jours) && jours >= DELAI_MINIMUM && jours <= DELAI_MAXIMUM;

  async function soumettre(evenement: FormEvent) {
    evenement.preventDefault();
    // Un ref, et non l'état : deux clics dans le même rendu n'envoient qu'une fois.
    if (envoiEnCours.current) {
      return;
    }

    if (!delaiAdmis) {
      setRefus({ delai: [DELAI_HORS_BORNES] });
      champDuDelai.current?.focus();
      return;
    }

    envoiEnCours.current = true;
    setEnvoi("en-cours");
    try {
      const refusRecus = await envoyer({ delai: jours, destinataire: destinataire === "" ? null : Number(destinataire) });
      setRefus(refusRecus);
      setEnvoi("pret");
      if (refusRecus.delai !== undefined) {
        champDuDelai.current?.focus();
      }
    } catch {
      setEnvoi("echec");
    } finally {
      envoiEnCours.current = false;
    }
  }

  return (
    <form className="saisie" noValidate onSubmit={(evenement) => void soumettre(evenement)}>
      <Champ id="delai-de-la-relance" libelle="Relancer après, en jours depuis l'envoi" aide="De 1 à 365 jours." refus={refus.delai}>
        {(attributs) => (
          <input
            {...attributs}
            ref={champDuDelai}
            type="number"
            inputMode="numeric"
            min={DELAI_MINIMUM}
            max={DELAI_MAXIMUM}
            value={delai}
            onChange={(evenement) => {
              setDelai(evenement.target.value);
              setRefus((avant) => sansLeChamp(avant, "delai"));
            }}
          />
        )}
      </Champ>
      <p className="aide" aria-live="polite">
        {delaiAdmis ? `Échéance : ${afficherLeJour(echeancePour(envoyeeLe, jours))}.` : "Échéance : indiquez un délai de 1 à 365 jours."}
      </p>
      <Champ
        id="destinataire-de-la-relance"
        libelle="Destinataire"
        aide={contacts !== undefined && contacts.length === 0 ? "Cette entreprise n'a pas encore de contact : ajoutez-en un plus bas pour adresser le brouillon." : undefined}
        refus={refus.destinataire}
      >
        {(attributs) => (
          <select
            {...attributs}
            value={destinataire}
            onChange={(evenement) => {
              setDestinataire(evenement.target.value);
              setRefus((avant) => sansLeChamp(avant, "destinataire"));
            }}
          >
            <option value="">Personne : le brouillon s'ouvrira sans destinataire</option>
            {(contacts ?? []).map((contact) => (
              <option key={contact.id} value={String(contact.id)}>
                {contact.nom} ({contact.email})
              </option>
            ))}
          </select>
        )}
      </Champ>
      <div className="actions">
        <button type="submit" disabled={envoi === "en-cours"}>
          {envoi === "en-cours" ? "Enregistrement…" : libelleDuBouton}
        </button>
        <button type="button" className="secondaire" disabled={envoi === "en-cours"} onClick={annuler}>
          Annuler
        </button>
      </div>
      {envoi === "echec" && (
        <p className="alerte" role="alert">
          La relance n'a pas pu être enregistrée. {REESSAYER}
        </p>
      )}
    </form>
  );
}
