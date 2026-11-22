import type { ReactNode } from "react";

/**
 * Les écrans de l'interface. La navigation propose le tableau de bord, la saisie d'une candidature, celle d'une offre
 * (#36) et les réglages (#26) ; la fiche d'une candidature s'ouvre depuis le tableau de bord, et sa modification depuis
 * la fiche (#25) : toutes deux désignent leur candidature par son identifiant. La fiche d'une offre s'ouvre après son
 * enregistrement, ou depuis un signal de doublon, et sa correction depuis la fiche : la liste des offres attend #38.
 */
export type Ecran =
  | { nom: "tableau" }
  | { nom: "saisie" }
  | { nom: "saisieDUneOffre" }
  | { nom: "reglages" }
  | { nom: "fiche"; candidature: number }
  | { nom: "modification"; candidature: number }
  | { nom: "offre"; offre: number }
  | { nom: "modificationDUneOffre"; offre: number };

const ENTREES: readonly { ecran: "tableau" | "saisie" | "saisieDUneOffre" | "reglages"; libelle: string }[] = [
  { ecran: "tableau", libelle: "Tableau de bord" },
  { ecran: "saisie", libelle: "Nouvelle candidature" },
  { ecran: "saisieDUneOffre", libelle: "Nouvelle offre" },
  { ecran: "reglages", libelle: "Réglages" },
];

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
                    aria-current={entree.ecran === ecran.nom ? "page" : undefined}
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
