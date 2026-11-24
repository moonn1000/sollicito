import { useEffect, useRef, useState } from "react";
import type { Candidature, ClientDeLApi } from "../api/clientDeLApi.ts";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { RelancesDuTableauDeBord } from "../relances/RelancesDuTableauDeBord.tsx";
import { AlerteDesSauvegardes } from "../sauvegardes/AlerteDesSauvegardes.tsx";
import { afficherLInstant, LIBELLES_DES_STATUTS } from "./affichage.ts";
import { CarteDeLIA } from "../ia/CarteDeLIA.tsx";

type Chargement = { etape: "chargement" } | { etape: "pret"; candidatures: Candidature[] } | { etape: "echec" };

/**
 * Le tableau de bord : les candidatures, les plus récentes d'abord, dans l'ordre que rend l'API, avec leur
 * entreprise, leur poste, leur date d'envoi et leur statut. Le poste ouvre la fiche de la candidature (#12). Les
 * textes s'affichent comme du texte, jamais comme du HTML : React les échappe. Quand les copies de la base ne se font
 * plus, une alerte le dit en tête (#26). Les relances échues et celles de la semaine précèdent les candidatures (#14).
 */
export function TableauDeBord({
  client,
  annonce,
  nouvelleCandidature,
  ouvrirLaFiche,
  ouvrirLesReglages,
  ouvrirLeBrouillon,
  choisirUneIA,
  prendreLeFocus = false,
}: {
  client: ClientDeLApi;
  /** Ce qui vient de se passer, par exemple une candidature enregistrée. */
  annonce?: string;
  nouvelleCandidature: () => void;
  /** Ouvre la fiche de la candidature de cet identifiant. */
  ouvrirLaFiche: (candidature: number) => void;
  /** Ouvre les réglages, où se choisit le dossier des sauvegardes. */
  ouvrirLesReglages?: () => void;
  /** Demande à la fenêtre hôte d'ouvrir le brouillon d'une relance ; absent hors de la fenêtre. */
  ouvrirLeBrouillon?: (relance: number) => Promise<IssueDuBrouillon>;
  /** Mène à la section « IA » des réglages, depuis la carte « Connecter une IA ? » (#43). */
  choisirUneIA?: () => void;
  /** Vrai quand l'utilisateur vient d'y être mené : son titre reçoit alors le focus, pas au premier affichage. */
  prendreLeFocus?: boolean;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const titre = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (prendreLeFocus) {
      titre.current?.focus();
    }
    // Au montage seulement : le focus suit un changement d'écran.
  }, []);

  useEffect(() => {
    let affiche = true;
    client.listerLesCandidatures().then(
      (candidatures) => {
        if (affiche) {
          setChargement({ etape: "pret", candidatures });
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
  }, [client]);

  return (
    <section aria-labelledby="titre-du-tableau">
      <h2 id="titre-du-tableau" ref={titre} tabIndex={-1}>
        Tableau de bord
      </h2>
      {annonce !== undefined && (
        <p className="annonce" role="status">
          {annonce}
        </p>
      )}
      <AlerteDesSauvegardes client={client} ouvrirLesReglages={ouvrirLesReglages} />
      <CarteDeLIA client={client} choisirUneIA={choisirUneIA} />
      <RelancesDuTableauDeBord client={client} ouvrirLaFiche={ouvrirLaFiche} ouvrirLeBrouillon={ouvrirLeBrouillon} />
      {chargement.etape === "chargement" && <p>Chargement des candidatures…</p>}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          Les candidatures n'ont pas pu être chargées. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {chargement.etape === "pret" && chargement.candidatures.length === 0 && (
        <div className="vide">
          <p>Aucune candidature enregistrée pour l'instant.</p>
          <button type="button" onClick={nouvelleCandidature}>
            Enregistrer une candidature
          </button>
        </div>
      )}
      {chargement.etape === "pret" && chargement.candidatures.length > 0 && (
        <table className="candidatures">
          <caption>Vos candidatures, les plus récentes d'abord. Choisissez un poste pour ouvrir sa fiche.</caption>
          <thead>
            <tr>
              <th scope="col">Entreprise</th>
              <th scope="col">Poste</th>
              <th scope="col">Envoyée le</th>
              <th scope="col">Statut</th>
            </tr>
          </thead>
          <tbody>
            {chargement.candidatures.map((candidature) => (
              <tr key={candidature.id}>
                <td>{candidature.entreprise.nom}</td>
                <td>
                  <button
                    type="button"
                    className="lien"
                    aria-label={`${candidature.poste}, chez ${candidature.entreprise.nom} : ouvrir la fiche`}
                    onClick={() => ouvrirLaFiche(candidature.id)}
                  >
                    {candidature.poste}
                  </button>
                </td>
                <td>
                  <time dateTime={candidature.envoyeeLe}>{afficherLInstant(candidature.envoyeeLe)}</time>
                </td>
                <td>{LIBELLES_DES_STATUTS[candidature.statut] ?? candidature.statut}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
