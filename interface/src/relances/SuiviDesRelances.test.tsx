import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { FicheDUneCandidature } from "../api/clientDeLApi.ts";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { clientSimule, contactFictif, ficheFictive, relanceFictive } from "../tests/clientSimule.ts";
import { MESSAGES_DU_BROUILLON } from "./affichage.ts";
import { SuiviDesRelances } from "./SuiviDesRelances.tsx";

// Envoyée le 27 mars 2026 à 23:30, heure de Bruxelles.
const FICHE = ficheFictive();

function rendre(
  modifications: {
    client?: ReturnType<typeof clientSimule>;
    fiche?: FicheDUneCandidature;
    contacts?: ReturnType<typeof contactFictif>[];
    ouvrirLeBrouillon?: (relance: number) => Promise<IssueDuBrouillon>;
  } = {},
) {
  const client = modifications.client ?? clientSimule();
  const montrer = vi.fn();
  const relire = vi.fn(async () => true);
  render(
    <SuiviDesRelances
      client={client}
      fiche={modifications.fiche ?? FICHE}
      contacts={modifications.contacts ?? []}
      ouvrirLeBrouillon={modifications.ouvrirLeBrouillon}
      montrer={montrer}
      relire={relire}
    />,
  );
  return { client, montrer, relire };
}

