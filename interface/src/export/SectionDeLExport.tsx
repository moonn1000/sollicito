import { useRef, useState } from "react";
import type { FormatDExport, IssueDeLExport } from "../hote/canalDeLHote.ts";
import {
  ATTENTE_DU_CHOIX,
  AVERTISSEMENT_DE_L_EXPORT,
  BOUTONS_DE_L_EXPORT,
  CONTENU_DE_L_EXPORT,
  estUneAnnonce,
  EXPORT_EN_COURS,
  FORMES_DE_L_EXPORT,
  texteDeLExport,
} from "./affichage.ts";

/** Où en est un export demandé depuis la section : en attente du dialogue de l'hôte, puis en cours. */
type Etape = "repos" | "choix" | "enCours";

const FORMATS: readonly FormatDExport[] = ["json"];

/**
 * La section « Exporter vos données » des réglages (#49, ADR-0020) : ce que contient l'export, qu'il n'est pas chiffré, et
 * un bouton par forme. Le fichier ne se choisit que dans le dialogue « Enregistrer sous » de la fenêtre hôte : la page
 * n'envoie que le format, jamais un chemin, et ne reçoit que l'issue. Hors de la fenêtre, aucun bouton.
 */
export function SectionDeLExport({
  exporter,
}: {
  /** Demande à la fenêtre hôte d'exporter dans ce format ; absent hors de la fenêtre. */
  exporter?: (format: FormatDExport, quandEnCours: () => void) => Promise<IssueDeLExport>;
}) {
  const [etape, setEtape] = useState<Etape>("repos");
  const [issue, setIssue] = useState<{ issue: IssueDeLExport; format: FormatDExport }>();
  // Garde du double clic : l'état de React ne change qu'au rendu suivant.
  const occupe = useRef(false);

  const lancer = async (format: FormatDExport) => {
    if (exporter === undefined || occupe.current) {
      return;
    }

    occupe.current = true;
    setIssue(undefined);
    setEtape("choix");
    try {
      const resultat = await exporter(format, () => setEtape("enCours"));
      setIssue({ issue: resultat, format });
    } finally {
      occupe.current = false;
      setEtape("repos");
    }
  };

  return (
    <section className="sauvegardes" aria-labelledby="titre-de-l-export" aria-busy={etape !== "repos"}>
      <h3 id="titre-de-l-export">Exporter vos données</h3>
      <p>{CONTENU_DE_L_EXPORT}</p>
      <p className="avertissement">{AVERTISSEMENT_DE_L_EXPORT}</p>
      <p className="aide">{FORMES_DE_L_EXPORT}</p>
      {etape !== "repos" && (
        <p className="annonce" role="status">
          {etape === "choix" ? ATTENTE_DU_CHOIX : EXPORT_EN_COURS}
        </p>
      )}
      {issue !== undefined &&
        (estUneAnnonce(issue.issue) ? (
          <p className="annonce" role="status">
            {texteDeLExport(issue.issue, issue.format)}
          </p>
        ) : (
          <p className="alerte" role="alert">
            {texteDeLExport(issue.issue, issue.format)}
          </p>
        ))}
      {exporter !== undefined && (
        <div className="actions">
          {FORMATS.map((format) => (
            <button key={format} type="button" disabled={etape !== "repos"} onClick={() => void lancer(format)}>
              {BOUTONS_DE_L_EXPORT[format]}
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
