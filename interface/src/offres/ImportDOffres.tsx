import { useEffect, useRef, useState, type ChangeEvent } from "react";
import {
  FICHIER_TROP_GROS,
  TAILLE_MAXIMALE_DU_FICHIER,
  type ClientDeLApi,
  type CompteRenduDUnFichier,
  type LigneDuCompteRendu,
} from "../api/clientDeLApi.ts";
import { ApercuDUneOffre } from "./ApercuDUneOffre.tsx";
import {
  annonceDeLImport,
  decrireLesComptes,
  decrireLesDoublons,
  decrireLesMotifs,
  decrireLeSort,
  EN_TETE_DU_FORMAT,
  type MomentDuCompteRendu,
} from "./affichageDeLImport.ts";

type Etat =
  | { etape: "choix" }
  | { etape: "examen" }
  | { etape: "apercu"; contenu: ArrayBuffer; compteRendu: CompteRenduDUnFichier; gardees: ReadonlySet<number> }
  | { etape: "import"; contenu: ArrayBuffer; compteRendu: CompteRenduDUnFichier; gardees: ReadonlySet<number> }
  | { etape: "fait"; compteRendu: CompteRenduDUnFichier; different: boolean }
  | { etape: "refuse"; motif: string; pendantLImport: boolean }
  | { etape: "echec"; pendantLImport: boolean };

const ECHEC_DE_L_EXAMEN = "Le fichier n'a pas pu être examiné. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";
const ECHEC_DE_L_IMPORT = "L'import n'a pas pu se faire : rien n'a été importé. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.";

/**
 * L'écran « Importer des offres » (#40, ADR-0015) : on choisit un fichier CSV ; la page en lit les octets, que l'API
 * reçoit seuls, sans chemin ni nom de fichier. L'API montre d'abord, dans un aperçu qui n'écrit rien, ce qu'elle ferait
 * de chaque ligne ; l'utilisateur garde ou non chaque doublon possible, puis confirme, et l'API importe, en une seule
 * transaction. Les textes s'affichent comme du texte, jamais comme du HTML : React les échappe.
 */
