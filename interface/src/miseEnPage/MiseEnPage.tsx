import type { ReactNode } from "react";

/**
 * Les écrans de l'interface. La navigation propose le tableau de bord, la saisie d'une candidature, la liste des offres
 * (#38) et les réglages (#26) ; la fiche d'une candidature s'ouvre depuis le tableau de bord, et sa modification depuis
 * la fiche (#25) : toutes deux désignent leur candidature par son identifiant. La liste des offres mène à « Coller une
 * offre » (#36) et à la fiche d'une offre, qui mène à sa correction et à « Postuler » (#38).
 */
export type Ecran =
  | { nom: "tableau" }
  | { nom: "saisie" }
  | { nom: "offres" }
  | { nom: "saisieDUneOffre" }
  | { nom: "importDOffres" }
  | { nom: "reglages" }
  | { nom: "fiche"; candidature: number }
  | { nom: "modification"; candidature: number }
  | { nom: "offre"; offre: number }
  | { nom: "modificationDUneOffre"; offre: number }
  | { nom: "postuler"; offre: number };

type Entree = "tableau" | "saisie" | "offres" | "reglages";

const ENTREES: readonly { ecran: Entree; libelle: string }[] = [
  { ecran: "tableau", libelle: "Tableau de bord" },
  { ecran: "saisie", libelle: "Nouvelle candidature" },
  { ecran: "offres", libelle: "Offres" },
  { ecran: "reglages", libelle: "Réglages" },
];

// L'entrée de la navigation où l'on se trouve : « Coller une offre » et « Importer des offres » s'ouvrent depuis la liste
// des offres.
function entreeDe(ecran: Ecran): string {
  return ecran.nom === "saisieDUneOffre" || ecran.nom === "importDOffres" ? "offres" : ecran.nom;
}

/**
 * La mise en page commune des écrans (absorbe #24) : l'en-tête et sa navigation, la zone de contenu, et l'état de la
 * connexion à l'API locale. La navigation change d'écran sans changer d'adresse : la fenêtre ne sert que les fichiers
 * de l'interface, et une autre adresse ne mènerait nulle part. Elle n'apparaît qu'une fois l'API jointe.
 */
export function MiseEnPage({
  ecran,
  naviguer,
  etatDeLaConnexion,
  children,
}: {
  ecran: Ecran;
  /** Change d'écran ; absent tant que l'API n'est pas jointe. */
  naviguer?: (ecran: Ecran) => void;
  etatDeLaConnexion: string;
  children?: ReactNode;
}) {
  return (
    <div className="mise-en-page">
      <header className="en-tete">
        <h1>Sollicito</h1>
        {naviguer !== undefined && (
          <nav aria-label="Navigation principale">
            <ul>
              {ENTREES.map((entree) => (
                <li key={entree.ecran}>
                  <button
                    type="button"
                    aria-current={entree.ecran === entreeDe(ecran) ? "page" : undefined}
                    onClick={() => naviguer({ nom: entree.ecran })}
                  >
                    {entree.libelle}
                  </button>
                </li>
              ))}
            </ul>
          </nav>
        )}
      </header>
      <main className="contenu">{children}</main>
      <footer className="pied">
        <p role="status">{etatDeLaConnexion}</p>
      </footer>
    </div>
  );
}
