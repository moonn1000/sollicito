import { useEffect, useId, useRef, useState } from "react";
import type { ClientDeLApi, EtatDeLIA } from "../api/clientDeLApi.ts";
import { Champ } from "../formulaires/Champ.tsx";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { fournisseurPret, societeDuFournisseur, type FournisseurEnLigne } from "../ia/disponibiliteDeLIA.ts";
import { MESSAGES_DU_BROUILLON } from "./affichage.ts";

/** La plus longue réponse que l'API rend : le texte relu se borne de même (ADR-0019). */
export const LONGUEUR_MAXIMALE_DU_TEXTE_RELU = 2_000;

/** Les textes du bloc, que les tests comparent mot pour mot. */
export const TEXTES_DU_PREMIER_JET = {
  titre: "Premier jet par l'IA",
  avantLeClic: (fournisseur: FournisseurEnLigne | undefined) =>
    fournisseur === undefined
      ? "Sur votre demande, le poste, l'entreprise et la date d'envoi de cette candidature partent chez le fournisseur d'IA choisi, avec les consignes de Sollicito. Rien d'autre : ni le contact, ni vos notes, ni l'historique."
      : `Sur votre demande, le poste, l'entreprise et la date d'envoi de cette candidature partent chez ${societeDuFournisseur(fournisseur)}, avec les consignes de Sollicito. Rien d'autre : ni le contact, ni vos notes, ni l'historique. L'appel est décompté sur votre compte chez ${societeDuFournisseur(fournisseur)}.`,
  rediger: (fournisseur: FournisseurEnLigne | undefined) =>
    fournisseur === undefined ? "Rédiger un premier jet" : `Rédiger un premier jet avec ${fournisseur}`,
  redactionEnCours: "Rédaction en cours…",
  etiquette: "Premier jet, à relire et à corriger",
  aide: "L'IA peut se tromper : relisez chaque phrase. L'objet et le destinataire restent ceux de la relance. Le brouillon s'ouvrira dans votre messagerie, et c'est vous qui l'enverrez.",
  ouvrir: "Ouvrir dans ma messagerie",
  ouvertureEnCours: "Ouverture du brouillon…",
  effacer: "Effacer le premier jet",
  brouillonDeSollicito: "Le brouillon de Sollicito reste disponible : « Préparer le brouillon ».",
  demandeImpossible: "Le premier jet n'a pas pu être demandé. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  depotImpossible: "Le brouillon n'a pas pu être préparé. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  sansIA: {
    aucune: "Aucune IA n'est choisie : pour faire rédiger un premier jet, choisissez Claude ou OpenAI dans les réglages.",
    modeleLocal: "Le modèle local n'est pas encore branché pour la rédaction : choisissez Claude ou OpenAI dans les réglages.",
    sansCle: "Aucune clé n'est enregistrée : enregistrez d'abord votre clé.",
    cleIllisible: "La clé enregistrée ne peut pas être relue sur ce poste : saisissez-la de nouveau.",
    reglageIllisible: "Choisissez d'abord Claude ou OpenAI, puis enregistrez votre clé.",
  },
} as const;

/**
 * Ce que le bloc sait de l'IA, lue à son ouverture : la lecture en cours ; prête, avec son fournisseur ; pas prête, avec
 * la phrase qui dit pourquoi ; ou inconnue, quand l'état n'a pas pu être lu : le bouton reste, et le clic dira ce qu'il
 * en est.
 */
type IA =
  | { sorte: "lecture" }
  | { sorte: "prete"; fournisseur: FournisseurEnLigne }
  | { sorte: "absente"; pourquoi: string }
  | { sorte: "inconnue" };

type Redaction =
  | { sorte: "attente" }
  | { sorte: "enCours" }
  | { sorte: "echec"; message: string }
  | { sorte: "refus"; motif: string };

type Ouverture =
  | { sorte: "attente" }
  | { sorte: "enCours" }
  | { sorte: "issue"; issue: IssueDuBrouillon }
  | { sorte: "refus"; message: string };

