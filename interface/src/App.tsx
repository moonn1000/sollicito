import { useEffect, useState } from "react";
import type { Candidature, ClientDeLApi, FicheDUneCandidature as Fiche } from "./api/clientDeLApi.ts";
import { FicheDUneCandidature } from "./candidatures/FicheDUneCandidature.tsx";
import { ModificationDUneCandidature } from "./candidatures/ModificationDUneCandidature.tsx";
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

/**
 * L'interface : la connexion à l'API locale, puis le tableau de bord, l'écran de saisie, la fiche d'une candidature et
 * sa modification, dans la mise en page commune.
 */
export function App({ connecter }: { connecter: () => Promise<ClientDeLApi> }) {
  const [connexion, setConnexion] = useState<Connexion>({ etat: "connexion" });
  const [ecran, setEcran] = useState<Ecran>({ nom: "tableau" });
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

  // Mène à un écran en y annonçant ce qui vient de se passer.
  const annoncer = (texte: string, vers: Ecran) => {
    setAnnonce(texte);
    setNavigue(true);
    setEcran(vers);
  };

  const quandEnregistree = (candidature: Candidature) =>
    annoncer(`Candidature enregistrée : ${candidature.poste}, chez ${candidature.entreprise.nom}.`, { nom: "tableau" });

  // L'annonce nomme l'entreprise rattachée, celle que l'API a reconnue : son nom est celui de sa première saisie.
  const quandModifiee = (fiche: Fiche) =>
    annoncer(`Candidature modifiée : ${fiche.poste}, chez ${fiche.entreprise.nom}.`, { nom: "fiche", candidature: fiche.id });

  const quandSupprimee = (fiche: Fiche) =>
    annoncer(`Candidature supprimée : ${fiche.poste}, chez ${fiche.entreprise.nom}.`, { nom: "tableau" });

  const ecranOuvert = () => {
    if (connexion.etat !== "prete") {
      return null;
    }

    switch (ecran.nom) {
      case "tableau":
        return (
          <TableauDeBord
            client={connexion.client}
            annonce={annonce}
            nouvelleCandidature={() => naviguer({ nom: "saisie" })}
            ouvrirLaFiche={(candidature) => naviguer({ nom: "fiche", candidature })}
            prendreLeFocus={navigue}
          />
        );
      case "saisie":
        return <SaisieDUneCandidature client={connexion.client} quandEnregistree={quandEnregistree} />;
      case "fiche":
        return (
          <FicheDUneCandidature
            key={ecran.candidature}
            client={connexion.client}
            candidature={ecran.candidature}
            annonce={annonce}
            revenir={() => naviguer({ nom: "tableau" })}
            modifier={() => naviguer({ nom: "modification", candidature: ecran.candidature })}
            quandSupprimee={quandSupprimee}
          />
        );
      case "modification":
        return (
          <ModificationDUneCandidature
            key={ecran.candidature}
            client={connexion.client}
            candidature={ecran.candidature}
            revenir={() => naviguer({ nom: "fiche", candidature: ecran.candidature })}
            quandModifiee={quandModifiee}
          />
        );
    }
  };

  return (
    <MiseEnPage
      ecran={ecran}
      naviguer={connexion.etat === "prete" ? naviguer : undefined}
      etatDeLaConnexion={MESSAGES[connexion.etat]}
    >
      {ecranOuvert()}
    </MiseEnPage>
  );
}
