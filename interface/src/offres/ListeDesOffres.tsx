import { useEffect, useMemo, useRef, useState } from "react";
import type { ClientDeLApi, OffreListee, SourceDOffre } from "../api/clientDeLApi.ts";
import { afficherLInstant, LIBELLES_DES_STATUTS } from "../candidatures/affichage.ts";
import {
  AUCUN_FILTRE,
  filtreActif,
  filtrerLesOffres,
  LIBELLES_DES_PERIODES,
  LIBELLES_DES_SOURCES,
  PERIODES_D_AJOUT,
  RECHERCHE_AU_PLUS,
  type FiltresDesOffres,
  type PeriodeDAjout,
} from "./affichage.ts";

type Chargement = { etape: "chargement" } | { etape: "prete"; offres: OffreListee[] } | { etape: "echec" };

const SOURCES = Object.keys(LIBELLES_DES_SOURCES) as SourceDOffre[];

/**
 * L'écran « Offres » (#38) : toutes les offres, les plus récemment ajoutées d'abord, dans l'ordre que rend l'API, avec
 * leur poste, leur entreprise, leur lieu, leur date d'ajout et leur candidature. Le poste ouvre la fiche de l'offre, le
 * statut celle de sa candidature. Les filtres, par texte, par source et par date d'ajout, se font ici, sur la liste déjà
 * lue : rien de la recherche ne quitte la page, ni dans une adresse, ni dans un corps. Les textes s'affichent comme du
 * texte, jamais comme du HTML : React les échappe.
 */
