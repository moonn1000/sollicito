import { Fragment, useEffect, useState } from "react";
import type { Candidature, ClientDeLApi, CopieGardee, FicheDUneCandidature as Fiche } from "./api/clientDeLApi.ts";
import { FicheDUneCandidature } from "./candidatures/FicheDUneCandidature.tsx";
import { ModificationDUneCandidature } from "./candidatures/ModificationDUneCandidature.tsx";
import { SaisieDUneCandidature } from "./candidatures/SaisieDUneCandidature.tsx";
import { TableauDeBord } from "./candidatures/TableauDeBord.tsx";
import { HorsDeLaFenetre } from "./connexion.ts";
import type { IssueDeLaRestauration, IssueDuBrouillon, IssueDuChoixDuDossier } from "./hote/canalDeLHote.ts";
import { MiseEnPage, type Ecran } from "./miseEnPage/MiseEnPage.tsx";
import { ANNONCE_DU_RENONCEMENT, annonceDeLaPostulation, annonceDeLOffre, AUCUN_FILTRE, type FiltresDesOffres } from "./offres/affichage.ts";
import { FicheDUneOffre } from "./offres/FicheDUneOffre.tsx";
import { ImportDOffres } from "./offres/ImportDOffres.tsx";
import { ListeDesOffres } from "./offres/ListeDesOffres.tsx";
import { ModificationDUneOffre } from "./offres/ModificationDUneOffre.tsx";
import { PostulerAUneOffre } from "./offres/PostulerAUneOffre.tsx";
import { SaisieDUneOffre } from "./offres/SaisieDUneOffre.tsx";
import { EcranDesReglages } from "./reglages/EcranDesReglages.tsx";
import { annonceDeLaRestauration } from "./sauvegardes/affichage.ts";

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
 * sa modification, la liste des offres (#38), la saisie d'une offre, sa fiche et sa correction (#36), « Postuler » (#38),
 * et les réglages (#26), dans la mise en page commune.
 */
export function App({
  connecter,
  choisirLeDossierDesSauvegardes,
  ouvrirLeBrouillon,
  restaurerUneSauvegarde,
}: {
  connecter: () => Promise<ClientDeLApi>;
  /** Ouvre le dialogue de dossier de la fenêtre hôte ; absent hors de la fenêtre. */
  choisirLeDossierDesSauvegardes?: () => Promise<IssueDuChoixDuDossier>;
  /** Demande à la fenêtre hôte d'ouvrir le brouillon d'une relance dans la messagerie ; absent hors de la fenêtre (#14). */
  ouvrirLeBrouillon?: (relance: number) => Promise<IssueDuBrouillon>;
  /** Demande à la fenêtre hôte de restaurer une copie gardée ; absent hors de la fenêtre (#28). */
  restaurerUneSauvegarde?: (copie: string, quandEnCours: () => void) => Promise<IssueDeLaRestauration>;
}) {
  const [connexion, setConnexion] = useState<Connexion>({ etat: "connexion" });
  const [ecran, setEcran] = useState<Ecran>({ nom: "tableau" });
  const [annonce, setAnnonce] = useState<string>();
  // Changée à chaque restauration : tous les écrans se remontent et relisent la base, dont les identifiants ont changé.
  const [generation, setGeneration] = useState(0);
  // Vrai dès que l'utilisateur a changé d'écran : le titre de l'écran atteint reçoit alors le focus.
  const [navigue, setNavigue] = useState(false);
  // Les filtres de la liste des offres (#38), gardés le temps de la session, en mémoire seulement : jamais écrits.
  const [filtresDesOffres, setFiltresDesOffres] = useState<FiltresDesOffres>(AUCUN_FILTRE);

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

  // L'annonce nomme l'entreprise rattachée, celle que l'API a reconnue : sous le nom de sa première saisie, ou sous celui
  // que la modification vient de corriger (ADR-0007).
  const quandModifiee = (fiche: Fiche) =>
    annoncer(`Candidature modifiée : ${fiche.poste}, chez ${fiche.entreprise.nom}.`, { nom: "fiche", candidature: fiche.id });

  const quandSupprimee = (fiche: Fiche) =>
    annoncer(`Candidature supprimée : ${fiche.poste}, chez ${fiche.entreprise.nom}.`, { nom: "tableau" });

  // La fiche d'une offre : depuis la liste, un doublon possible (#36) ou la fiche d'une candidature (#38).
  const ouvrirUneOffre = (offre: number) => naviguer({ nom: "offre", offre });

  // La fiche d'une candidature : depuis la liste des offres, la fiche d'une offre ou « Postuler » (#38).
  const ouvrirLaCandidature = (candidature: number) => naviguer({ nom: "fiche", candidature });

  // La base vient d'être remplacée : retour au tableau de bord, qui relit tout, avec l'annonce de la restauration.
  const quandRestauree = (issue: "restauree" | "restaureeDepuisUneBaseAbimee", copie: CopieGardee) => {
    setGeneration((avant) => avant + 1);
    annoncer(annonceDeLaRestauration(issue, copie.faiteLe), { nom: "tableau" });
  };

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
            ouvrirLesReglages={() => naviguer({ nom: "reglages" })}
            ouvrirLeBrouillon={ouvrirLeBrouillon}
            choisirUneIA={() => naviguer({ nom: "reglages", section: "ia" })}
            prendreLeFocus={navigue}
          />
        );
      case "saisie":
        return <SaisieDUneCandidature client={connexion.client} quandEnregistree={quandEnregistree} />;
      case "reglages":
        return (
          <EcranDesReglages
            client={connexion.client}
            choisirLeDossierDesSauvegardes={choisirLeDossierDesSauvegardes}
            restaurerUneSauvegarde={restaurerUneSauvegarde}
            quandRestauree={quandRestauree}
            prendreLeFocus={navigue && ecran.section !== "ia"}
            allerALIA={navigue && ecran.section === "ia"}
          />
        );
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
            ouvrirLeBrouillon={ouvrirLeBrouillon}
            ouvrirLOffre={ouvrirUneOffre}
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
      case "offres":
        return (
          <ListeDesOffres
            client={connexion.client}
            annonce={annonce}
            filtres={filtresDesOffres}
            changerLesFiltres={setFiltresDesOffres}
            collerUneOffre={() => naviguer({ nom: "saisieDUneOffre" })}
            importerUnFichier={() => naviguer({ nom: "importDOffres" })}
            ouvrirLOffre={ouvrirUneOffre}
            ouvrirLaCandidature={ouvrirLaCandidature}
            prendreLeFocus={navigue}
          />
        );
      case "saisieDUneOffre":
        return (
          <SaisieDUneOffre
            client={connexion.client}
            quandEnregistree={(fiche) => annoncer(annonceDeLOffre("enregistrée", fiche), { nom: "offre", offre: fiche.id })}
            renoncer={() => annoncer(ANNONCE_DU_RENONCEMENT, { nom: "tableau" })}
          />
        );
      case "importDOffres":
        return <ImportDOffres client={connexion.client} revenir={() => naviguer({ nom: "offres" })} ouvrirLOffre={ouvrirUneOffre} />;
      case "offre":
        return (
          <FicheDUneOffre
            key={ecran.offre}
            client={connexion.client}
            offre={ecran.offre}
            annonce={annonce}
            revenir={() => naviguer({ nom: "offres" })}
            modifier={() => naviguer({ nom: "modificationDUneOffre", offre: ecran.offre })}
            postuler={() => naviguer({ nom: "postuler", offre: ecran.offre })}
            ouvrirUneOffre={ouvrirUneOffre}
            ouvrirLaCandidature={ouvrirLaCandidature}
          />
        );
      case "modificationDUneOffre":
        return (
          <ModificationDUneOffre
            key={ecran.offre}
            client={connexion.client}
            offre={ecran.offre}
            revenir={() => naviguer({ nom: "offre", offre: ecran.offre })}
            quandModifiee={(fiche) => annoncer(annonceDeLOffre("modifiée", fiche), { nom: "offre", offre: fiche.id })}
          />
        );
      case "postuler":
        return (
          <PostulerAUneOffre
            key={ecran.offre}
            client={connexion.client}
            offre={ecran.offre}
            revenir={() => naviguer({ nom: "offre", offre: ecran.offre })}
            quandEnregistree={(candidature) => annoncer(annonceDeLaPostulation(candidature), { nom: "fiche", candidature: candidature.id })}
            ouvrirLaCandidature={ouvrirLaCandidature}
            indiquerLEntreprise={() => naviguer({ nom: "modificationDUneOffre", offre: ecran.offre })}
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
      <Fragment key={generation}>{ecranOuvert()}</Fragment>
    </MiseEnPage>
  );
}