/** Pourquoi l'IA n'est pas prête, en une phrase ; rien si elle l'est. */
export function pourquoiSansIA(etat: EtatDeLIA): string | undefined {
  if (fournisseurPret(etat) !== undefined) {
    return undefined;
  }

  if (!etat.reglageLisible) {
    return TEXTES_DU_PREMIER_JET.sansIA.reglageIllisible;
  }

  if (etat.fournisseur === "Aucun") {
    return TEXTES_DU_PREMIER_JET.sansIA.aucune;
  }

  if (etat.fournisseur === "Ollama") {
    return TEXTES_DU_PREMIER_JET.sansIA.modeleLocal;
  }

  return etat.cle === "Illisible" ? TEXTES_DU_PREMIER_JET.sansIA.cleIllisible : TEXTES_DU_PREMIER_JET.sansIA.sansCle;
}

/**
 * Le premier jet d'une relance par l'IA (#47, ADR-0019), dans sa fiche, sous « Préparer le brouillon ». L'écran dit, avant
 * le clic, ce qui part chez le fournisseur ; la demande ne part que sur ce clic, jamais à l'affichage. Le texte revient
 * dans un champ modifiable, comme du texte, jamais comme du HTML ; l'utilisateur le relit et le corrige, puis la page le
 * dépose auprès de l'API et demande à la fenêtre hôte de l'ouvrir dans la messagerie, avec l'objet et le destinataire de
 * la relance. Sollicito n'envoie rien : c'est l'utilisateur qui envoie le brouillon. Sans IA prête, le bloc le dit, et le
 * brouillon de #14 reste.
 */
