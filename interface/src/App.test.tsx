import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { App } from "./App.tsx";
import { ErreurDeLApi, type ClientDeLApi } from "./api/clientDeLApi.ts";
import { HorsDeLaFenetre } from "./connexion.ts";

const clientPret: ClientDeLApi = { lireEtat: async () => ({ statut: "pret" }) };

test("l'écran d'accueil affiche le nom de l'application", () => {
  render(<App connecter={() => new Promise(() => {})} />);

  expect(screen.getByRole("heading", { level: 1, name: "Sollicito" })).toBeDefined();
  expect(screen.getByRole("status").textContent).toBe("Connexion à l'API locale…");
});

test("connectée, l'interface le dit", async () => {
  render(<App connecter={async () => clientPret} />);

  expect(await screen.findByText("Connectée à l'API locale.")).toBeDefined();
});

test("hors de la fenêtre de Sollicito, l'interface le dit", async () => {
  render(<App connecter={() => Promise.reject(new HorsDeLaFenetre())} />);

  expect(await screen.findByText("Cette page ne fonctionne que dans la fenêtre de Sollicito.")).toBeDefined();
});

test("si l'API refuse ou ne répond pas, l'interface le dit", async () => {
  const clientRefuse: ClientDeLApi = { lireEtat: () => Promise.reject(new ErreurDeLApi(401)) };

  render(<App connecter={async () => clientRefuse} />);

  expect(await screen.findByText("L'API locale ne répond pas. Fermez Sollicito, puis relancez-le.")).toBeDefined();
});
