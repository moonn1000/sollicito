import type { ReactNode } from "react";

/**
 * L'adresse d'un lien externe, si elle est admise : une adresse absolue en `http:` ou `https:`, avec un hôte, sans nom
 * d'utilisateur ni mot de passe, forme qui sert à tromper sur la destination (ADR-0006). L'analyseur d'adresses du
 * navigateur la lit, comme il la lirait pour l'ouvrir ; rien sinon.
 */
export function adresseDuWeb(adresse: string): string | undefined {
  let lue: URL;
  try {
    lue = new URL(adresse);
  } catch {
    return undefined;
  }

  const duWeb = lue.protocol === "http:" || lue.protocol === "https:";
  return duWeb && lue.hostname.length > 0 && lue.username === "" && lue.password === "" ? lue.href : undefined;
}

/**
 * Le seul lien externe de l'interface (ADR-0006, ADR-0013), comme l'adresse de l'annonce d'une offre. Il s'ouvre en
 * nouvelle fenêtre (`target="_blank"`) : la fenêtre hôte l'ouvre alors dans le navigateur par défaut, sur le clic de
 * l'utilisateur seulement ; un lien cliqué sur place serait une navigation, annulée, qui ne mènerait nulle part.
 * L'adresse se revalide au rendu, quelle que soit sa provenance (une base restaurée peut porter n'importe quoi) : une
 * adresse refusée s'affiche en texte, sans lien. Le texte du lien est l'adresse elle-même, sauf texte donné : on voit
 * où il mène. Comme tout texte, React l'échappe.
 */
export function LienExterne({ adresse, children }: { adresse: string; children?: ReactNode }) {
  const lien = adresseDuWeb(adresse);
  if (lien === undefined) {
    return <span className="lien-refuse">{children ?? adresse}</span>;
  }

  return (
    <a className="lien-externe" href={lien} target="_blank" rel="noopener noreferrer">
      {children ?? adresse}
    </a>
  );
}
