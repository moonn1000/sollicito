import { useEffect, useState } from "react";
import type { Candidature, ClientDeLApi } from "./api/clientDeLApi.ts";
import { SaisieDUneCandidature } from "./candidatures/SaisieDUneCandidature.tsx";
import { TableauDeBord } from "./candidatures/TableauDeBord.tsx";
import { HorsDeLaFenetre } from "./connexion.ts";
import { MiseEnPage, type Ecran } from "./miseEnPage/MiseEnPage.tsx";

type Connexion =
  | { etat: "connexion" }
  | { etat: "prete"; client: ClientDeLApi }
  | { etat: "hors-fenetre" }
  | { etat: "injoignable" };

const MESSAGES: Record<Connexion["etat"], string> = {
  connexion: "Connexion à l'API locale…",
  prete: "Connectée à l'API locale.",
  "hors-fenetre": "Cette page ne fonctionne que dans la fenêtre de Sollicito.",
  injoignable: "L'API locale ne répond pas. Fermez Sollicito, puis relancez-le.",
};

/** L'interface : la connexion à l'API locale, puis le tableau de bord et l'écran de saisie, dans la mise en page commune. */
export function App({ connecter }: { connecter: () => Promise<ClientDeLApi> }) {
  const [connexion, setConnexion] = useState<Connexion>({ etat: "connexion" });
  const [ecran, setEcran] = useState<Ecran>("tableau");
  const [annonce, setAnnonce] = useState<string>();
  // Vrai dès que l'utilisateur a changé d'écran : le titre de l'écran atteint reçoit alors le focus.
  const [navigue, setNavigue] = useState(false);

  useEffect(() => {
    let affichee = true;
    connecter()
      .then(async (client) => ({ client, etat: await client.lireEtat() }))
      .then(
        ({ client, etat }) => {
          if (affichee) {
            setConnexion(etat.statut === "pret" ? { etat: "prete", client } : { etat: "injoignable" });
          }
        },
        (erreur: unknown) => {
          if (affichee) {
            setConnexion({ etat: erreur instanceof HorsDeLaFenetre ? "hors-fenetre" : "injoignable" });
          }
        },
      );
    return () => {
      affichee = false;
    };
  }, [connecter]);

  const naviguer = (vers: Ecran) => {
    setAnnonce(undefined);
    setNavigue(true);
    setEcran(vers);
  };

  const quandEnregistree = (candidature: Candidature) => {
    setAnnonce(`Candidature enregistrée : ${candidature.poste}, chez ${candidature.entreprise.nom}.`);
    setNavigue(true);
    setEcran("tableau");
  };

  return (
    <MiseEnPage
      ecran={ecran}
      naviguer={connexion.etat === "prete" ? naviguer : undefined}
      etatDeLaConnexion={MESSAGES[connexion.etat]}
    >
      {connexion.etat === "prete" &&
        (ecran === "tableau" ? (
          <TableauDeBord
            client={connexion.client}
            annonce={annonce}
            nouvelleCandidature={() => naviguer("saisie")}
            prendreLeFocus={navigue}
          />
        ) : (
          <SaisieDUneCandidature client={connexion.client} quandEnregistree={quandEnregistree} />
        ))}
    </MiseEnPage>
  );
}
