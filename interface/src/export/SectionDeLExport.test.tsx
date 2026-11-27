import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { FormatDExport, IssueDeLExport } from "../hote/canalDeLHote.ts";
import { SectionDeLExport } from "./SectionDeLExport.tsx";

const CONTENU =
  "L'export rassemble toutes les données de votre base : entreprises, contacts, offres, candidatures, leur historique et leurs relances. Il ne contient ni votre clé d'IA ni vos réglages.";
const AVERTISSEMENT =
  "L'export n'est pas chiffré : toute personne qui accède au fichier peut le lire. Enregistré dans un dossier synchronisé, comme OneDrive, il part là où ce dossier l'envoie.";

test("la section dit ce que contient l'export, qu'il n'est pas chiffré, et propose l'export en JSON", () => {
  render(<SectionDeLExport exporter={vi.fn()} />);

  const section = screen.getByRole("region", { name: "Exporter vos données" });
  expect(within(section).getByText(CONTENU)).toBeDefined();
  expect(within(section).getByText(AVERTISSEMENT)).toBeDefined();
  expect(within(section).getByText("JSON : un seul fichier, qui garde chaque valeur telle quelle.")).toBeDefined();
  expect(within(section).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual(["Exporter en JSON…"]);
});

test("hors de la fenêtre, la section ne propose aucun export", () => {
  render(<SectionDeLExport />);

  const section = screen.getByRole("region", { name: "Exporter vos données" });
  expect(within(section).getByText(AVERTISSEMENT)).toBeDefined();
  expect(within(section).queryByRole("button")).toBeNull();
});

test("l'export ne demande à l'hôte que le format, attend le choix du fichier, dit qu'il est en cours, puis son issue", async () => {
  let enCours: () => void = () => {};
  let rendre: (issue: IssueDeLExport) => void = () => {};
  const exporter = vi.fn(
    (_format: FormatDExport, quandEnCours: () => void) =>
      new Promise<IssueDeLExport>((resoudre) => {
        enCours = quandEnCours;
        rendre = resoudre;
      }),
  );
  render(<SectionDeLExport exporter={exporter} />);

  const bouton = screen.getByRole("button", { name: "Exporter en JSON…" });
  fireEvent.click(bouton);
  // Un second clic, avant la réponse, ne demande rien de plus.
  fireEvent.click(bouton);

  expect(exporter).toHaveBeenCalledOnce();
  expect(exporter.mock.calls[0]?.[0]).toBe("json");
  expect(await screen.findByText("Choisissez où enregistrer l'export dans la fenêtre de Sollicito.")).toBeDefined();
  expect((screen.getByRole("button", { name: "Exporter en JSON…" }) as HTMLButtonElement).disabled).toBe(true);
  expect(screen.getByRole("region", { name: "Exporter vos données" }).getAttribute("aria-busy")).toBe("true");

  act(() => enCours());
  expect(await screen.findByText("Export en cours : ne fermez pas Sollicito.")).toBeDefined();

  await act(async () => rendre("exporte"));
  expect((await screen.findByRole("status")).textContent).toBe("Export enregistré dans le fichier choisi.");
  expect((screen.getByRole("button", { name: "Exporter en JSON…" }) as HTMLButtonElement).disabled).toBe(false);
  expect(screen.queryByText("Export en cours : ne fermez pas Sollicito.")).toBeNull();
});

test("une annulation s'annonce, et chaque échec se dit en alerte, sans chemin ni nom de fichier", async () => {
  const textes: Record<Exclude<IssueDeLExport, "exporte" | "annule">, string> = {
    occupe: "Sollicito est occupé : réessayez dans un instant. Rien n'a été enregistré.",
    existe: "Un fichier du même nom est apparu entre-temps : Sollicito ne l'a pas remplacé. Exportez de nouveau.",
    extension: "Le nom du fichier doit finir par .json : rien n'a été enregistré. Exportez de nouveau sans changer l'extension.",
    dossierDeDonnees: "Ce dossier est celui des données de Sollicito : choisissez-en un autre. Rien n'a été enregistré.",
    accesRefuse:
      "Sollicito n'a pas le droit d'écrire dans ce dossier, ou de remplacer ce fichier : rien n'a été enregistré. Choisissez un autre dossier.",
    placeManquante: "La place manque sur ce disque : rien n'a été enregistré. Libérez de la place, ou choisissez un autre support.",
    dossierIntrouvable: "Ce dossier est introuvable : son support a peut-être été débranché. Rien n'a été enregistré.",
    impossible:
      "L'export n'a pas pu se faire : rien n'a été enregistré. Le fichier est peut-être ouvert dans un autre programme ; réessayez, ou choisissez un autre nom.",
  };

  const annulation = render(<SectionDeLExport exporter={vi.fn(async () => "annule" as const)} />);
  fireEvent.click(screen.getByRole("button", { name: "Exporter en JSON…" }));
  expect((await screen.findByText("Export annulé : rien n'a été enregistré.")).getAttribute("role")).toBe("status");
  expect(screen.queryByRole("alert")).toBeNull();
  annulation.unmount();

  for (const [issue, texte] of Object.entries(textes)) {
    const section = render(<SectionDeLExport exporter={vi.fn(async () => issue as IssueDeLExport)} />);
    fireEvent.click(screen.getByRole("button", { name: "Exporter en JSON…" }));
    expect((await screen.findByRole("alert")).textContent).toBe(texte);
    section.unmount();
  }
});

test("un nouvel export efface l'issue du précédent", async () => {
  const exporter = vi.fn<(format: FormatDExport, quandEnCours: () => void) => Promise<IssueDeLExport>>();
  exporter.mockResolvedValueOnce("placeManquante");
  let rendre: (issue: IssueDeLExport) => void = () => {};
  exporter.mockImplementationOnce(() => new Promise<IssueDeLExport>((resoudre) => (rendre = resoudre)));
  render(<SectionDeLExport exporter={exporter} />);

  fireEvent.click(screen.getByRole("button", { name: "Exporter en JSON…" }));
  expect(await screen.findByRole("alert")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Exporter en JSON…" }));

  expect(await screen.findByText("Choisissez où enregistrer l'export dans la fenêtre de Sollicito.")).toBeDefined();
  expect(screen.queryByRole("alert")).toBeNull();
  await act(async () => rendre("exporte"));
  expect((await screen.findByRole("status")).textContent).toBe("Export enregistré dans le fichier choisi.");
});
