import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi, type EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import type { IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { clientSimule, DOSSIER_PAR_DEFAUT_FICTIF, sauvegardesFictives } from "../tests/clientSimule.ts";
import { SectionDesSauvegardes } from "./SectionDesSauvegardes.tsx";

const CLE_FICTIVE = "E:\\Sauvegardes Sollicito";

test("le dossier par défaut est montré, avec ses avertissements : copies non chiffrées, même disque que la base", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={vi.fn()} />);

  const section = await screen.findByRole("region", { name: "Sauvegardes" });
  expect(await within(section).findByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
  expect(within(section).getByText("Les copies ne sont pas chiffrées : toute personne qui accède à leur dossier peut les lire.")).toBeDefined();
  expect(within(section).getByText(/C'est le dossier par défaut, sur le même disque que votre base : il ne vous protège pas d'une panne de ce disque\./)).toBeDefined();
  expect(within(section).getByText("18/11/2026 14:30", { selector: "dd time" })).toBeDefined();
  // Ni restauration (#28), ni retour au dossier par défaut quand il sert déjà.
  expect(within(section).queryByRole("button", { name: /restaurer/i })).toBeNull();
  expect(within(section).queryByRole("button", { name: "Revenir au dossier par défaut" })).toBeNull();
  expect(within(section).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual(["Choisir un autre dossier…", "Sauvegarder maintenant"]);
});

test("la section ne présente jamais une copie comme le moyen de rendre une candidature supprimée", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={vi.fn()} />);

  const section = await screen.findByRole("region", { name: "Sauvegardes" });
  await within(section).findByText(DOSSIER_PAR_DEFAUT_FICTIF);
  expect(section.textContent).not.toMatch(/supprim|candidature/i);
});

