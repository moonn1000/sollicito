import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { clientSimule, etapeFictive, ficheFictive } from "../tests/clientSimule.ts";
import { NoterUneEtape } from "./NoterUneEtape.tsx";

// Le 18/11/2026 à 14:30, heure de Bruxelles (le fuseau des tests).
const maintenant = () => new Date("2026-11-18T13:30:00Z");

function afficher(client = clientSimule(), quandNotee = vi.fn()) {
  render(<NoterUneEtape client={client} candidature={7} quandNotee={quandNotee} maintenant={maintenant} />);
  return { client, quandNotee };
}

function remplir(libelle: string, valeur: string) {
  fireEvent.change(screen.getByLabelText(libelle), { target: { value: valeur } });
}

test("le formulaire demande le type, la date et l'heure, et une note facultative", () => {
  afficher();

  expect(screen.getByRole("heading", { level: 3, name: "Noter un entretien ou un échange" })).toBeDefined();
  const type = screen.getByLabelText<HTMLSelectElement>("Type");
  expect(type.value).toBe("Entretien");
  expect(within(type).getAllByRole("option").map((option) => [option.getAttribute("value"), option.textContent])).toEqual([
    ["Appel", "Appel"],
    ["Entretien", "Entretien"],
    ["Echange", "Échange (courriel, message)"],
    ["Autre", "Autre"],
  ]);
  // L'heure présente, à la minute, remplit d'avance la date et l'heure.
  expect(screen.getByLabelText<HTMLInputElement>("Date et heure").value).toBe("2026-11-18T14:30");
  expect(screen.getByLabelText("Date et heure").getAttribute("type")).toBe("datetime-local");
  expect(screen.getByLabelText<HTMLTextAreaElement>("Note (facultative)").value).toBe("");
});

test("l'étape part telle que saisie, puis le formulaire se vide et l'annonce", async () => {
  const fiche = ficheFictive({ historique: [etapeFictive()] });
  const { client, quandNotee } = afficher(clientSimule({ noterUneEtape: vi.fn(async () => ({ notee: true as const, fiche })) }));

  remplir("Type", "Appel");
  remplir("Date et heure", "2026-11-10T10:00");
  remplir("Note (facultative)", "  Rappel de la responsable\nmardi  ");
  fireEvent.click(screen.getByRole("button", { name: "Ajouter à l'historique" }));

  await waitFor(() => expect(quandNotee).toHaveBeenCalledExactlyOnceWith(fiche));
  expect(client.noterUneEtape).toHaveBeenCalledExactlyOnceWith(7, {
    type: "Appel",
    survenuLe: "2026-11-10T10:00",
    note: "  Rappel de la responsable\nmardi  ",
  });
  expect(screen.getByRole("status").textContent).toBe("Étape ajoutée à l'historique : Appel.");
  expect(screen.getByLabelText<HTMLTextAreaElement>("Note (facultative)").value).toBe("");
  expect(screen.getByLabelText<HTMLInputElement>("Date et heure").value).toBe("2026-11-18T14:30");
});

test("les refus de l'API s'affichent à côté du champ concerné, et le premier champ refusé reçoit le focus", async () => {
  const client = clientSimule({
    noterUneEtape: async () => ({
      notee: false,
      refus: {
        survenuLe: ["La date et l'heure ne peuvent pas précéder l'envoi de la candidature."],
        note: ["La note ne doit pas dépasser 2000 caractères."],
      },
    }),
  });
  const { quandNotee } = afficher(client);

  fireEvent.click(screen.getByRole("button", { name: "Ajouter à l'historique" }));

  const date = screen.getByLabelText("Date et heure");
  await waitFor(() => expect(date.getAttribute("aria-invalid")).toBe("true"));
  expect(document.getElementById(date.getAttribute("aria-describedby")!)?.textContent).toBe(
    "La date et l'heure ne peuvent pas précéder l'envoi de la candidature.",
  );
  const note = screen.getByLabelText("Note (facultative)");
  expect(document.getElementById(note.getAttribute("aria-describedby")!)?.textContent).toBe("La note ne doit pas dépasser 2000 caractères.");
  expect(screen.getByLabelText("Type").getAttribute("aria-invalid")).toBeNull();
  await waitFor(() => expect(document.activeElement).toBe(date));
  expect(quandNotee).not.toHaveBeenCalled();
});

test("corriger un champ refusé efface son refus, sans déplacer le focus, et un échec suivant n'en laisse aucun", async () => {
  const noterUneEtape = vi
    .fn<ReturnType<typeof clientSimule>["noterUneEtape"]>()
    .mockResolvedValueOnce({
      notee: false,
      refus: { survenuLe: ["Indiquez la date et l'heure."], note: ["La note ne doit pas dépasser 2000 caractères."] },
    })
    .mockRejectedValueOnce(new Error("panne"));
  afficher(clientSimule({ noterUneEtape }));

  fireEvent.click(screen.getByRole("button", { name: "Ajouter à l'historique" }));
  const date = screen.getByLabelText("Date et heure");
  await waitFor(() => expect(document.activeElement).toBe(date));
  remplir("Date et heure", "2026-11-10T10:00");

  expect(date.getAttribute("aria-invalid")).toBeNull();
  expect(screen.getByLabelText("Note (facultative)").getAttribute("aria-invalid")).toBe("true");
  await new Promise((resolve) => setTimeout(resolve, 20));
  expect(document.activeElement).toBe(date);

  fireEvent.click(screen.getByRole("button", { name: "Ajouter à l'historique" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "L'étape n'a pas pu être ajoutée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(screen.getByLabelText("Note (facultative)").getAttribute("aria-invalid")).toBeNull();
  expect(screen.queryByText("La note ne doit pas dépasser 2000 caractères.")).toBeNull();
  // La saisie reste là, à renvoyer.
  expect(screen.getByLabelText<HTMLInputElement>("Date et heure").value).toBe("2026-11-10T10:00");
});

test("le bouton attend la réponse de l'API, et un second clic n'envoie rien", async () => {
  let repondre: () => void = () => {};
  const noterUneEtape = vi.fn(
    () => new Promise<{ notee: true; fiche: ReturnType<typeof ficheFictive> }>((resoudre) => (repondre = () => resoudre({ notee: true, fiche: ficheFictive() }))),
  );
  afficher(clientSimule({ noterUneEtape }));

  fireEvent.click(screen.getByRole("button", { name: "Ajouter à l'historique" }));
  const bouton = await screen.findByRole("button", { name: "Ajout…" });
  expect(bouton.hasAttribute("disabled")).toBe(true);
  fireEvent.click(bouton);
  expect(noterUneEtape).toHaveBeenCalledOnce();

  repondre();
  expect(await screen.findByRole("button", { name: "Ajouter à l'historique" })).toBeDefined();
});
