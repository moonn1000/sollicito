import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { ClientDeLApi } from "../api/clientDeLApi.ts";
import { clientSimule, offreFictive, offreSignaleeFictive } from "../tests/clientSimule.ts";
import { ModificationDUneOffre } from "./ModificationDUneOffre.tsx";

function afficher(client: ClientDeLApi = clientSimule()) {
  const revenir = vi.fn();
  const quandModifiee = vi.fn();
  const ouvrirUneOffre = vi.fn();
  render(<ModificationDUneOffre client={client} offre={3} revenir={revenir} quandModifiee={quandModifiee} ouvrirUneOffre={ouvrirUneOffre} />);
  return { client, revenir, quandModifiee, ouvrirUneOffre };
}

test("la correction préremplit les champs de l'offre, sans son texte, et n'envoie jamais de texte", async () => {
  const { client, quandModifiee } = afficher();

  const poste = await screen.findByLabelText<HTMLInputElement>("Poste");
  expect(poste.value).toBe("Développeur .NET (H/F)");
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Lumière Verte SA");
  expect(screen.getByLabelText<HTMLInputElement>("Lieu").value).toBe("Liège");
  expect(screen.getByLabelText<HTMLInputElement>("Date de publication").value).toBe("2026-11-17");
  expect(screen.getByLabelText<HTMLInputElement>("Adresse de l'annonce").value).toBe("https://emploi.exemple.invalid/offres/123");
  expect(screen.queryByLabelText("Texte de l'offre")).toBeNull();

  fireEvent.change(poste, { target: { value: "Développeuse .NET" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  await waitFor(() => expect(quandModifiee).toHaveBeenCalledOnce());
  expect(client.corrigerUneOffre).toHaveBeenCalledWith(
    3,
    {
      entreprise: "Lumière Verte SA",
      poste: "Développeuse .NET",
      lieu: "Liège",
      publieeLe: "2026-11-17",
      adresse: "https://emploi.exemple.invalid/offres/123",
    },
    false,
  );
});

test("un doublon possible attend l'accord ; « Ne pas modifier » revient à la fiche sans rien changer", async () => {
  const corrigerUneOffre = vi
    .fn<ClientDeLApi["corrigerUneOffre"]>()
    .mockResolvedValueOnce({ issue: "doublonPossible", doublons: [offreSignaleeFictive()] })
    .mockResolvedValueOnce({ issue: "enregistree", fiche: offreFictive() });
  const { revenir, quandModifiee } = afficher(clientSimule({ corrigerUneOffre }));

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer les modifications" }));
  const signal = await screen.findByRole("alert");
  fireEvent.click(within(signal).getByRole("button", { name: "Ne pas modifier" }));
  expect(revenir).toHaveBeenCalledOnce();

  fireEvent.click(within(signal).getByRole("button", { name: "Garder les deux" }));
  await waitFor(() => expect(quandModifiee).toHaveBeenCalledOnce());
  expect(corrigerUneOffre).toHaveBeenLastCalledWith(3, expect.objectContaining({ poste: "Développeur .NET (H/F)" }), true);
});

test("les refus de la correction s'affichent à côté de leur champ ; « Annuler » revient à la fiche", async () => {
  const { revenir } = afficher(
    clientSimule({ corrigerUneOffre: async () => ({ issue: "refusee", refus: { adresse: ["L'adresse de l'annonce ne doit pas porter de nom d'utilisateur ni de mot de passe."] } }) }),
  );

  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer les modifications" }));

  const adresse = screen.getByLabelText("Adresse de l'annonce");
  await waitFor(() => expect(adresse.getAttribute("aria-invalid")).toBe("true"));
  await waitFor(() => expect(document.activeElement).toBe(adresse));
  fireEvent.click(screen.getByRole("button", { name: "Annuler" }));
  expect(revenir).toHaveBeenCalledOnce();
});