test("une relance se planifie au délai par défaut, avec l'échéance qui en résulte, et le contact unique d'office", async () => {
  const { client, montrer } = rendre({ contacts: [contactFictif()] });

  fireEvent.click(screen.getByRole("button", { name: "Planifier une relance" }));

  const delai = screen.getByLabelText("Relancer après, en jours depuis l'envoi");
  await vi.waitFor(() => expect((delai as HTMLInputElement).value).toBe("7"));
  expect(screen.getByText("Échéance : vendredi 3 avril 2026.")).toBeDefined();
  expect((screen.getByLabelText("Destinataire") as HTMLSelectElement).value).toBe("4");
  fireEvent.change(delai, { target: { value: "10" } });
  expect(screen.getByText("Échéance : lundi 6 avril 2026.")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Planifier la relance" }));

  expect((await screen.findByRole("status")).textContent).toBe("Relance planifiée.");
  expect(client.planifierUneRelance).toHaveBeenCalledWith(1, { delai: 10, destinataire: 4 });
  expect(montrer).toHaveBeenCalledOnce();
});

test("le délai proposé suit le réglage, et après une relance close, son délai plus celui par défaut", async () => {
  const lireLeReglageDesRelances = vi.fn(async () => ({ delaiParDefaut: 10, delaiLisible: true, fichierLisible: true }));
  rendre({
    client: clientSimule({ lireLeReglageDesRelances }),
    fiche: ficheFictive({ relances: [relanceFictive({ etat: "Faite", echeance: "2026-04-03" })] }),
  });
  // Le réglage lu, le formulaire part de lui.
  await act(async () => {
    await lireLeReglageDesRelances.mock.results[0]?.value;
  });

  fireEvent.click(screen.getByRole("button", { name: "Planifier une relance" }));

  expect((screen.getByLabelText("Relancer après, en jours depuis l'envoi") as HTMLInputElement).value).toBe("17");
  // Sans contact, le brouillon s'ouvrira sans destinataire.
  expect((screen.getByLabelText("Destinataire") as HTMLSelectElement).value).toBe("");
  expect(screen.getByText(/ajoutez-en un plus bas/u)).toBeDefined();
});

test("un délai hors des bornes est refusé avant l'envoi ; les refus de l'API s'affichent à côté de leur champ", async () => {
  const client = clientSimule({
    planifierUneRelance: vi.fn(async () => ({ issue: "refusee" as const, refus: { destinataire: ["Ce contact n'est pas un contact de l'entreprise de la candidature."] } })),
  });
  rendre({ client, contacts: [contactFictif()] });
  fireEvent.click(screen.getByRole("button", { name: "Planifier une relance" }));

  fireEvent.change(screen.getByLabelText("Relancer après, en jours depuis l'envoi"), { target: { value: "0" } });
  fireEvent.click(screen.getByRole("button", { name: "Planifier la relance" }));
  expect(screen.getByText("Le délai d'une relance compte de 1 à 365 jours.")).toBeDefined();
  expect(client.planifierUneRelance).not.toHaveBeenCalled();

  fireEvent.change(screen.getByLabelText("Relancer après, en jours depuis l'envoi"), { target: { value: "7" } });
  fireEvent.click(screen.getByRole("button", { name: "Planifier la relance" }));

  expect(await screen.findByText("Ce contact n'est pas un contact de l'entreprise de la candidature.")).toBeDefined();
  expect(screen.getByLabelText("Destinataire").getAttribute("aria-invalid")).toBe("true");
});

test("une relance refusée par l'état de la candidature relit la fiche et dit le motif", async () => {
  const client = clientSimule({
    planifierUneRelance: vi.fn(async () => ({ issue: "nonPermise" as const, motif: "La candidature est close : elle ne reçoit plus de relance." })),
  });
  const { relire } = rendre({ client });
  fireEvent.click(screen.getByRole("button", { name: "Planifier une relance" }));
  fireEvent.click(screen.getByRole("button", { name: "Planifier la relance" }));

  expect((await screen.findByRole("alert")).textContent).toBe("La candidature est close : elle ne reçoit plus de relance.");
  expect(relire).toHaveBeenCalledOnce();
});

test("une relance à faire se montre avec son échéance et son destinataire, se change, se marque faite ou s'abandonne", async () => {
  const fiche = ficheFictive({ relances: [relanceFictive({ destinataire: contactFictif() })] });
  const { client } = rendre({ fiche, contacts: [contactFictif(), contactFictif({ id: 5, nom: "Paul Lambert", email: "paul@exemple.invalid" })] });

  expect(screen.getByText(/^À faire le/u).textContent).toBe("À faire le vendredi 3 avril 2026, Jeanne Martin (jeanne.martin@exemple.invalid).");
  expect(screen.queryByRole("button", { name: "Planifier une relance" })).toBeNull();

  fireEvent.click(screen.getByRole("button", { name: "Changer la relance" }));
  expect((screen.getByLabelText("Relancer après, en jours depuis l'envoi") as HTMLInputElement).value).toBe("7");
  expect((screen.getByLabelText("Destinataire") as HTMLSelectElement).value).toBe("4");
  fireEvent.change(screen.getByLabelText("Destinataire"), { target: { value: "5" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la relance" }));
  expect((await screen.findByRole("status")).textContent).toBe("Relance changée.");
  expect(client.changerLaRelance).toHaveBeenCalledWith(1, 7, { delai: 7, destinataire: 5 });

  fireEvent.click(screen.getByRole("button", { name: "Marquer faite" }));
  await vi.waitFor(() => expect(screen.getByRole("status").textContent).toBe("Relance marquée faite : elle entre dans l'historique."));
  expect(client.marquerLaRelanceFaite).toHaveBeenCalledWith(1, 7);

  fireEvent.click(screen.getByRole("button", { name: "Abandonner" }));
  await vi.waitFor(() => expect(screen.getByRole("status").textContent).toBe("Relance abandonnée : elle entre dans l'historique."));
  expect(client.abandonnerLaRelance).toHaveBeenCalledWith(1, 7);
});

test("une clôture refusée relit la fiche et dit le motif", async () => {
  const client = clientSimule({ marquerLaRelanceFaite: vi.fn(async () => ({ faite: false as const, motif: "Cette relance n'est plus à faire." })) });
  const { relire } = rendre({ client, fiche: ficheFictive({ relances: [relanceFictive()] }) });

  fireEvent.click(screen.getByRole("button", { name: "Marquer faite" }));

  expect((await screen.findByRole("alert")).textContent).toBe("Cette relance n'est plus à faire.");
  expect(relire).toHaveBeenCalledOnce();
});

test("une candidature close ne propose plus de relance", () => {
  rendre({ fiche: ficheFictive({ statut: "Refusee", passagesPermis: [] }) });

  expect(screen.getByText("La candidature est close : elle ne reçoit plus de relance.")).toBeDefined();
  expect(screen.queryByRole("button", { name: "Planifier une relance" })).toBeNull();
});

test("« Préparer le brouillon » demande à la fenêtre d'ouvrir celui de la relance à faire, et dit l'issue", async () => {
  const ouvrirLeBrouillon = vi.fn<(relance: number) => Promise<IssueDuBrouillon>>(async () => "ouvert");
  rendre({ fiche: ficheFictive({ relances: [relanceFictive()] }), ouvrirLeBrouillon });

  fireEvent.click(screen.getByRole("button", { name: "Préparer le brouillon de la relance du vendredi 3 avril 2026" }));

  expect((await screen.findByRole("status")).textContent).toBe(MESSAGES_DU_BROUILLON.ouvert);
  expect(ouvrirLeBrouillon.mock.calls).toEqual([[7]]);
});

test("un brouillon refusé relit la fiche ; hors de la fenêtre, aucun bouton de brouillon", async () => {
  const { relire } = rendre({ fiche: ficheFictive({ relances: [relanceFictive()] }), ouvrirLeBrouillon: async () => "refuse" });

  fireEvent.click(screen.getByRole("button", { name: /Préparer le brouillon/u }));

  expect((await screen.findByRole("alert")).textContent).toBe(MESSAGES_DU_BROUILLON.refuse);
  expect(relire).toHaveBeenCalledOnce();
});

test("hors de la fenêtre, la relance se suit sans bouton de brouillon", () => {
  rendre({ fiche: ficheFictive({ relances: [relanceFictive()] }) });

  const section = screen.getByRole("heading", { name: "Relance" }).parentElement!;
  expect(within(section).queryByRole("button", { name: /Préparer le brouillon/u })).toBeNull();
  expect(within(section).getByRole("button", { name: "Marquer faite" })).toBeDefined();
});
