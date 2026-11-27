// L'écran des réglages (#26) : un dossier des sauvegardes choisi réécrit reglages.json, et les sections « Relances » et
// « IA », qui en lisent un réglage, le relisent (#93, point 12).
import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { EtatDeLIA, ReglageDuDelai } from "../api/clientDeLApi.ts";
import type { IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { clientSimule, etatDeLIAFictif } from "../tests/clientSimule.ts";
import { EcranDesReglages } from "./EcranDesReglages.tsx";

const ILLISIBLE: ReglageDuDelai = { delaiParDefaut: 7, delaiLisible: false, fichierLisible: false };
const REECRIT: ReglageDuDelai = { delaiParDefaut: 7, delaiLisible: true, fichierLisible: true };

function sections() {
  return { relances: screen.getByRole("region", { name: "Relances" }), ia: screen.getByRole("region", { name: "IA" }) };
}

async function choisirUnDossier() {
  fireEvent.click(await screen.findByRole("button", { name: "Choisir un autre dossier…" }));
}

test("après un dossier choisi, les sections « Relances » et « IA » relisent les réglages réécrits", async () => {
  const lireLeReglageDesRelances = vi.fn(async () => REECRIT).mockResolvedValueOnce(ILLISIBLE);
  const lireLEtatDeLIA = vi
    .fn(async (): Promise<EtatDeLIA> => etatDeLIAFictif({ choisi: false }))
    .mockResolvedValueOnce(etatDeLIAFictif({ choisi: false, reglageLisible: false, fichierLisible: false }));
  render(<EcranDesReglages client={clientSimule({ lireLeReglageDesRelances, lireLEtatDeLIA })} choisirLeDossierDesSauvegardes={async () => "choisi"} />);
  const { relances, ia } = sections();
  await within(relances).findByText(/^Les réglages n'ont pas pu être relus/u);
  await within(ia).findByText(/^Les réglages n'ont pas pu être relus/u);

  await choisirUnDossier();

  await vi.waitFor(() => expect(within(relances).queryByRole("alert")).toBeNull());
  await vi.waitFor(() => expect(within(ia).queryByRole("alert")).toBeNull());
  expect(lireLeReglageDesRelances).toHaveBeenCalledTimes(2);
  expect(lireLEtatDeLIA).toHaveBeenCalledTimes(2);
});

test("une relecture qui ne change rien garde la saisie en cours ; sans dossier choisi, rien ne se relit", async () => {
  const issues: IssueDuChoixDuDossier[] = ["annule", "choisi"];
  const lireLeReglageDesRelances = vi.fn(async () => REECRIT);
  const lireLEtatDeLIA = vi.fn(async (): Promise<EtatDeLIA> => etatDeLIAFictif({ fournisseur: "Claude", cle: "Aucune" }));
  render(
    <EcranDesReglages client={clientSimule({ lireLeReglageDesRelances, lireLEtatDeLIA })} choisirLeDossierDesSauvegardes={async () => issues.shift()!} />,
  );
  const { relances, ia } = sections();
  const delai = (await within(relances).findByLabelText("Délai par défaut d'une relance, en jours depuis l'envoi")) as HTMLInputElement;
  const cle = (await within(ia).findByLabelText("Clé d'API de Claude")) as HTMLInputElement;
  fireEvent.change(delai, { target: { value: "10" } });
  fireEvent.change(cle, { target: { value: "saisie-fictive" } });

  await choisirUnDossier();
  await screen.findByText("Le dossier des sauvegardes n'a pas changé.");
  expect(lireLeReglageDesRelances).toHaveBeenCalledOnce();
  expect(lireLEtatDeLIA).toHaveBeenCalledOnce();

  await choisirUnDossier();
  await vi.waitFor(() => expect(lireLEtatDeLIA).toHaveBeenCalledTimes(2));
  await vi.waitFor(() => expect(lireLeReglageDesRelances).toHaveBeenCalledTimes(2));

  expect(delai.value).toBe("10");
  expect(cle.value).toBe("saisie-fictive");
});
