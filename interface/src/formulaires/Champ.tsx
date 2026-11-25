import type { ReactNode } from "react";

/** Les attributs qu'un champ de formulaire reçoit de son cadre : son nom, et son aide et ses refus comme description. */
export interface AttributsDuChamp {
  id: string;
  name: string;
  "aria-invalid"?: true;
  "aria-describedby"?: string;
}

/**
 * Un champ de formulaire, son libellé, une aide facultative et ses refus, que le champ désigne comme sa description
 * (`aria-describedby`) : l'écran de saisie d'une candidature (#11), sa modification (#25) et la note d'une étape (#12)
 * s'en servent. L'aide et le refus s'affichent comme du texte, à côté du champ.
 */
export function Champ({
  id,
  libelle,
  aide,
  marque,
  refus,
  children,
}: {
  id: string;
  libelle: string;
  /** Ce qu'il faut savoir avant de remplir le champ, par exemple une limite. */
  aide?: string;
  /** Ce qui distingue sa valeur, par exemple une valeur que l'IA a proposée et qu'il faut relire (#46). */
  marque?: string;
  refus: string[] | undefined;
  children: (attributs: AttributsDuChamp) => ReactNode;
}) {
  const refuse = refus !== undefined && refus.length > 0;
  const idDeLAide = `${id}-aide`;
  const idDeLaMarque = `${id}-marque`;
  const idDuRefus = `${id}-refus`;
  const description = [aide === undefined ? undefined : idDeLAide, marque === undefined ? undefined : idDeLaMarque, refuse ? idDuRefus : undefined]
    .filter(Boolean)
    .join(" ");
  return (
    <div className={refuse ? "champ champ-refuse" : "champ"}>
      <label htmlFor={id}>{libelle}</label>
      {children({
        id,
        name: id,
        ...(refuse ? { "aria-invalid": true } : {}),
        ...(description.length > 0 ? { "aria-describedby": description } : {}),
      })}
      {aide !== undefined && (
        <p className="aide" id={idDeLAide}>
          {aide}
        </p>
      )}
      {marque !== undefined && (
        <p className="marque" id={idDeLaMarque}>
          {marque}
        </p>
      )}
      {refuse && (
        <p className="refus" id={idDuRefus}>
          {refus.join(" ")}
        </p>
      )}
    </div>
  );
}

/** Les refus sans ceux d'un champ : l'utilisateur vient de le modifier, et ses anciens refus ne le décrivent plus. */
export function sansLeChamp<Champs extends string>(
  refus: Partial<Record<Champs, string[]>>,
  champ: Champs,
): Partial<Record<Champs, string[]>> {
  if (refus[champ] === undefined) {
    return refus;
  }

  const reste = { ...refus };
  delete reste[champ];
  return reste;
}
