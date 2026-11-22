import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { ClientDeLApi } from "../api/clientDeLApi.ts";
import { clientSimule, offreFictive, offreSignaleeFictive } from "../tests/clientSimule.ts";
import { SaisieDUneOffre } from "./SaisieDUneOffre.tsx";

const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script><b>gras</b>";

function remplir(libelle: string, valeur: string) {
  fireEvent.change(screen.getByLabelText(libelle), { target: { value: valeur } });
}

function afficher(client: ClientDeLApi = clientSimule()) {
  const quandEnregistree = vi.fn();
  const ouvrirUneOffre = vi.fn();
  const renoncer = vi.fn();
  render(<SaisieDUneOffre client={client} quandEnregistree={quandEnregistree} ouvrirUneOffre={ouvrirUneOffre} renoncer={renoncer} />);
  return { client, quandEnregistree, ouvrirUneOffre, renoncer };
}

test("l'écran demande le texte collé, puis les champs à remplir à la main, sans borne de longueur sur le texte", () => {
  afficher();

  expect(screen.getByRole("heading", { level: 2, name: "Coller une offre" })).toBeDefined();
  const texte = screen.getByLabelText<HTMLTextAreaElement>("Texte de l'offre");
  expect(texte.tagName).toBe("TEXTAREA");
  expect(texte.getAttribute("maxlength")).toBeNull();
  expect(document.getElementById(texte.getAttribute("aria-describedby")!)?.textContent).toContain("gardée telle quelle, 50 000 caractères au plus");
  for (const libelle of ["Entreprise", "Poste", "Lieu", "Date de publication", "Adresse de l'annonce"]) {
    expect(screen.getByLabelText<HTMLInputElement>(libelle).value).toBe("");
  }
  expect(screen.getByLabelText("Date de publication").getAttribute("type")).toBe("date");
  expect(screen.getByLabelText("Adresse de l'annonce").getAttribute("type")).toBe("url");
});

test("le texte et les champs partent tels quels vers l'API, piégés, très longs ou accentués compris, puis l'offre s'ouvre", async () => {
  const { client, quandEnregistree } = afficher();
  const texte = `  ${PIEGE}\nDeuxième ligne : « Œuvres » & été.\n\n\t${"a".repeat(60_000)}  `;

  remplir("Texte de l'offre", texte);
  remplir("Entreprise", " Lumière Verte SA ");
  remplir("Poste", "Développeur .NET (H/F)");
  remplir("Lieu", "Liège");
  remplir("Date de publication", "2026-11-17");
  remplir("Adresse de l'annonce", "https://emploi.exemple.invalid/offres/123");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledOnce());
  // Rien n'est retouché ni borné à l'écran : l'API lit et valide.
  expect(client.collerUneOffre).toHaveBeenCalledWith(
    {
      texte,
      entreprise: " Lumière Verte SA ",
      poste: "Développeur .NET (H/F)",
      lieu: "Liège",
      publieeLe: "2026-11-17",
      adresse: "https://emploi.exemple.invalid/offres/123",
    },
    false,
  );
  expect(document.querySelector("img, script, b")).toBeNull();
});

test("les refus de l'API s'affichent à côté de leur champ, ligne du caractère refusé comprise, et le premier reçoit le focus", async () => {
  const client = clientSimule({
    collerUneOffre: async () => ({
      issue: "refusee",
      refus: {
        texte: ["Le texte de l'offre contient, à la ligne 3, un caractère de contrôle ou invalide : retirez-le, puis réessayez."],
        publieeLe: ["La date de publication ne peut pas être après aujourd'hui."],
      },
    }),
  });
  afficher(client);

  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  const texte = screen.getByLabelText("Texte de l'offre");
  await waitFor(() => expect(texte.getAttribute("aria-invalid")).toBe("true"));
  await waitFor(() => expect(document.activeElement).toBe(texte));
  expect(texte.getAttribute("aria-describedby")!.split(" ").map((id) => document.getElementById(id)?.textContent)).toContain(
    "Le texte de l'offre contient, à la ligne 3, un caractère de contrôle ou invalide : retirez-le, puis réessayez.",
  );
  expect(screen.getByLabelText("Date de publication").getAttribute("aria-invalid")).toBe("true");
  expect(screen.getByLabelText("Poste").getAttribute("aria-invalid")).toBeNull();
  // Corriger un champ efface son refus.
  remplir("Date de publication", "2026-11-17");
  expect(screen.getByLabelText("Date de publication").getAttribute("aria-invalid")).toBeNull();
});