export function ListeDesOffres({
  client,
  annonce,
  filtres,
  changerLesFiltres,
  collerUneOffre,
  importerUnFichier,
  ouvrirLOffre,
  ouvrirLaCandidature,
  prendreLeFocus = false,
  maintenant = () => new Date(),
}: {
  client: ClientDeLApi;
  /** Ce qui vient de se passer. */
  annonce?: string;
  /** Les filtres, que l'application garde le temps de la session, en mémoire seulement. */
  filtres: FiltresDesOffres;
  changerLesFiltres: (filtres: FiltresDesOffres) => void;
  /** Ouvre l'écran « Coller une offre ». */
  collerUneOffre: () => void;
  /** Ouvre l'écran « Importer des offres » (#40). */
  importerUnFichier: () => void;
  /** Ouvre la fiche de l'offre de cet identifiant. */
  ouvrirLOffre: (offre: number) => void;
  /** Ouvre la fiche de la candidature de cet identifiant. */
  ouvrirLaCandidature: (candidature: number) => void;
  /** Vrai quand l'utilisateur vient d'y être mené : son titre reçoit alors le focus. */
  prendreLeFocus?: boolean;
  /** L'heure présente, qui situe les derniers jours du filtre par date d'ajout. */
  maintenant?: () => Date;
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
    let affichee = true;
    client.listerLesOffres().then(
      (offres) => {
        if (affichee) {
          setChargement({ etape: "prete", offres });
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

  const offres = chargement.etape === "prete" ? chargement.offres : [];
  const visibles = useMemo(() => filtrerLesOffres(offres, filtres, maintenant()), [offres, filtres]);
  const changer = (modification: Partial<FiltresDesOffres>) => changerLesFiltres({ ...filtres, ...modification });

  return (
    <section aria-labelledby="titre-des-offres">
      <h2 id="titre-des-offres" ref={titre} tabIndex={-1}>
        Offres
      </h2>
      {annonce !== undefined && (
        <p className="annonce" role="status">
          {annonce}
        </p>
      )}
      <div className="actions">
        <button type="button" onClick={collerUneOffre}>
          Coller une offre
        </button>
        <button type="button" onClick={importerUnFichier}>
          Importer un fichier CSV
        </button>
      </div>
      {chargement.etape === "chargement" && <p>Chargement des offres…</p>}
      {chargement.etape === "echec" && (
        <p className="alerte" role="alert">
          Les offres n'ont pas pu être chargées. Fermez Sollicito, puis relancez-le.
        </p>
      )}
      {chargement.etape === "prete" && offres.length === 0 && (
        <div className="vide">
          <p>Aucune offre enregistrée pour l'instant.</p>
        </div>
      )}
      {chargement.etape === "prete" && offres.length > 0 && (
        <>
          <form className="filtres-des-offres" role="search" aria-label="Filtrer les offres" onSubmit={(evenement) => evenement.preventDefault()}>
            <div className="champ">
              <label htmlFor="recherche-des-offres">Rechercher</label>
              <input
                id="recherche-des-offres"
                type="search"
                autoComplete="off"
                spellCheck={false}
                maxLength={RECHERCHE_AU_PLUS}
                aria-describedby="recherche-des-offres-aide"
                value={filtres.texte}
                onChange={(evenement) => changer({ texte: evenement.target.value })}
              />
              <p className="aide" id="recherche-des-offres-aide">
                Dans le poste, l'entreprise et le lieu.
              </p>
            </div>
            <div className="champ">
              <label htmlFor="source-des-offres">Source</label>
              <select
                id="source-des-offres"
                value={filtres.source}
                onChange={(evenement) => changer({ source: evenement.target.value as FiltresDesOffres["source"] })}
              >
                <option value="toutes">Toutes les sources</option>
                {SOURCES.map((source) => (
                  <option key={source} value={source}>
                    {LIBELLES_DES_SOURCES[source]}
                  </option>
                ))}
              </select>
            </div>
            <div className="champ">
              <label htmlFor="periode-des-offres">Ajoutées</label>
              <select
                id="periode-des-offres"
                value={filtres.periode}
                onChange={(evenement) => changer({ periode: evenement.target.value as PeriodeDAjout })}
              >
                {PERIODES_D_AJOUT.map((periode) => (
                  <option key={periode} value={periode}>
                    {LIBELLES_DES_PERIODES[periode]}
                  </option>
                ))}
              </select>
            </div>
            {filtreActif(filtres) && (
              <div className="actions">
                <button type="button" className="secondaire" onClick={() => changerLesFiltres(AUCUN_FILTRE)}>
                  Effacer les filtres
                </button>
              </div>
            )}
          </form>
          <p className="compte-des-offres" role="status">
            {compter(visibles.length, offres.length)}
          </p>
          {visibles.length === 0 ? (
            <p>Aucune offre ne correspond à ces filtres.</p>
          ) : (
            <table className="candidatures offres">
              <caption>Vos offres, les plus récemment ajoutées d'abord. Choisissez un poste pour ouvrir sa fiche.</caption>
              <thead>
                <tr>
                  <th scope="col">Poste</th>
                  <th scope="col">Entreprise</th>
                  <th scope="col">Lieu</th>
                  <th scope="col">Ajoutée le</th>
                  <th scope="col">Candidature</th>
                </tr>
              </thead>
              <tbody>
                {visibles.map((offre) => (
                  <tr key={offre.id}>
                    <td>
                      <button type="button" className="lien" aria-label={`${decrireLOffre(offre)} : ouvrir la fiche`} onClick={() => ouvrirLOffre(offre.id)}>
                        {offre.poste}
                      </button>
                    </td>
                    <td>{offre.entreprise?.nom ?? "Entreprise non indiquée"}</td>
                    <td>{offre.lieu}</td>
                    <td>
                      <time dateTime={offre.ajouteeLe}>{afficherLInstant(offre.ajouteeLe)}</time>
                    </td>
                    <td>{celluleDeLaCandidature(offre, ouvrirLaCandidature)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}

// « 12 offres », ou, sous un filtre, « 3 offres sur 12 ».
function compter(visibles: number, toutes: number): string {
  const offres = (nombre: number) => (nombre <= 1 ? `${nombre} offre` : `${nombre} offres`);
  return visibles === toutes ? offres(toutes) : `${offres(visibles)} sur ${toutes}`;
}

// « Aucune », ou le statut de la candidature, qui ouvre sa fiche.
function celluleDeLaCandidature(offre: OffreListee, ouvrirLaCandidature: (candidature: number) => void) {
  const { candidature } = offre;
  if (candidature === null) {
    return "Aucune";
  }

  const statut = LIBELLES_DES_STATUTS[candidature.statut] ?? candidature.statut;
  return (
    <button
      type="button"
      className="lien"
      aria-label={`Candidature ${statut} pour ${decrireLOffre(offre)} : ouvrir sa fiche`}
      onClick={() => ouvrirLaCandidature(candidature.id)}
    >
      {statut}
    </button>
  );
}

// Le poste, et l'entreprise si l'offre en a une : ce que disent les boutons d'une ligne.
function decrireLOffre(offre: OffreListee): string {
  return offre.entreprise === null ? offre.poste : `${offre.poste}, chez ${offre.entreprise.nom}`;
}
