import type { ReactNode } from "react";

/** Les attributs qu'un champ de formulaire reçoit de son cadre : son nom, et ses refus comme description. */
export interface AttributsDuChamp {
  id: string;
  name: string;
  "aria-invalid"?: true;
  "aria-describedby"?: string;
}

/**
 * Un champ de formulaire, son libellé et ses refus, que le champ désigne comme sa description (`aria-describedby`) :
 * l'écran de saisie d'une candidature (#11) et la note d'une étape (#12) s'en servent. Le refus s'affiche comme du
 * texte, à côté du champ.
 */
export function Champ({
  id,
  libelle,
  refus,
  children,
}: {
  id: string;
  libelle: string;
  refus: string[] | undefined;
  children: (attributs: AttributsDuChamp) => ReactNode;
}) {
  const refuse = refus !== undefined && refus.length > 0;
  const idDuRefus = `${id}-refus`;
  return (
    <div className={refuse ? "champ champ-refuse" : "champ"}>
      <label htmlFor={id}>{libelle}</label>
      {children({
        id,
        name: id,
        ...(refuse ? { "aria-invalid": true, "aria-describedby": idDuRefus } : {}),
      })}
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
