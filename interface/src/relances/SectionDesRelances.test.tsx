import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { clientSimule } from "../tests/clientSimule.ts";
import { SectionDesRelances } from "./SectionDesRelances.tsx";

test("la section montre le délai par défaut, et l'enregistre", async () => {
  const client = clientSimule();
  render(<SectionDesRelances client={client} />);

  const champ = (await screen.findByLabelText("Délai par défaut d'une relance, en jours depuis l'envoi")) as HTMLInputElement;
  expect(champ.value).toBe("7");
  expect(screen.getByText("De 1 à 365 jours. Il ne change pas les relances déjà planifiées.")).toBeDefined();
  fireEvent.change(champ, { target: { value: "10" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer le délai" }));

  expect((await screen.findByRole("status")).textContent).toBe("Délai par défaut enregistré : 10 jours.");
  expect(client.changerLeDelaiParDefaut).toHaveBeenCalledWith(10);
});

test.each(["0", "366", "", "7.5", "abc"])("un délai hors des bornes est refusé avant l'envoi (%s)", async (saisie) => {
  const client = clientSimule();
  render(<SectionDesRelances client={client} />);

  fireEvent.change(await screen.findByLabelText("Délai par défaut d'une relance, en jours depuis l'envoi"), { target: { value: saisie } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer le délai" }));

  expect(screen.getByText("Le délai d'une relance compte de 1 à 365 jours.")).toBeDefined();
  expect(client.changerLeDelaiParDefaut).not.toHaveBeenCalled();
});

test("un délai enregistré qui ne se relit pas, ou des réglages illisibles, se disent", async () => {
  const { unmount } = render(
    <SectionDesRelances client={clientSimule({ lireLeReglageDesRelances: async () => ({ delaiParDefaut: 7, delaiLisible: false, fichierLisible: true }) })} />,
  );
  expect((await screen.findByRole("alert")).textContent).toBe(
    "Le délai enregistré n'a pas pu être relu : le délai d'usine de 7 jours s'applique, jusqu'à ce que vous en enregistriez un.",
  );
  unmount();

  render(<SectionDesRelances client={clientSimule({ lireLeReglageDesRelances: async () => ({ delaiParDefaut: 7, delaiLisible: false, fichierLisible: false }) })} />);
  expect((await screen.findByRole("alert")).textContent).toMatch(/^Les réglages n'ont pas pu être relus : le délai d'usine de 7 jours s'applique\./u);
});

test("un délai que l'API ne peut pas écrire le dit", async () => {
  const client = clientSimule({
    changerLeDelaiParDefaut: vi.fn(async () => ({ issue: "impossible" as const, motif: "Le délai par défaut n'a pas changé : les réglages n'ont pas pu être relus." })),
  });
  render(<SectionDesRelances client={client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer le délai" }));

  expect((await screen.findByRole("alert")).textContent).toBe("Le délai par défaut n'a pas changé : les réglages n'ont pas pu être relus.");
});
