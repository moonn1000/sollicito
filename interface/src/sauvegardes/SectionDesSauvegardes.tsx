import { useEffect, useRef, useState } from "react";
import type { ClientDeLApi, EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";
import type { IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { ANNONCE_DE_LA_COPIE, ANNONCE_DU_RETOUR, ANNONCES_DU_CHOIX, LIBELLES_DES_SORTES, texteDeLAlerte } from "./affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "pret"; etat: EtatDesSauvegardes } | { etape: "echec" };

/**
 * La section « Sauvegardes » des réglages (#26) : le dossier des copies, la dernière copie, les copies gardées, et ce
 * que l'utilisateur en fait. Elle dit, toujours, que les copies ne sont pas chiffrées. Le dossier ne se choisit que par
 * le dialogue de la fenêtre hôte : la page n'envoie jamais de chemin. Elle ne propose pas de restaurer une copie (#28).
 */
export function SectionDesSauvegardes({
  client,
  choisirLeDossier,
}: {
  client: ClientDeLApi;
  /** Ouvre le dialogue de dossier de la fenêtre hôte ; absent hors de la fenêtre. */
  choisirLeDossier?: () => Promise<IssueDuChoixDuDossier>;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [annonce, setAnnonce] = useState<string>();
  const [erreur, setErreur] = useState(false);
  const [enCours, setEnCours] = useState(false);
  // Garde du double clic : l'état de React ne change qu'au rendu suivant.
  const occupe = useRef(false);

  useEffect(() => {
    let affichee = true;
    client.lireLesSauvegardes().then(
      (etat) => {
        if (affichee) {
          setChargement({ etape: "pret", etat });
        }
      },
      () => {
        if (affichee) {
          setChargement({ etape: "echec" });
        }
      },
    );
    return () => {
      affichee = false;
    };
  }, [client]);

  // Une action à la fois : elle rend l'état à jour et l'annonce à faire.
  const agir = async (action: () => Promise<{ etat: EtatDesSauvegardes; annonce?: string }>) => {
    if (occupe.current) {
      return;
    }

    occupe.current = true;
    setEnCours(true);
    setAnnonce(undefined);
    setErreur(false);
    try {
      const { etat, annonce: texte } = await action();
      setChargement({ etape: "pret", etat });
      setAnnonce(texte);
    } catch {
      setErreur(true);
    } finally {
      occupe.current = false;
      setEnCours(false);
    }
  };

  const choisir = () =>
    agir(async () => {
      const issue = await choisirLeDossier!();
      return { etat: await client.lireLesSauvegardes(), annonce: ANNONCES_DU_CHOIX[issue] };
    });

  const revenir = () =>
    agir(async () => {
      const resultat = await client.revenirAuDossierParDefaut();
      return resultat.issue === "fait"
        ? { etat: resultat.etat, annonce: ANNONCE_DU_RETOUR }
        : { etat: await client.lireLesSauvegardes(), annonce: resultat.motif };
    });

  const sauvegarder = () =>
    agir(async () => {
      const etat = await client.sauvegarderMaintenant();
      return { etat, annonce: etat.echec === null ? ANNONCE_DE_LA_COPIE : undefined };
    });

  return (
    <section className="sauvegardes" aria-labelledby="titre-des-sauvegardes">
      <h3 id="titre-des-sauvegardes">Sauvegardes</h3>
      <p>
        Sollicito copie votre base une fois par jour d'utilisation, et refait la copie du jour à la fermeture. Il garde
        les sept dernières.
      </p>
      <p className="avertissement">
        Les copies ne sont pas chiffrées : toute personne qui accède à leur dossier peut les lire.
      </p>
      {chargement.etape === "chargement" && <p>Chargement des sauvegardes…</p>}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          Les sauvegardes n'ont pas pu être lues. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {chargement.etape === "pret" && (
        <ContenuDesSauvegardes
          etat={chargement.etat}
          annonce={annonce}
          erreur={erreur}
          enCours={enCours}
          choisir={choisirLeDossier === undefined ? undefined : choisir}
          revenir={revenir}
          sauvegarder={sauvegarder}
        />
      )}
    </section>
  );
}

function ContenuDesSauvegardes({
  etat,
  annonce,
  erreur,
  enCours,
  choisir,
  revenir,
  sauvegarder,
}: {
  etat: EtatDesSauvegardes;
  annonce?: string;
  erreur: boolean;
  enCours: boolean;
  choisir?: () => void;
  revenir: () => void;
  sauvegarder: () => void;
}) {
  const alerte = texteDeLAlerte(etat);
  return (
    <>
      {annonce !== undefined && (
        <p className="annonce" role="status">
          {annonce}
        </p>
      )}
      {erreur && (
        <p className="alerte" role="alert">
          L'action n'a pas abouti. Réessayez ; si rien ne change, fermez Sollicito, puis relancez-le.
        </p>
      )}
      {alerte !== undefined && (
        <p className="alerte" role="alert">
          {alerte}
        </p>
      )}
      <dl className="resume">
        <dt>Dossier des copies</dt>
        <dd className="chemin">{etat.dossier}</dd>
        <dt>Dernière copie</dt>
        <dd>
          {etat.derniereCopie === null ? (
            "Aucune copie pour l'instant."
          ) : (
            <time dateTime={etat.derniereCopie}>{afficherLInstant(etat.derniereCopie)}</time>
          )}
        </dd>
      </dl>
      {etat.parDefaut ? (
        <p className="aide">
          C'est le dossier par défaut, sur le même disque que votre base : il ne vous protège pas d'une panne de ce
          disque. Choisissez de préférence un dossier sur un autre support, comme une clé USB ou un disque externe.
        </p>
      ) : (
        <p className="aide">
          Les copies d'avant une mise à jour de Sollicito restent dans le dossier par défaut, sur le disque de votre
          base : <span className="chemin">{etat.dossierParDefaut}</span>.
        </p>
      )}
      <div className="actions">
        {choisir !== undefined && (
          <button type="button" disabled={enCours} onClick={choisir}>
            Choisir un autre dossier…
          </button>
        )}
        {!etat.parDefaut && (
          <button type="button" className="secondaire" disabled={enCours} onClick={revenir}>
            Revenir au dossier par défaut
          </button>
        )}
        <button type="button" className="secondaire" disabled={enCours} onClick={sauvegarder}>
          Sauvegarder maintenant
        </button>
      </div>
      {etat.copies.length === 0 ? (
        <p>Aucune copie gardée pour l'instant.</p>
      ) : (
        <table className="candidatures copies">
          <caption>Les copies gardées : celles du jour, les plus récentes d'abord, puis celles d'avant une mise à jour.</caption>
          <thead>
            <tr>
              <th scope="col">Faite le</th>
              <th scope="col">Copie</th>
              <th scope="col">Fichier</th>
            </tr>
          </thead>
          <tbody>
            {etat.copies.map((copie) => (
              <tr key={copie.nom}>
                <td>
                  <time dateTime={copie.faiteLe}>{afficherLInstant(copie.faiteLe)}</time>
                </td>
                <td>{LIBELLES_DES_SORTES[copie.sorte] ?? copie.sorte}</td>
                <td className="chemin">{copie.nom}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}
