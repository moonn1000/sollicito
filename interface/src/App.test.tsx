import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test } from "vitest";
import { App } from "./App.tsx";
import { ErreurDeLApi } from "./api/clientDeLApi.ts";
import { HorsDeLaFenetre } from "./connexion.ts";
import { candidatureFictive, clientSimule } from "./tests/clientSimule.ts";

test("l'écran d'accueil affiche le nom de l'application", () => {
  render(<App connecter={() => new Promise(() => {})} />);

  expect(screen.getByRole("heading", { level: 1, name: "Sollicito" })).toBeDefined();
  expect(screen.getByRole("status").textContent).toBe("Connexion à l'API locale…");
  // Tant que l'API n'est pas jointe, aucune navigation.
  expect(screen.queryByRole("navigation")).toBeNull();
});

test("connectée, l'interface le dit et ouvre le tableau de bord, dans la mise en page commune", async () => {
  render(<App connecter={async () => clientSimule()} />);

  expect(await screen.findByText("Connectée à l'API locale.")).toBeDefined();
  const navigation = screen.getByRole("navigation", { name: "Navigation principale" });
  expect(within(navigation).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual([
    "Tableau de bord",
    "Nouvelle candidature",
  ]);
  expect(within(navigation).getByRole("button", { name: "Tableau de bord" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("main")).toBeDefined();
  expect(await screen.findByRole("heading", { level: 2, name: "Tableau de bord" })).toBeDefined();
});

test("hors de la fenêtre de Sollicito, l'interface le dit", async () => {
  render(<App connecter={() => Promise.reject(new HorsDeLaFenetre())} />);

  expect(await screen.findByText("Cette page ne fonctionne que dans la fenêtre de Sollicito.")).toBeDefined();
});

test("si l'API refuse ou ne répond pas, l'interface le dit", async () => {
  const clientRefuse = clientSimule({ lireEtat: () => Promise.reject(new ErreurDeLApi(401)) });

  render(<App connecter={async () => clientRefuse} />);

  expect(await screen.findByText("L'API locale ne répond pas. Fermez Sollicito, puis relancez-le.")).toBeDefined();
  expect(screen.queryByRole("navigation")).toBeNull();
});

test("une candidature saisie s'enregistre, puis le tableau de bord la liste et l'annonce", async () => {
  const enregistrees: ReturnType<typeof candidatureFictive>[] = [];
  const client = clientSimule({
    listerLesCandidatures: async () => [...enregistrees],
    enregistrerUneCandidature: async (saisie) => {
      const candidature = candidatureFictive({ id: 7, entreprise: { id: 4, nom: saisie.entreprise.trim() }, poste: saisie.poste });
      enregistrees.unshift(candidature);
      return { enregistree: true, candidature };
    },
  });
  render(<App connecter={async () => client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Nouvelle candidature" }));
  expect(screen.getByRole("button", { name: "Nouvelle candidature" }).getAttribute("aria-current")).toBe("page");
  fireEvent.change(screen.getByLabelText("Entreprise"), { target: { value: "Brasserie Lune Rousse" } });
  fireEvent.change(screen.getByLabelText("Poste"), { target: { value: "Brasseuse" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  expect(await screen.findByText("Candidature enregistrée : Brasseuse, chez Brasserie Lune Rousse.")).toBeDefined();
  expect(screen.getByRole("button", { name: "Tableau de bord" }).getAttribute("aria-current")).toBe("page");
  await waitFor(() => expect(screen.getAllByRole("row")).toHaveLength(2));
  expect(within(screen.getAllByRole("row")[1]!).getAllByRole("cell").map((cellule) => cellule.textContent)).toEqual([
    "Brasserie Lune Rousse",
    "Brasseuse",
    "27/03/2026 23:30",
    "Envoyée",
  ]);
});
