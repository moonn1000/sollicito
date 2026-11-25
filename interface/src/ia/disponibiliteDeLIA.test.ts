import { renderHook } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { clientSimule, etatDeLIAFictif } from "../tests/clientSimule.ts";
import { fournisseurPret, societeDuFournisseur, useDisponibiliteDeLIA } from "./disponibiliteDeLIA.ts";

test.each([
  ["Claude", "Claude"],
  ["OpenAI", "OpenAI"],
] as const)("%s, réglage lisible et clé enregistrée, est prêt", (fournisseur, attendu) => {
  expect(fournisseurPret(etatDeLIAFictif({ fournisseur, cle: "Enregistree" }))).toBe(attendu);
});

test.each([
  ["aucune IA", etatDeLIAFictif({ fournisseur: "Aucun", cle: "SansObjet" })],
  ["aucun choix fait", etatDeLIAFictif({ fournisseur: "Aucun", choisi: false, cle: "SansObjet" })],
  ["le modèle local, pas encore branché", etatDeLIAFictif({ fournisseur: "Ollama", cle: "SansObjet" })],
  ["Claude sans clé", etatDeLIAFictif({ fournisseur: "Claude", cle: "Aucune" })],
  ["OpenAI avec une clé illisible", etatDeLIAFictif({ fournisseur: "OpenAI", cle: "Illisible" })],
  ["un réglage illisible", etatDeLIAFictif({ fournisseur: "Aucun", reglageLisible: false, cle: "SansObjet" })],
  ["un réglage illisible, même avec une clé", etatDeLIAFictif({ fournisseur: "Claude", reglageLisible: false, cle: "Enregistree" })],
])("avec %s, aucune IA n'est prête", (_, etat) => {
  expect(fournisseurPret(etat)).toBeUndefined();
});

test("la société du fournisseur est celle du compte de l'utilisateur", () => {
  expect(societeDuFournisseur("Claude")).toBe("Anthropic");
  expect(societeDuFournisseur("OpenAI")).toBe("OpenAI");
});

test("l'état se lit une fois, à l'ouverture : prête, avec son fournisseur", async () => {
  const client = clientSimule({ lireLEtatDeLIA: vi.fn(async () => etatDeLIAFictif({ fournisseur: "OpenAI", cle: "Enregistree" })) });
  const { result } = renderHook(() => useDisponibiliteDeLIA(client));

  expect(result.current).toEqual({ sorte: "lecture" });
  await vi.waitFor(() => expect(result.current).toEqual({ sorte: "prete", fournisseur: "OpenAI" }));
  expect(client.lireLEtatDeLIA).toHaveBeenCalledOnce();
});

test("sans IA prête, l'écran le sait", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ fournisseur: "Claude", cle: "Aucune" }) });
  const { result } = renderHook(() => useDisponibiliteDeLIA(client));

  await vi.waitFor(() => expect(result.current).toEqual({ sorte: "absente" }));
});

test("un état qui ne se lit pas rend l'IA inconnue", async () => {
  const client = clientSimule({
    lireLEtatDeLIA: async () => {
      throw new Error("API muette");
    },
  });
  const { result } = renderHook(() => useDisponibiliteDeLIA(client));

  await vi.waitFor(() => expect(result.current).toEqual({ sorte: "inconnue" }));
});

test("un écran fermé avant la réponse n'est plus mis à jour", async () => {
  let repondre: (etat: ReturnType<typeof etatDeLIAFictif>) => void = () => {};
  const client = clientSimule({
    lireLEtatDeLIA: () =>
      new Promise((resolve) => {
        repondre = resolve;
      }),
  });
  const { result, unmount } = renderHook(() => useDisponibiliteDeLIA(client));

  unmount();
  repondre(etatDeLIAFictif({ fournisseur: "Claude", cle: "Enregistree" }));
  await Promise.resolve();

  expect(result.current).toEqual({ sorte: "lecture" });
});
