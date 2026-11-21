import { useEffect, useState } from "react";
import type { ClientDeLApi, RelanceSignalee, RelancesDuTableauDeBord as Relances } from "../api/clientDeLApi.ts";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { afficherLeJour, decrireLeDestinataire, situerLEcheance } from "./affichage.ts";
import { PreparerLeBrouillon } from "./PreparerLeBrouillon.tsx";

type Chargement = { etape: "chargement" } | { etape: "pret"; relances: Relances } | { etape: "echec" };

/**
 * Les relances que signale le tableau de bord (#14) : celles qui sont échues, dont l'échéance tombe aujourd'hui ou avant,
 * et celles de la semaine, dans les sept jours qui suivent. L'API les calcule en date locale. Chacune mène à sa fiche, et
 * ouvre son brouillon dans la messagerie par la fenêtre hôte. Les textes s'affichent comme du texte : React les échappe.
 */
export function RelancesDuTableauDeBord({
  client,
  ouvrirLaFiche,
  ouvrirLeBrouillon,
}: {
  client: ClientDeLApi;
  ouvrirLaFiche: (candidature: number) => void;
  /** Demande à la fenêtre hôte d'ouvrir un brouillon ; absent hors de la fenêtre. */
  ouvrirLeBrouillon?: (relance: number) => Promise<IssueDuBrouillon>;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  // Relit les relances quand un brouillon est refusé : la relance n'était plus à faire.
  const [lectures, setLectures] = useState(0);

  useEffect(() => {
    let affiche = true;
    client.listerLesRelances().then(
      (relances) => {
        if (affiche) {
          setChargement({ etape: "pret", relances });
        }
      },
      () => {
        if (affiche) {
          setChargement({ etape: "echec" });
        }
      },
    );
    return () => {
      affiche = false;
    };
  }, [client, lectures]);

  const relire = (issue: IssueDuBrouillon) => {
    if (issue === "refuse") {
      setLectures((nombre) => nombre + 1);
    }
  };

  return (
    <section className="relances" aria-labelledby="titre-des-relances-du-tableau">
      <h3 id="titre-des-relances-du-tableau">Relances</h3>
      {chargement.etape === "chargement" && <p>Chargement des relances…</p>}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          Les relances n'ont pas pu être chargées. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {chargement.etape === "pret" && chargement.relances.echues.length + chargement.relances.deLaSemaine.length === 0 && (
        <p>Aucune relance échue, ni prévue dans les sept jours.</p>
      )}
      {chargement.etape === "pret" && chargement.relances.echues.length > 0 && (
        <ListeDesRelances
          titre="Échues"
          relances={chargement.relances.echues}
          aujourdhui={chargement.relances.aujourdhui}
          ouvrirLaFiche={ouvrirLaFiche}
          ouvrirLeBrouillon={ouvrirLeBrouillon}
          apres={relire}
        />
      )}
      {chargement.etape === "pret" && chargement.relances.deLaSemaine.length > 0 && (
        <ListeDesRelances
          titre="Cette semaine"
          relances={chargement.relances.deLaSemaine}
          aujourdhui={chargement.relances.aujourdhui}
          ouvrirLaFiche={ouvrirLaFiche}
          ouvrirLeBrouillon={ouvrirLeBrouillon}
          apres={relire}
        />
      )}
    </section>
  );
}

function ListeDesRelances({
  titre,
  relances,
  aujourdhui,
  ouvrirLaFiche,
  ouvrirLeBrouillon,
  apres,
}: {
  titre: string;
  relances: RelanceSignalee[];
  aujourdhui: string;
  ouvrirLaFiche: (candidature: number) => void;
  ouvrirLeBrouillon?: (relance: number) => Promise<IssueDuBrouillon>;
  apres: (issue: IssueDuBrouillon) => void;
}) {
  return (
    <div className="liste-de-relances">
      <h4>
        {titre} ({relances.length})
      </h4>
      <ul>
        {relances.map((relance) => {
          const nom = `${relance.candidature.poste}, chez ${relance.candidature.entreprise.nom}`;
          return (
            <li key={relance.id}>
              <p>
                <button type="button" className="lien" aria-label={`${nom} : ouvrir la fiche`} onClick={() => ouvrirLaFiche(relance.candidature.id)}>
                  {relance.candidature.poste}
                </button>
                , chez {relance.candidature.entreprise.nom}
              </p>
              <p>
                <time dateTime={relance.echeance}>{afficherLeJour(relance.echeance)}</time> ({situerLEcheance(relance.echeance, aujourdhui)}),{" "}
                {decrireLeDestinataire(relance.destinataire)}
              </p>
              {ouvrirLeBrouillon !== undefined && (
                <PreparerLeBrouillon
                  relance={relance.id}
                  ouvrirLeBrouillon={ouvrirLeBrouillon}
                  description={`Préparer le brouillon de la relance : ${nom}`}
                  apres={apres}
                />
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
