import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { clientSimule, etatDeLIAFictif } from "../tests/clientSimule.ts";
import { CarteDeLIA } from "./CarteDeLIA.tsx";

test("tant qu'aucun choix n'est fait, la carte propose de connecter une IA, facultative", async () => {
  const choisirUneIA = vi.fn();
  render(<CarteDeLIA client={clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ choisi: false }) })} choisirUneIA={choisirUneIA} />);

  expect(await screen.findByRole("heading", { name: "Connecter une IA ?" })).toBeDefined();
  expect(
    screen.getByText(
      "Facultative : Sollicito fonctionne aussi sans. Vous pouvez choisir un fournisseur en ligne, avec votre propre clé, ou un modèle qui tourne sur votre ordinateur.",
    ),
  ).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Choisir une IA" }));

  expect(choisirUneIA).toHaveBeenCalledOnce();
});

test("Continuer sans IA enregistre aucune IA, et la carte part", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ choisi: false }) });
  render(<CarteDeLIA client={client} choisirUneIA={() => {}} />);

  fireEvent.click(await screen.findByRole("button", { name: "Continuer sans IA" }));

  await vi.waitFor(() => expect(screen.queryByRole("heading", { name: "Connecter une IA ?" })).toBeNull());
  expect(client.choisirLeFournisseurDIA).toHaveBeenCalledWith("Aucun");
});

test.each([
  ["un choix déjà fait", etatDeLIAFictif({ choisi: true })],
  ["des réglages illisibles en entier", etatDeLIAFictif({ choisi: true, reglageLisible: false, fichierLisible: false })],
])("avec %s, la carte ne se montre pas", async (_, etat) => {
  const client = clientSimule({ lireLEtatDeLIA: vi.fn(async () => etat) });
  render(<CarteDeLIA client={client} choisirUneIA={() => {}} />);

  await vi.waitFor(() => expect(client.lireLEtatDeLIA).toHaveBeenCalled());
  expect(screen.queryByRole("heading", { name: "Connecter une IA ?" })).toBeNull();
});

test("un état illisible ne montre rien, et un choix manqué le dit", async () => {
  const { unmount } = render(
    <CarteDeLIA
      client={clientSimule({
        lireLEtatDeLIA: async () => {
          throw new Error("Panne simulée");
        },
      })}
    />,
  );
  await new Promise((attente) => setTimeout(attente, 0));
  expect(screen.queryByRole("heading", { name: "Connecter une IA ?" })).toBeNull();
  unmount();

  render(
    <CarteDeLIA
      client={clientSimule({
        lireLEtatDeLIA: async () => etatDeLIAFictif({ choisi: false }),
        choisirLeFournisseurDIA: vi.fn(async () => {
          throw new Error("Panne simulée");
        }),
      })}
    />,
  );
  fireEvent.click(await screen.findByRole("button", { name: "Continuer sans IA" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "Le choix n'a pas pu être enregistré. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(screen.getByRole("heading", { name: "Connecter une IA ?" })).toBeDefined();
});
