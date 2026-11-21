import { act, fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi } from "../api/clientDeLApi.ts";
import { TableauDeBord } from "../candidatures/TableauDeBord.tsx";
import { clientSimule, sauvegardesFictives } from "../tests/clientSimule.ts";
import { INTERVALLE_DE_RELECTURE_EN_MS } from "./AlerteDesSauvegardes.tsx";

function tableauDeBord(client: ReturnType<typeof clientSimule>, ouvrirLesReglages = vi.fn()) {
  render(<TableauDeBord client={client} nouvelleCandidature={vi.fn()} ouvrirLaFiche={vi.fn()} ouvrirLesReglages={ouvrirLesReglages} />);
  return ouvrirLesReglages;
}

test("quand les copies se font, le tableau de bord n'affiche aucune alerte", async () => {
  const client = clientSimule();
  tableauDeBord(client);

  await screen.findByText("Aucune candidature enregistrée pour l'instant.");
  expect(client.lireLesSauvegardes).toHaveBeenCalled();
  expect(screen.queryByRole("alert")).toBeNull();
});

test("un dossier introuvable met une alerte en tête : depuis quand, quoi faire, et le chemin vers les réglages", async () => {
  const client = clientSimule({
    lireLesSauvegardes: async () => sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } }),
  });
  const ouvrirLesReglages = tableauDeBord(client);

  const alerte = await screen.findByRole("alert");
  expect(alerte.textContent).toContain(
    "Aucune copie de votre base n'a réussi depuis le 18/11/2026 14:30 : le dossier des sauvegardes est introuvable. Rebranchez son support, ou choisissez un autre dossier.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Ouvrir les réglages" }));
  expect(ouvrirLesReglages).toHaveBeenCalledOnce();
});

test("sans aucune copie réussie, l'alerte dit depuis quand les essais échouent", async () => {
  const client = clientSimule({
    lireLesSauvegardes: async () =>
      sauvegardesFictives({ derniereCopie: null, copies: [], echec: { motif: "CopieImpossible", depuis: "2026-11-19T08:00:00Z" } }),
  });
  tableauDeBord(client);

  expect((await screen.findByRole("alert")).textContent).toContain(
    "Aucune copie de votre base n'a pu se faire depuis le 19/11/2026 09:00 : Sollicito n'a pas pu y écrire de copie. Vérifiez la place libre sur ce support, ou choisissez un autre dossier.",
  );
});

test("« Réessayer » refait la copie : réussie, l'alerte part et l'écran l'annonce ; manquée, elle reste", async () => {
  const enPanne = sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } });
  const client = clientSimule({
    lireLesSauvegardes: async () => enPanne,
    sauvegarderMaintenant: vi.fn().mockResolvedValueOnce(enPanne).mockResolvedValueOnce(sauvegardesFictives()),
  });
  tableauDeBord(client);

  fireEvent.click(await screen.findByRole("button", { name: "Réessayer" }));
  await vi.waitFor(() => expect(client.sauvegarderMaintenant).toHaveBeenCalledOnce());
  expect(await screen.findByRole("alert")).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Réessayer" }));
  expect(await screen.findByText("Copie du jour faite.")).toBeDefined();
  expect(screen.queryByRole("alert")).toBeNull();
});

test("un réglage illisible met aussi l'alerte, sans « Réessayer » : seul un nouveau choix du dossier la lève", async () => {
  tableauDeBord(clientSimule({ lireLesSauvegardes: async () => sauvegardesFictives({ reglageIllisible: true }) }));

  expect((await screen.findByRole("alert")).textContent).toContain("Le réglage du dossier des sauvegardes n'a pas pu être relu");
  expect(screen.queryByRole("button", { name: "Réessayer" })).toBeNull();
  expect(screen.getByRole("button", { name: "Ouvrir les réglages" })).toBeDefined();
});

test("si l'état des sauvegardes ne se lit pas, le tableau de bord s'affiche sans alerte", async () => {
  tableauDeBord(clientSimule({ lireLesSauvegardes: () => Promise.reject(new ErreurDeLApi(500)) }));

  await screen.findByText("Aucune candidature enregistrée pour l'instant.");
  expect(screen.queryByRole("alert")).toBeNull();
});

