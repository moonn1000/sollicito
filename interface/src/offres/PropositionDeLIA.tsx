import { useEffect, useRef, useState } from "react";
import type { ChampsProposes, ClientDeLApi } from "../api/clientDeLApi.ts";
import { societeDuFournisseur, useDisponibiliteDeLIA } from "../ia/disponibiliteDeLIA.ts";
import {
  aideDeLaProposition,
  LIBELLE_DE_LA_PROPOSITION,
  LIBELLE_DE_LA_PROPOSITION_EN_COURS,
  LIBELLE_DU_RETRAIT_DE_LA_PROPOSITION,
  messageDeLaProposition,
  PROPOSITION_IMPOSSIBLE,
  SANS_IA_PRETE,
} from "./affichage.ts";

type Etat = { sorte: "repos" } | { sorte: "en-cours" } | { sorte: "faite"; message: string } | { sorte: "echec"; message: string };

/**
 * Le bouton qui demande à l'IA de proposer les champs d'une offre collée (#46, ADR-0018), sous son texte. Il ne part que
 * sur un clic : jamais au collage, à la frappe, ni à l'ouverture de l'écran, qui ne lit que l'état de l'IA, sur le poste.
 * Sans IA prête, l'écran le dit à sa place, et la saisie à la main reste entière. La proposition ne remplit que les champs
 * encore vides, par `appliquer` : rien de ce que l'utilisateur a saisi n'est remplacé, et rien ne s'enregistre avant
 * « Enregistrer l'offre ». Une seule demande à la fois ; quitter l'écran l'interrompt. Un échec se dit par le texte de
 * l'API, jamais par celui de l'IA.
 */
export function PropositionDeLIA({
  client,
  texte,
  appliquer,
  quandTexteRefuse,
  retirer,
}: {
  client: ClientDeLApi;
  /** Le texte collé, tel qu'il est à l'écran. */
  texte: string;
  /** Remplit les champs encore vides ; rend combien il en a remplis, et combien de champs trouvés étaient déjà remplis. */
  appliquer: (champs: ChampsProposes) => { remplis: number; dejaRemplis: number };
  /** Reçoit le refus du texte, que l'API rend avant toute demande, à montrer sous son champ. */
  quandTexteRefuse: (refus: string[]) => void;
  /** Retire la proposition ; absent quand aucun champ proposé n'est à retirer. */
  retirer?: () => void;
}) {
  const disponibilite = useDisponibiliteDeLIA(client);
  const [etat, setEtat] = useState<Etat>({ sorte: "repos" });
  // Lu aussitôt, sans attendre le rendu : deux clics rapprochés ne font qu'une demande.
  const enCours = useRef(false);
  const affichee = useRef(true);
  const annulation = useRef<AbortController>(undefined);

  useEffect(() => {
    affichee.current = true;
    return () => {
      affichee.current = false;
      annulation.current?.abort();
    };
  }, []);

  async function proposer() {
    if (enCours.current) {
      return;
    }

    enCours.current = true;
    const controleur = new AbortController();
    annulation.current = controleur;
    setEtat({ sorte: "en-cours" });
    try {
      const resultat = await client.extraireUneOffre(texte, controleur.signal);
      if (!affichee.current) {
        return;
      }

      switch (resultat.issue) {
        case "proposee": {
          const trouves = Object.values(resultat.champs).filter((valeur) => valeur !== null).length;
          const { remplis, dejaRemplis } = appliquer(resultat.champs);
          setEtat({ sorte: "faite", message: messageDeLaProposition(trouves, remplis, dejaRemplis) });
          break;
        }
        case "echec":
          setEtat({ sorte: "echec", message: resultat.message });
          break;
        case "refuse":
          setEtat({ sorte: "echec", message: resultat.motif });
          break;
        case "texteRefuse":
          setEtat({ sorte: "repos" });
          quandTexteRefuse(resultat.refus);
          break;
      }
    } catch {
      if (affichee.current) {
        setEtat({ sorte: "echec", message: PROPOSITION_IMPOSSIBLE });
      }
    } finally {
      enCours.current = false;
      if (annulation.current === controleur) {
        annulation.current = undefined;
      }
    }
  }

  if (disponibilite.sorte === "lecture") {
    return null;
  }

  if (disponibilite.sorte === "absente") {
    return (
      <p className="aide proposition-de-l-ia" id="aide-de-la-proposition">
        {SANS_IA_PRETE}
      </p>
    );
  }

  return (
    <div className="proposition-de-l-ia">
      <div className="actions">
        <button
          type="button"
          disabled={etat.sorte === "en-cours"}
          aria-describedby={disponibilite.sorte === "prete" ? "aide-de-la-proposition" : undefined}
          onClick={() => void proposer()}
        >
          {etat.sorte === "en-cours" ? LIBELLE_DE_LA_PROPOSITION_EN_COURS : LIBELLE_DE_LA_PROPOSITION}
        </button>
        {retirer !== undefined && (
          <button
            type="button"
            className="secondaire"
            onClick={() => {
              // Le message de la proposition ne décrit plus les champs : il part avec elle.
              setEtat({ sorte: "repos" });
              retirer();
            }}
          >
            {LIBELLE_DU_RETRAIT_DE_LA_PROPOSITION}
          </button>
        )}
      </div>
      {disponibilite.sorte === "prete" && (
        <p className="aide" id="aide-de-la-proposition">
          {aideDeLaProposition(societeDuFournisseur(disponibilite.fournisseur))}
        </p>
      )}
      <p className="etat-de-la-proposition" role="status">
        {etat.sorte === "faite" ? etat.message : ""}
      </p>
      {etat.sorte === "echec" && (
        <p className="alerte" role="alert">
          {etat.message}
        </p>
      )}
    </div>
  );
}