test("les copies gardées sont listées, celles d'avant une mise à jour marquées comme telles", async () => {
  const client = clientSimule({
    lireLesSauvegardes: async () =>
      sauvegardesFictives({
        copies: [
          { nom: "sollicito-1a2b3c4d-2026-11-18.db", sorte: "DuJour", faiteLe: "2026-11-18T13:30:00Z" },
          { nom: "sollicito-1a2b3c4d-2026-11-17.db", sorte: "DuJour", faiteLe: "2026-11-17T17:00:00Z" },
          { nom: "sollicito-avant-20261120164027_CorrectionsDesEtapes.db", sorte: "AvantMiseAJour", faiteLe: "2026-11-16T08:00:00Z" },
        ],
      }),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);

  const tableau = await screen.findByRole("table");
  const lignes = within(tableau).getAllByRole("row").slice(1);
  expect(lignes.map((ligne) => within(ligne).getAllByRole("cell").map((cellule) => cellule.textContent))).toEqual([
    ["18/11/2026 14:30", "Copie du jour", "sollicito-1a2b3c4d-2026-11-18.db"],
    ["17/11/2026 18:00", "Copie du jour", "sollicito-1a2b3c4d-2026-11-17.db"],
    ["16/11/2026 09:00", "Avant une mise à jour", "sollicito-avant-20261120164027_CorrectionsDesEtapes.db"],
  ]);
});

test("un dossier choisi par l'hôte s'annonce, et la section relit l'état : les copies de l'ancien dossier y restent", async () => {
  let etat = sauvegardesFictives();
  const client = clientSimule({ lireLesSauvegardes: vi.fn(async () => etat) });
  const choisirLeDossier = vi.fn(async (): Promise<IssueDuChoixDuDossier> => {
    etat = sauvegardesFictives({ dossier: CLE_FICTIVE, parDefaut: false });
    return "choisi";
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={choisirLeDossier} />);

  fireEvent.click(await screen.findByRole("button", { name: "Choisir un autre dossier…" }));

  expect(await screen.findByText("Dossier des sauvegardes changé : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.")).toBeDefined();
  expect(screen.getByText(CLE_FICTIVE)).toBeDefined();
  expect(choisirLeDossier).toHaveBeenCalledOnce();
  // Hors du dossier par défaut : on peut y revenir, et l'écran dit où restent les copies d'avant une mise à jour.
  expect(screen.getByRole("button", { name: "Revenir au dossier par défaut" })).toBeDefined();
  expect(screen.getByText(/Les copies d'avant une mise à jour de Sollicito restent dans le dossier par défaut/)).toBeDefined();
});

test("un choix annulé ou refusé ne change pas de dossier, et l'écran le dit", async () => {
  const issues: IssueDuChoixDuDossier[] = ["annule", "refuse"];
  const choisirLeDossier = vi.fn(async () => issues.shift()!);
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={choisirLeDossier} />);

  fireEvent.click(await screen.findByRole("button", { name: "Choisir un autre dossier…" }));
  expect(await screen.findByText("Le dossier des sauvegardes n'a pas changé.")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Choisir un autre dossier…" }));
  expect(
    await screen.findByText("Sollicito n'a pas pu faire de copie dans ce dossier : le dossier des sauvegardes n'a pas changé."),
  ).toBeDefined();
  expect(screen.getByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
});

test("hors de la fenêtre, aucun bouton ne propose de choisir un dossier", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} />);

  await screen.findByText(DOSSIER_PAR_DEFAUT_FICTIF);
  expect(screen.queryByRole("button", { name: "Choisir un autre dossier…" })).toBeNull();
});

test("le retour au dossier par défaut s'annonce ; refusé, son motif s'affiche et l'ancien dossier reste", async () => {
  const ailleurs = sauvegardesFictives({ dossier: CLE_FICTIVE, parDefaut: false });
  const client = clientSimule({
    lireLesSauvegardes: vi.fn(async () => ailleurs),
    revenirAuDossierParDefaut: vi
      .fn()
      .mockResolvedValueOnce({ issue: "refuse", motif: "Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie." })
      .mockResolvedValueOnce({ issue: "fait", etat: sauvegardesFictives() }),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: "Revenir au dossier par défaut" }));
  expect(await screen.findByText("Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie.")).toBeDefined();
  expect(screen.getByText(CLE_FICTIVE)).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Revenir au dossier par défaut" }));
  expect(
    await screen.findByText("Retour au dossier par défaut : une copie vient d'y être faite. Les copies de l'ancien dossier y restent."),
  ).toBeDefined();
  expect(screen.getByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
});

test("« Sauvegarder maintenant » refait la copie du jour ; manquée, l'alerte dit depuis quand et quoi faire", async () => {
  const client = clientSimule({
    sauvegarderMaintenant: vi
      .fn()
      .mockResolvedValueOnce(sauvegardesFictives({ derniereCopie: "2026-11-18T15:00:00Z" }))
      .mockResolvedValueOnce(sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-18T16:00:00Z" } })),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: "Sauvegarder maintenant" }));
  expect(await screen.findByText("Copie du jour faite.")).toBeDefined();
  expect(screen.getByText("18/11/2026 16:00", { selector: "dd time" })).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Sauvegarder maintenant" }));
  expect(
    await screen.findByText(
      "Aucune copie de votre base n'a réussi depuis le 18/11/2026 14:30 : le dossier des sauvegardes est introuvable. Rebranchez son support, ou choisissez un autre dossier.",
    ),
  ).toBeDefined();
  expect(screen.queryByText("Copie du jour faite.")).toBeNull();
});

test("un réglage illisible le dit, et invite à choisir de nouveau son dossier", async () => {
  render(<SectionDesSauvegardes client={clientSimule({ lireLesSauvegardes: async () => sauvegardesFictives({ reglageIllisible: true }) })} />);

  expect(
    await screen.findByText(
      "Le réglage du dossier des sauvegardes n'a pas pu être relu : les copies vont dans le dossier par défaut. Choisissez de nouveau votre dossier.",
    ),
  ).toBeDefined();
});

test("deux clics rapides ne lancent qu'une copie", async () => {
  let finir: () => void = () => {};
  const client = clientSimule({
    sauvegarderMaintenant: vi.fn(
      () =>
        new Promise<EtatDesSauvegardes>((resoudre) => {
          finir = () => resoudre(sauvegardesFictives());
        }),
    ),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);
  const bouton = await screen.findByRole("button", { name: "Sauvegarder maintenant" });

  act(() => {
    bouton.click();
    bouton.click();
  });
  await act(async () => finir());

  await waitFor(() => expect(screen.getByText("Copie du jour faite.")).toBeDefined());
  expect(client.sauvegarderMaintenant).toHaveBeenCalledOnce();
});

test("si l'état ne se lit pas, ou si une action échoue, l'écran le dit", async () => {
  const { unmount } = render(<SectionDesSauvegardes client={clientSimule({ lireLesSauvegardes: () => Promise.reject(new ErreurDeLApi(500)) })} />);
  expect(await screen.findByText("Les sauvegardes n'ont pas pu être lues. Fermez Sollicito, puis relancez-le.")).toBeDefined();
  unmount();

  render(<SectionDesSauvegardes client={clientSimule({ sauvegarderMaintenant: () => Promise.reject(new ErreurDeLApi(500)) })} />);
  fireEvent.click(await screen.findByRole("button", { name: "Sauvegarder maintenant" }));
  expect(await screen.findByText("L'action n'a pas abouti. Réessayez ; si rien ne change, fermez Sollicito, puis relancez-le.")).toBeDefined();
});