export function ImportDOffres({
  client,
  revenir,
  ouvrirLOffre,
}: {
  client: ClientDeLApi;
  /** Revient à la liste des offres. */
  revenir: () => void;
  /** Ouvre la fiche de l'offre de cet identifiant, depuis le compte rendu. */
  ouvrirLOffre: (offre: number) => void;
}) {
  const [etat, setEtat] = useState<Etat>({ etape: "choix" });
  const [apercu, setApercu] = useState<number>();
  const titre = useRef<HTMLHeadingElement>(null);
  const champ = useRef<HTMLInputElement>(null);
  const suite = useRef<HTMLHeadingElement>(null);
  // Le dernier fichier choisi : la réponse d'un fichier choisi avant lui ne l'écrase pas.
  const choix = useRef(0);

  useEffect(() => {
    titre.current?.focus();
  }, []);

  useEffect(() => {
    if (etat.etape === "apercu" || etat.etape === "fait") {
      suite.current?.focus();
    }
  }, [etat.etape]);

  const choisir = async (evenement: ChangeEvent<HTMLInputElement>) => {
    const fichier = evenement.target.files?.[0];
    const numero = ++choix.current;
    setApercu(undefined);
    if (fichier === undefined) {
      return;
    }

    // Le même fichier, corrigé, peut se choisir de nouveau, même si sa lecture échoue.
    evenement.target.value = "";

    // Refusé avant d'être lu : un fichier trop gros ne passe pas en mémoire pour rien.
    if (fichier.size > TAILLE_MAXIMALE_DU_FICHIER) {
      setEtat({ etape: "refuse", motif: FICHIER_TROP_GROS, pendantLImport: false });
      return;
    }

    setEtat({ etape: "examen" });
    try {
      const contenu = await fichier.arrayBuffer();
      const resultat = await client.examinerUnFichierCsv(contenu);
      if (numero !== choix.current) {
        return;
      }

      setEtat(
        resultat.issue === "lu"
          ? { etape: "apercu", contenu, compteRendu: resultat.compteRendu, gardees: new Set() }
          : { etape: "refuse", motif: resultat.motif, pendantLImport: false },
      );
    } catch {
      if (numero === choix.current) {
        setEtat({ etape: "echec", pendantLImport: false });
      }
    }
  };

  const garder = (ligne: number, garde: boolean) => {
    if (etat.etape !== "apercu") {
      return;
    }

    const gardees = new Set(etat.gardees);
    if (garde) {
      gardees.add(ligne);
    } else {
      gardees.delete(ligne);
    }

    setEtat({ ...etat, gardees });
  };

  const importer = async () => {
    if (etat.etape !== "apercu") {
      return;
    }

    const { contenu, compteRendu, gardees } = etat;
    const numero = ++choix.current;
    setApercu(undefined);
    setEtat({ etape: "import", contenu, compteRendu, gardees });
    try {
      const resultat = await client.importerUnFichierCsv(contenu, [...gardees].sort((a, b) => a - b));
      if (numero !== choix.current) {
        return;
      }

      setEtat(
        resultat.issue === "lu"
          ? { etape: "fait", compteRendu: resultat.compteRendu, different: differeDeLApercu(compteRendu, gardees, resultat.compteRendu) }
          : { etape: "refuse", motif: resultat.motif, pendantLImport: true },
      );
    } catch {
      if (numero === choix.current) {
        setEtat({ etape: "echec", pendantLImport: true });
      }
    }
  };

  const occupe = etat.etape === "examen" || etat.etape === "import";
  const aImporter =
    etat.etape === "apercu" || etat.etape === "import"
      ? etat.compteRendu.comptes.aImporter + etat.compteRendu.lignes.filter((ligne) => ligne.sort === "doublonPossible" && etat.gardees.has(ligne.ligne)).length
      : 0;

  return (
    <section aria-labelledby="titre-de-l-import">
      <h2 id="titre-de-l-import" ref={titre} tabIndex={-1}>
        Importer des offres
      </h2>
      <p>
        Choisissez un fichier CSV d'offres. Sollicito vous montre d'abord ce qu'il en ferait, ligne par ligne ; puis il importe, toutes
        ensemble, les offres que vous confirmez.
      </p>
      <section aria-labelledby="titre-du-format" className="format-du-fichier">
        <h3 id="titre-du-format">Le format du fichier</h3>
        <p>
          La première ligne nomme les colonnes : poste, obligatoire ; entreprise, lieu, publieeLe, adresse et texte, facultatives, dans l'ordre
          que vous voulez. Par exemple :
        </p>
        <pre>
          <code>{EN_TETE_DU_FORMAT}</code>
        </pre>
        <ul>
          <li>Encodage UTF-8 (dans Excel : « CSV UTF-8 »), séparateur ; ou , ; 2 Mo et 1 000 offres au plus.</li>
          <li>La date de publication s'écrit JJ/MM/AAAA ou AAAA-MM-JJ, par exemple 18/11/2026.</li>
          <li>Une cellule qui contient le séparateur, un guillemet ou un saut de ligne s'écrit entre guillemets ; un guillemet s'y double.</li>
        </ul>
      </section>
      <div className="champ">
        <label htmlFor="fichier-csv">Fichier CSV</label>
        <input id="fichier-csv" ref={champ} type="file" accept=".csv,text/csv" disabled={occupe} onChange={(evenement) => void choisir(evenement)} />
      </div>

      {etat.etape === "examen" && <p role="status">Examen du fichier…</p>}

      {(etat.etape === "refuse" || etat.etape === "echec") && (
        <>
          <p className="alerte" role="alert">
            {etat.etape === "refuse"
              ? `${etat.motif}${etat.pendantLImport ? " Rien n'a été importé." : ""}`
              : etat.pendantLImport
                ? ECHEC_DE_L_IMPORT
                : ECHEC_DE_L_EXAMEN}
          </p>
          <div className="actions">
            <button type="button" onClick={() => champ.current?.click()}>
              Choisir un autre fichier
            </button>
            <button type="button" className="secondaire" onClick={revenir}>
              Annuler
            </button>
          </div>
        </>
      )}

      {(etat.etape === "apercu" || etat.etape === "import") && (
        <section aria-labelledby="titre-de-l-apercu">
          <h3 id="titre-de-l-apercu" ref={suite} tabIndex={-1}>
            Aperçu
          </h3>
          <ul className="comptes-de-l-import">
            {decrireLesComptes(etat.compteRendu.comptes, "apercu").map((compte) => (
              <li key={compte}>{compte}</li>
            ))}
          </ul>
          <TableauDesLignes
            compteRendu={etat.compteRendu}
            moment="apercu"
            gardees={etat.gardees}
            garder={etat.etape === "apercu" ? garder : undefined}
            voir={setApercu}
          />
          {apercu !== undefined && <ApercuDUneOffre client={client} offre={apercu} masquer={() => setApercu(undefined)} />}
          {aImporter === 0 && <p>Aucune offre à importer.</p>}
          <div className="actions">
            <button type="button" disabled={aImporter === 0 || etat.etape === "import"} onClick={() => void importer()}>
              {etat.etape === "import" ? "Import…" : aImporter === 0 ? "Importer" : aImporter === 1 ? "Importer 1 offre" : `Importer ${aImporter} offres`}
            </button>
            <button type="button" className="secondaire" disabled={etat.etape === "import"} onClick={() => champ.current?.click()}>
              Choisir un autre fichier
            </button>
            <button type="button" className="secondaire" disabled={etat.etape === "import"} onClick={revenir}>
              Annuler
            </button>
          </div>
        </section>
      )}

      {etat.etape === "fait" && (
        <section aria-labelledby="titre-du-compte-rendu">
          <h3 id="titre-du-compte-rendu" ref={suite} tabIndex={-1}>
            Compte rendu
          </h3>
          <p className="annonce" role="status">
            {annonceDeLImport(etat.compteRendu.comptes)}
          </p>
          {etat.different && <p>La base a changé depuis l'aperçu : voici ce qui a été fait.</p>}
          <ul className="comptes-de-l-import">
            {decrireLesComptes(etat.compteRendu.comptes, "import").map((compte) => (
              <li key={compte}>{compte}</li>
            ))}
          </ul>
          <TableauDesLignes compteRendu={etat.compteRendu} moment="import" ouvrirLOffre={ouvrirLOffre} voir={setApercu} />
          {apercu !== undefined && <ApercuDUneOffre client={client} offre={apercu} masquer={() => setApercu(undefined)} />}
          <div className="actions">
            <button type="button" onClick={revenir}>
              Revenir aux offres
            </button>
          </div>
        </section>
      )}
    </section>
  );
}

/** Le tableau des lignes d'un compte rendu : le numéro, les champs lus d'une ligne admise, et son sort. */
function TableauDesLignes({
  compteRendu,
  moment,
  gardees,
  garder,
  ouvrirLOffre,
  voir,
}: {
  compteRendu: CompteRenduDUnFichier;
  moment: MomentDuCompteRendu;
  gardees?: ReadonlySet<number>;
  /** Garde ou non le doublon possible d'une ligne ; rien pendant l'import. */
  garder?: (ligne: number, garde: boolean) => void;
  ouvrirLOffre?: (offre: number) => void;
  /** Ouvre sur place l'aperçu d'une offre enregistrée. */
  voir: (offre: number) => void;
}) {
  return (
    <table className="candidatures lignes-du-fichier">
      <caption>{moment === "apercu" ? "Ce que Sollicito ferait de chaque ligne." : "Ce que Sollicito a fait de chaque ligne."}</caption>
      <thead>
        <tr>
          <th scope="col">Ligne</th>
          <th scope="col">Poste</th>
          <th scope="col">Entreprise</th>
          <th scope="col">Lieu</th>
          <th scope="col">Sort</th>
        </tr>
      </thead>
      <tbody>
        {compteRendu.lignes.map((ligne) => (
          <tr key={ligne.ligne}>
            <td>{ligne.ligne}</td>
            <td>{ligne.poste}</td>
            <td>{ligne.entreprise}</td>
            <td>{ligne.lieu}</td>
            <td>
              <SortDUneLigne ligne={ligne} moment={moment} gardees={gardees} garder={garder} ouvrirLOffre={ouvrirLOffre} voir={voir} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function SortDUneLigne({
  ligne,
  moment,
  gardees,
  garder,
  ouvrirLOffre,
  voir,
}: {
  ligne: LigneDuCompteRendu;
  moment: MomentDuCompteRendu;
  gardees?: ReadonlySet<number>;
  garder?: (ligne: number, garde: boolean) => void;
  ouvrirLOffre?: (offre: number) => void;
  voir: (offre: number) => void;
}) {
  const motifs = decrireLesMotifs(ligne);
  const doublons = decrireLesDoublons(ligne);
  const { trouvee, offre } = ligne;
  return (
    <>
      <span className="sort-d-une-ligne">{decrireLeSort(ligne, moment)}</span>
      {motifs.length > 0 && (
        <ul className="motifs-d-une-ligne">
          {motifs.map((motif) => (
            <li key={motif}>{motif}</li>
          ))}
        </ul>
      )}
      {doublons.length > 0 && (
        <>
          <span>{moment === "apercu" ? " de :" : " Semblable à :"}</span>
          <ul className="doublons-d-une-ligne">
            {doublons.map((doublon, rang) => (
              // Deux offres d'un même lot peuvent se décrire de même : même poste, même lieu, même instant d'ajout.
              <li key={rang}>{doublon}</li>
            ))}
          </ul>
          {(ligne.doublons ?? []).map((enBase, rang, enBases) => (
            <button
              key={enBase.id}
              type="button"
              className="lien"
              aria-label={`Voir l'offre ${enBase.poste}, chez ${enBase.entreprise.nom}${enBases.length > 1 ? ` (${rang + 1} sur ${enBases.length})` : ""}`}
              onClick={() => voir(enBase.id)}
            >
              Voir cette offre
            </button>
          ))}
        </>
      )}
      {ligne.sort === "dejaEnregistree" && trouvee !== null && (
        <button type="button" className="lien" aria-label={`Voir l'offre enregistrée de la ligne ${ligne.ligne}`} onClick={() => voir(trouvee.id)}>
          Voir l'offre enregistrée
        </button>
      )}
      {ligne.sort === "doublonPossible" && moment === "apercu" && gardees !== undefined && (
        <label className="garder-la-ligne">
          <input
            type="checkbox"
            checked={gardees.has(ligne.ligne)}
            disabled={garder === undefined}
            aria-label={`Garder aussi l'offre de la ligne ${ligne.ligne}`}
            onChange={(evenement) => garder?.(ligne.ligne, evenement.target.checked)}
          />{" "}
          Garder aussi cette offre
        </label>
      )}
      {ligne.sort === "importee" && offre !== null && ouvrirLOffre !== undefined && (
        <button type="button" className="lien" aria-label={`Voir l'offre importée de la ligne ${ligne.ligne}`} onClick={() => ouvrirLOffre(offre)}>
          Voir l'offre
        </button>
      )}
    </>
  );
}

// Vrai si l'import n'a pas fait ce que l'aperçu annonçait : une ligne à importer, ou gardée, qui ne l'est pas, ou une
// ligne dont le sort a changé depuis.
function differeDeLApercu(apercu: CompteRenduDUnFichier, gardees: ReadonlySet<number>, fait: CompteRenduDUnFichier): boolean {
  const attendus = new Map(
    apercu.lignes.map((ligne) => [
      ligne.ligne,
      ligne.sort === "aImporter" || (ligne.sort === "doublonPossible" && gardees.has(ligne.ligne)) ? "importee" : ligne.sort,
    ]),
  );
  return fait.lignes.length !== apercu.lignes.length || fait.lignes.some((ligne) => attendus.get(ligne.ligne) !== ligne.sort);
}
