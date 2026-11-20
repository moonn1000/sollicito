import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi } from "../api/clientDeLApi.ts";
import { TableauDeBord } from "../candidatures/TableauDeBord.tsx";
import { clientSimule, sauvegardesFictives } from "../tests/clientSimule.ts";

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
