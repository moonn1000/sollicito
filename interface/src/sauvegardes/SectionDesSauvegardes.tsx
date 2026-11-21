import { useEffect, useRef, useState } from "react";
import type { ClientDeLApi, CopieGardee, EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import { afficherLInstant } from "../candidatures/affichage.ts";
import type { IssueDeLaRestauration, IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import {
  ANNONCE_DE_LA_COPIE,
  ANNONCE_DU_RETOUR,
  ANNONCES_DE_LA_RESTAURATION,
  ANNONCES_DU_CHOIX,
  ATTENTE_DE_LA_CONFIRMATION,
  afficherLaDateDUneCopie,
  LIBELLES_DES_SORTES,
  RESTAURATION_EN_COURS,
  texteDeLAlerte,
} from "./affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "pret"; etat: EtatDesSauvegardes } | { etape: "echec" };

/** Où en est une restauration demandée depuis la section. */
type Restauration = { etape: "confirmation" } | { etape: "enCours" };

/** Le délai entre deux lectures de l'état, tant que la vérification du lancement n'est pas faite (#88). */
export const RELECTURE_AVANT_LA_VERIFICATION_EN_MS = 1_000;

/**
 * La section « Sauvegardes » des réglages (#26) : le dossier des copies, la dernière copie, les copies gardées, et ce
 * que l'utilisateur en fait. Elle dit, toujours, que les copies ne sont pas chiffrées. Le dossier ne se choisit que par
 * le dialogue de la fenêtre hôte : la page n'envoie jamais de chemin. Chaque copie se restaure (#28) : la page n'envoie
 * que son identifiant à l'hôte, qui demande lui-même le oui de l'utilisateur. Tant que la vérification du lancement
 * n'est pas faite, la section relit l'état et le dit (#88).
 */
export function SectionDesSauvegardes({
  client,
  choisirLeDossier,
  restaurer,
  quandRestauree,
}: {
  client: ClientDeLApi;
  /** Ouvre le dialogue de dossier de la fenêtre hôte ; absent hors de la fenêtre. */
  choisirLeDossier?: () => Promise<IssueDuChoixDuDossier>;
  /** Demande à la fenêtre hôte de restaurer une copie, par son identifiant ; absent hors de la fenêtre (#28). */
  restaurer?: (copie: string, quandEnCours: () => void) => Promise<IssueDeLaRestauration>;
  /** Une copie vient de remplacer la base : l'interface relit tout (#28). */
  quandRestauree?: (issue: "restauree" | "restaureeDepuisUneBaseAbimee", copie: CopieGardee) => void;
}) {
  const [chargement, setChargement] = useState<Chargement>({ etape: "chargement" });
  const [annonce, setAnnonce] = useState<string>();
  const [erreur, setErreur] = useState(false);
  const [enCours, setEnCours] = useState(false);
  const [restauration, setRestauration] = useState<Restauration>();
  // Garde du double clic : l'état de React ne change qu'au rendu suivant.
  const occupe = useRef(false);

  const verifie = chargement.etape === "pret" && chargement.etat.verifie;
  useEffect(() => {
    if (verifie) {
      return;
    }

    let affichee = true;
    let minuterie: ReturnType<typeof setTimeout> | undefined;
    const lire = () =>
      client.lireLesSauvegardes().then(
        (etat) => {
          if (!affichee) {
            return;
          }
          setChargement((avant) => (avant.etape === "pret" && avant.etat.verifie ? avant : { etape: "pret", etat }));
          if (!etat.verifie) {
            minuterie = setTimeout(lire, RELECTURE_AVANT_LA_VERIFICATION_EN_MS);
          }
        },
        () => {
          if (affichee) {
            setChargement((avant) => (avant.etape === "pret" ? avant : { etape: "echec" }));
          }
        },
      );
    void lire();
    return () => {
      affichee = false;
      clearTimeout(minuterie);
    };
  }, [client, verifie]);

  // Une action à la fois : elle rend l'état à jour et l'annonce à faire.
  const agir = async (action: () => Promise<{ etat: EtatDesSauvegardes; annonce?: string } | undefined>) => {
    if (occupe.current) {
      return;
    }

    occupe.current = true;
    setEnCours(true);
    setAnnonce(undefined);
    setErreur(false);
    try {
      const resultat = await action();
      if (resultat !== undefined) {
        setChargement({ etape: "pret", etat: resultat.etat });
        setAnnonce(resultat.annonce);
      }
    } catch {
      setErreur(true);
    } finally {
      occupe.current = false;
      setEnCours(false);
      setRestauration(undefined);
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

  const restaurerLaCopie = (copie: CopieGardee) =>
    agir(async () => {
      setRestauration({ etape: "confirmation" });
      const issue = await restaurer!(copie.id, () => setRestauration({ etape: "enCours" }));
      if (issue === "restauree" || issue === "restaureeDepuisUneBaseAbimee") {
        quandRestauree?.(issue, copie);
        return undefined;
      }

      return { etat: await client.lireLesSauvegardes(), annonce: ANNONCES_DE_LA_RESTAURATION[issue] };
    });

  return (
    <section className="sauvegardes" aria-labelledby="titre-des-sauvegardes" aria-busy={restauration !== undefined}>
      <h3 id="titre-des-sauvegardes">Sauvegardes</h3>
      <p>
        Sollicito copie votre base une fois par jour d'utilisation, et refait la copie du jour à la fermeture. Il garde
        les sept dernières. Vous pouvez revenir à l'une d'elles : Sollicito copie d'abord votre base actuelle.
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
          restauration={restauration}
          choisir={choisirLeDossier === undefined ? undefined : choisir}
          revenir={revenir}
          sauvegarder={sauvegarder}
          restaurer={restaurer === undefined ? undefined : restaurerLaCopie}
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
  restauration,
  choisir,
  revenir,
  sauvegarder,
  restaurer,
}: {
  etat: EtatDesSauvegardes;
  annonce?: string;
  erreur: boolean;
  enCours: boolean;
  restauration?: Restauration;
  choisir?: () => void;
  revenir: () => void;
  sauvegarder: () => void;
  restaurer?: (copie: CopieGardee) => void;
}) {
  const alerte = texteDeLAlerte(etat);
  // Les plus récentes d'abord, toutes sortes mêlées : on revient à un moment.
  const copies = [...etat.copies].sort((a, b) => Date.parse(b.faiteLe) - Date.parse(a.faiteLe));
  return (
    <>
      {restauration !== undefined && (
        <p className="annonce" role="status">
          {restauration.etape === "confirmation" ? ATTENTE_DE_LA_CONFIRMATION : RESTAURATION_EN_COURS}
        </p>
      )}
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
          {!etat.verifie ? (
            "Vérification des copies…"
          ) : etat.derniereCopie === null ? (
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
      {!etat.verifie ? null : copies.length === 0 ? (
        <p>Aucune copie gardée pour l'instant.</p>
      ) : (
        <table className="candidatures copies">
          <caption>Les copies gardées, des plus récentes aux plus anciennes.</caption>
          <thead>
            <tr>
              <th scope="col">Faite le</th>
              <th scope="col">Copie</th>
              <th scope="col">Fichier</th>
              {restaurer !== undefined && (
                <th scope="col">
                  <span className="visuellement-cache">Action</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {copies.map((copie) => (
              <tr key={copie.id}>
                <td>
                  <time dateTime={copie.faiteLe}>{afficherLInstant(copie.faiteLe)}</time>
                </td>
                <td>{LIBELLES_DES_SORTES[copie.sorte] ?? copie.sorte}</td>
                <td className="chemin">{copie.nom}</td>
                {restaurer !== undefined && (
                  <td>
                    <button
                      type="button"
                      className="secondaire"
                      disabled={enCours}
                      aria-label={`Restaurer la copie du ${afficherLaDateDUneCopie(copie.faiteLe)}`}
                      onClick={() => restaurer(copie)}
                    >
                      Restaurer…
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}