test("au lancement, tant que la vérification n'est pas faite, l'état se relit : l'alerte vient sur ce premier écran", async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  try {
    const client = clientSimule({
      lireLesSauvegardes: vi
        .fn()
        .mockResolvedValueOnce(sauvegardesFictives({ verifie: false, derniereCopie: null, copies: [] }))
        .mockResolvedValue(sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } })),
    });
    tableauDeBord(client);

    await screen.findByText("Aucune candidature enregistrée pour l'instant.");
    expect(screen.queryByRole("alert")).toBeNull();
    await vi.advanceTimersByTimeAsync(1_000);

    expect((await screen.findByRole("alert")).textContent).toContain("le dossier des sauvegardes est introuvable");
    expect(client.lireLesSauvegardes).toHaveBeenCalledTimes(2);
  } finally {
    vi.useRealTimers();
  }
});

test("tant que le tableau de bord est affiché, l'état se relit : l'alerte vient, reste sans clignoter, puis part dès qu'une copie réussit", async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  try {
    const enPanne = sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } });
    const client = clientSimule({
      lireLesSauvegardes: vi
        .fn()
        .mockResolvedValueOnce(sauvegardesFictives())
        .mockResolvedValueOnce(enPanne)
        .mockResolvedValueOnce(enPanne)
        .mockResolvedValue(sauvegardesFictives()),
    });
    tableauDeBord(client);

    await screen.findByText("Aucune candidature enregistrée pour l'instant.");
    expect(screen.queryByRole("alert")).toBeNull();

    // La copie de l'heure échoue pendant que l'écran reste affiché : l'alerte vient à la relecture suivante.
    await vi.advanceTimersByTimeAsync(INTERVALLE_DE_RELECTURE_EN_MS);
    const alerte = await screen.findByRole("alert");
    expect(alerte.textContent).toContain("le dossier des sauvegardes est introuvable");

    // Relu, toujours en panne : la même alerte, restée en place.
    await vi.advanceTimersByTimeAsync(INTERVALLE_DE_RELECTURE_EN_MS);
    expect(client.lireLesSauvegardes).toHaveBeenCalledTimes(3);
    expect(screen.getByRole("alert")).toBe(alerte);

    // Le dossier revient, la copie suivante réussit : l'alerte part, sans que personne n'y touche.
    await vi.advanceTimersByTimeAsync(INTERVALLE_DE_RELECTURE_EN_MS);
    await vi.waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(client.lireLesSauvegardes).toHaveBeenCalledTimes(4);
  } finally {
    vi.useRealTimers();
  }
});

test("une relecture partie avant un « Réessayer » réussi ne ramène pas l'alerte", async () => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  try {
    const enPanne = sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } });
    let rendreLaRelecture: (etat: typeof enPanne) => void = () => {};
    const client = clientSimule({
      lireLesSauvegardes: vi
        .fn()
        .mockResolvedValueOnce(enPanne)
        .mockReturnValueOnce(new Promise((rendre) => (rendreLaRelecture = rendre)))
        .mockResolvedValue(sauvegardesFictives()),
      sauvegarderMaintenant: vi.fn().mockResolvedValue(sauvegardesFictives()),
    });
    tableauDeBord(client);

    await screen.findByRole("alert");
    await vi.advanceTimersByTimeAsync(INTERVALLE_DE_RELECTURE_EN_MS);
    expect(client.lireLesSauvegardes).toHaveBeenCalledTimes(2);

    fireEvent.click(screen.getByRole("button", { name: "Réessayer" }));
    expect(await screen.findByText("Copie du jour faite.")).toBeDefined();
    expect(screen.queryByRole("alert")).toBeNull();

    // La relecture partie avant l'essai répond après lui, avec l'état d'avant : il ne compte plus.
    await act(async () => {
      rendreLaRelecture(enPanne);
      await Promise.resolve();
    });
    expect(screen.queryByRole("alert")).toBeNull();
  } finally {
    vi.useRealTimers();
  }
});

test("un « Réessayer » que l'API ne mène pas au bout le dit dans l'alerte", async () => {
  const client = clientSimule({
    lireLesSauvegardes: async () => sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } }),
    sauvegarderMaintenant: () => Promise.reject(new ErreurDeLApi(500)),
  });
  tableauDeBord(client);

  fireEvent.click(await screen.findByRole("button", { name: "Réessayer" }));

  expect(await screen.findByText("Le nouvel essai n'a pas abouti. Réessayez ; si rien ne change, fermez Sollicito, puis relancez-le.")).toBeDefined();
});