export function PremierJetParLIA({
  client,
  candidature,
  relance,
  ouvrirLeBrouillonRelu,
  desactive = false,
  apres,
}: {
  client: ClientDeLApi;
  candidature: number;
  relance: number;
  /** Demande à la fenêtre hôte d'ouvrir le brouillon relu déposé pour cette relance, et rend l'issue. */
  ouvrirLeBrouillonRelu: (relance: number) => Promise<IssueDuBrouillon>;
  desactive?: boolean;
  /** Prévenu quand la relance n'est plus à faire, pour relire la fiche. */
  apres?: () => void;
}) {
  const [ia, setIA] = useState<IA>({ sorte: "lecture" });
  const [redaction, setRedaction] = useState<Redaction>({ sorte: "attente" });
  const [texte, setTexte] = useState<string>();
  const [refus, setRefus] = useState<string[]>();
  const [ouverture, setOuverture] = useState<Ouverture>({ sorte: "attente" });
  const affiche = useRef(true);
  const enCoursDEnvoi = useRef(false);
  const id = useId();

  useEffect(() => {
    affiche.current = true;
    return () => {
      affiche.current = false;
    };
  }, []);

  // L'état de l'IA, lu une fois à l'ouverture, sur le poste : aucune demande ne part à l'IA.
  useEffect(() => {
    let lu = true;
    client.lireLEtatDeLIA().then(
      (etat) => {
        if (lu) {
          const fournisseur = fournisseurPret(etat);
          setIA(
            fournisseur === undefined
              ? { sorte: "absente", pourquoi: pourquoiSansIA(etat) ?? TEXTES_DU_PREMIER_JET.sansIA.aucune }
              : { sorte: "prete", fournisseur },
          );
        }
      },
      () => {
        if (lu) {
          setIA({ sorte: "inconnue" });
        }
      },
    );
    return () => {
      lu = false;
    };
  }, [client]);

  async function rediger() {
    if (enCoursDEnvoi.current) {
      return;
    }

    enCoursDEnvoi.current = true;
    setRedaction({ sorte: "enCours" });
    setOuverture({ sorte: "attente" });
    try {
      const resultat = await client.demanderUnPremierJet(candidature, relance);
      if (!affiche.current) {
        return;
      }

      if (resultat.issue === "redige") {
        setTexte(resultat.texte);
        setRefus(undefined);
        setRedaction({ sorte: "attente" });
      } else if (resultat.issue === "echoue") {
        setRedaction({ sorte: "echec", message: resultat.message });
      } else {
        setRedaction({ sorte: "refus", motif: resultat.motif });
      }
    } catch {
      if (affiche.current) {
        setRedaction({ sorte: "refus", motif: TEXTES_DU_PREMIER_JET.demandeImpossible });
      }
    } finally {
      enCoursDEnvoi.current = false;
    }
  }

  async function ouvrir() {
    if (enCoursDEnvoi.current || texte === undefined) {
      return;
    }

    enCoursDEnvoi.current = true;
    setOuverture({ sorte: "enCours" });
    try {
      const depot = await client.deposerLeBrouillonRelu(candidature, relance, texte);
      if (!affiche.current) {
        return;
      }

      if (depot.issue === "refusee") {
        setRefus(depot.refus);
        setOuverture({ sorte: "attente" });
        return;
      }

      if (depot.issue === "refuse") {
        setOuverture({ sorte: "refus", message: depot.motif });
        apres?.();
        return;
      }

      setRefus(undefined);
      let issue: IssueDuBrouillon;
      try {
        issue = await ouvrirLeBrouillonRelu(relance);
      } catch {
        issue = "sansReponse";
      }

      if (affiche.current) {
        setOuverture({ sorte: "issue", issue });
        if (issue === "refuse") {
          apres?.();
        }
      }
    } catch {
      if (affiche.current) {
        setOuverture({ sorte: "refus", message: TEXTES_DU_PREMIER_JET.depotImpossible });
      }
    } finally {
      enCoursDEnvoi.current = false;
    }
  }

  function effacer() {
    setTexte(undefined);
    setRefus(undefined);
    setOuverture({ sorte: "attente" });
    setRedaction({ sorte: "attente" });
  }

  const fournisseur = ia.sorte === "prete" ? ia.fournisseur : undefined;
  const occupe = desactive || redaction.sorte === "enCours" || ouverture.sorte === "enCours";
  return (
    <section className="premier-jet" aria-labelledby={`${id}-titre`}>
      <h4 id={`${id}-titre`}>{TEXTES_DU_PREMIER_JET.titre}</h4>
      {ia.sorte === "absente" && <p>{ia.pourquoi}</p>}
      {(ia.sorte === "prete" || ia.sorte === "inconnue") && (
        <>
          <p>{TEXTES_DU_PREMIER_JET.avantLeClic(fournisseur)}</p>
          <div className="actions">
            <button type="button" disabled={occupe} onClick={() => void rediger()}>
              {redaction.sorte === "enCours" ? TEXTES_DU_PREMIER_JET.redactionEnCours : TEXTES_DU_PREMIER_JET.rediger(fournisseur)}
            </button>
          </div>
        </>
      )}
      {redaction.sorte === "echec" && (
        <p className="alerte" role="alert">
          {redaction.message} {TEXTES_DU_PREMIER_JET.brouillonDeSollicito}
        </p>
      )}
      {redaction.sorte === "refus" && (
        <p className="alerte" role="alert">
          {redaction.motif}
        </p>
      )}
      {texte !== undefined && (
        <>
          <Champ id={`${id}-texte`} libelle={TEXTES_DU_PREMIER_JET.etiquette} aide={TEXTES_DU_PREMIER_JET.aide} refus={refus}>
            {(attributs) => (
              <textarea
                {...attributs}
                value={texte}
                rows={10}
                maxLength={LONGUEUR_MAXIMALE_DU_TEXTE_RELU}
                spellCheck
                onChange={(evenement) => {
                  setTexte(evenement.target.value);
                  setRefus(undefined);
                }}
              />
            )}
          </Champ>
          <div className="actions">
            <button type="button" disabled={occupe} onClick={() => void ouvrir()}>
              {ouverture.sorte === "enCours" ? TEXTES_DU_PREMIER_JET.ouvertureEnCours : TEXTES_DU_PREMIER_JET.ouvrir}
            </button>
            <button type="button" className="secondaire" disabled={occupe} onClick={effacer}>
              {TEXTES_DU_PREMIER_JET.effacer}
            </button>
          </div>
        </>
      )}
      {ouverture.sorte === "issue" && ouverture.issue === "ouvert" && (
        <p className="annonce" role="status">
          {MESSAGES_DU_BROUILLON.ouvert}
        </p>
      )}
      {ouverture.sorte === "issue" && ouverture.issue !== "ouvert" && (
        <p className="alerte" role="alert">
          {MESSAGES_DU_BROUILLON[ouverture.issue]}
        </p>
      )}
      {ouverture.sorte === "refus" && (
        <p className="alerte" role="alert">
          {ouverture.message}
        </p>
      )}
    </section>
  );
}