test("le même texte recollé dit que l'offre est déjà enregistrée, et mène à elle", async () => {
  const client = clientSimule({
    collerUneOffre: async () => ({ issue: "dejaEnregistree", offre: { id: 7, ajouteeLe: "2026-11-18T13:30:00Z" } }),
  });
  const { ouvrirUneOffre, quandEnregistree } = afficher(client);

  remplir("Texte de l'offre", "Offre fictive.");
  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  const signal = await screen.findByRole("alert");
  const titre = within(signal).getByRole("heading", { level: 3 });
  expect(titre.textContent).toBe("Cette offre est déjà enregistrée : vous l'avez collée le mercredi 18 novembre 2026. Rien n'a été ajouté.");
  await waitFor(() => expect(document.activeElement).toBe(titre));
  fireEvent.click(within(signal).getByRole("button", { name: "Voir l'offre enregistrée" }));
  expect(ouvrirUneOffre).toHaveBeenCalledWith(7);
  expect(quandEnregistree).not.toHaveBeenCalled();
});

test("un doublon possible se montre avant tout enregistrement : garder les deux renvoie la saisie avec l'accord", async () => {
  const collerUneOffre = vi
    .fn<ClientDeLApi["collerUneOffre"]>()
    .mockResolvedValueOnce({ issue: "doublonPossible", doublons: [offreSignaleeFictive({ poste: PIEGE, entreprise: { id: 1, nom: PIEGE } })] })
    .mockResolvedValueOnce({ issue: "enregistree", fiche: offreFictive({ id: 12 }) });
  const { quandEnregistree, ouvrirUneOffre } = afficher(clientSimule({ collerUneOffre }));

  remplir("Texte de l'offre", "Seconde offre fictive.");
  remplir("Entreprise", "Lumière Verte SA");
  remplir("Poste", "Développeur .NET");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  const signal = await screen.findByRole("alert");
  expect(within(signal).getByRole("heading", { level: 3 }).textContent).toBe(
    "Doublon possible : une offre du même poste, dans la même entreprise, est déjà enregistrée.",
  );
  expect(within(signal).getByRole("listitem").textContent).toContain(`${PIEGE}, chez ${PIEGE} (Namur), ajoutée le 10/11/2026 09:00`);
  expect(signal.querySelector("img, script, b")).toBeNull();
  fireEvent.click(within(signal).getByRole("button", { name: "Voir cette offre" }));
  expect(ouvrirUneOffre).toHaveBeenCalledWith(2);

  fireEvent.click(within(signal).getByRole("button", { name: "Garder les deux" }));
  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledOnce());
  expect(collerUneOffre).toHaveBeenLastCalledWith(expect.objectContaining({ texte: "Seconde offre fictive.", poste: "Développeur .NET" }), true);
});

test("renoncer à la nouvelle n'enregistre rien, et modifier la saisie retire le signal", async () => {
  const client = clientSimule({ collerUneOffre: async () => ({ issue: "doublonPossible", doublons: [offreSignaleeFictive()] }) });
  const { renoncer } = afficher(client);

  remplir("Texte de l'offre", "Seconde offre fictive.");
  remplir("Poste", "Développeur .NET");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));
  fireEvent.click(await screen.findByRole("button", { name: "Renoncer à la nouvelle" }));
  expect(renoncer).toHaveBeenCalledOnce();

  remplir("Poste", "Développeur Java");
  expect(screen.queryByRole("alert")).toBeNull();
});

test("les entreprises proposées s'affichent échappées, et l'une d'elles remplit le champ", async () => {
  afficher(clientSimule({ listerLesEntreprises: async () => [{ id: 1, nom: "Lumière Verte SA" }, { id: 2, nom: `Lumière ${PIEGE}` }] }));

  remplir("Entreprise", "lumi");

  const propositions = await screen.findByRole("list", { name: "Entreprises déjà enregistrées :" });
  expect(within(propositions).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual(["Lumière Verte SA", `Lumière ${PIEGE}`]);
  expect(propositions.querySelector("img, script, b")).toBeNull();
  fireEvent.click(within(propositions).getByRole("button", { name: "Lumière Verte SA" }));
  expect(screen.getByLabelText<HTMLInputElement>("Entreprise").value).toBe("Lumière Verte SA");
});

test("un envoi qui échoue le dit, sans rien perdre de la saisie", async () => {
  afficher(clientSimule({ collerUneOffre: async () => Promise.reject(new Error("panne fictive")) }));

  remplir("Texte de l'offre", "Offre fictive.");
  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "L'offre n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(screen.getByLabelText<HTMLTextAreaElement>("Texte de l'offre").value).toBe("Offre fictive.");
});
