import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi, type FicheDUneOffre as Fiche } from "../api/clientDeLApi.ts";
import { candidatureDeLOffreFictive, clientSimule, offreFictive, offreSignaleeFictive } from "../tests/clientSimule.ts";
import { FicheDUneOffre } from "./FicheDUneOffre.tsx";

const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script><b>gras</b>";

function afficher(fiche: Fiche | Error, annonce?: string) {
  const client = clientSimule({ lireUneOffre: async () => (fiche instanceof Error ? Promise.reject(fiche) : fiche) });
  const revenir = vi.fn();
  const modifier = vi.fn();
  const postuler = vi.fn();
  const ouvrirUneOffre = vi.fn();
  const ouvrirLaCandidature = vi.fn();
  render(
    <FicheDUneOffre
      client={client}
      offre={3}
      annonce={annonce}
      revenir={revenir}
      modifier={modifier}
      postuler={postuler}
      ouvrirUneOffre={ouvrirUneOffre}
      ouvrirLaCandidature={ouvrirLaCandidature}
    />,
  );
  return { revenir, modifier, postuler, ouvrirUneOffre, ouvrirLaCandidature };
}

test("la fiche montre les champs de l'offre, sa source, son annonce par un lien externe et son texte", async () => {
  afficher(offreFictive(), "Offre enregistrée : Développeur .NET (H/F), chez Lumière Verte SA.");

  const titre = await screen.findByRole("heading", { level: 2, name: "Développeur .NET (H/F)" });
  await waitFor(() => expect(document.activeElement).toBe(titre));
  expect(screen.getByRole("status").textContent).toBe("Offre enregistrée : Développeur .NET (H/F), chez Lumière Verte SA.");
  const resume = Array.from(document.querySelectorAll("dt")).map((dt) => [dt.textContent, dt.nextElementSibling?.textContent]);
  expect(resume).toEqual([
    ["Entreprise", "Lumière Verte SA"],
    ["Lieu", "Liège"],
    ["Publiée le", "mardi 17 novembre 2026"],
    ["Ajoutée le", "18/11/2026 14:30"],
    ["Source", "Texte collé"],
    ["Annonce", "https://emploi.exemple.invalid/offres/123"],
  ]);
  const lien = screen.getByRole("link", { name: "https://emploi.exemple.invalid/offres/123" });
  expect(lien.getAttribute("target")).toBe("_blank");
  expect(document.querySelector(".texte-de-l-offre")?.textContent).toBe("Développeur .NET (H/F)\n\nMissions : maintenir l'application.");
});

test("une offre anonyme, sans lieu, date ni annonce, le dit sans rien inventer", async () => {
  afficher(offreFictive({ entreprise: null, lieu: null, publieeLe: null, adresse: null }));

  await screen.findByRole("heading", { level: 2 });
  expect(Array.from(document.querySelectorAll("dt")).map((dt) => dt.textContent)).toEqual(["Entreprise", "Ajoutée le", "Source"]);
  expect(screen.getByText("Entreprise non indiquée")).toBeDefined();
  expect(screen.queryByRole("link")).toBeNull();
});

test.each([
  ["des balises et un script", `${PIEGE}\n${PIEGE}`],
  ["des accents et des sauts de ligne", "Développeuse « senior »\n\n\tCœur de métier : l'été, à Liège.\r\nFin."],
  ["un texte très long", `${"Mission fictive. ".repeat(3_000)}${PIEGE}`],
])("un texte piégé s'affiche tel quel et échappé : %s", async (_cas, texte) => {
  afficher(offreFictive({ texte, poste: PIEGE, lieu: PIEGE, entreprise: { id: 1, nom: PIEGE } }));

  await screen.findByRole("heading", { level: 2, name: PIEGE });
  expect(document.querySelector(".texte-de-l-offre")?.textContent).toBe(texte);
  expect(document.querySelector("img, script, b")).toBeNull();
  expect(screen.getAllByText(PIEGE).length).toBeGreaterThan(0);
});

test("une adresse qui n'est pas en http ou https, venue d'une base restaurée, s'affiche en texte sans lien", async () => {
  afficher(offreFictive({ adresse: "javascript:alert('fictif')" }));

  await screen.findByRole("heading", { level: 2 });
  expect(screen.queryByRole("link")).toBeNull();
  expect(screen.getByText("javascript:alert('fictif')").tagName).toBe("SPAN");
});

test("les doublons possibles se montrent, et chacun s'ouvre ; la fiche mène à la correction et à la liste des offres", async () => {
  const { ouvrirUneOffre, modifier, revenir } = afficher(offreFictive({ doublonsPossibles: [offreSignaleeFictive({ lieu: null })] }));

  const doublons = await screen.findByRole("region", { name: "Doublons possibles" });
  expect(within(doublons).getByRole("listitem").textContent).toContain("Développeur .NET, chez Lumière Verte SA, ajoutée le 10/11/2026 09:00");
  fireEvent.click(within(doublons).getByRole("button", { name: "Voir cette offre" }));
  expect(ouvrirUneOffre).toHaveBeenCalledWith(2);
  fireEvent.click(screen.getByRole("button", { name: "Modifier l'offre" }));
  expect(modifier).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "Revenir aux offres" }));
  expect(revenir).toHaveBeenCalledOnce();
});

test("une offre qui a son entreprise et pas de candidature mène à « Postuler » (#38)", async () => {
  const { postuler } = afficher(offreFictive());

  fireEvent.click(await screen.findByRole("button", { name: "Postuler" }));

  expect(postuler).toHaveBeenCalledOnce();
  expect(screen.queryByRole("button", { name: "Voir la candidature" })).toBeNull();
});

test("une offre sans entreprise demande d'abord la sienne, par sa correction, et ne propose pas de postuler", async () => {
  const { modifier, postuler } = afficher(offreFictive({ entreprise: null }));

  expect(await screen.findByText("Pour postuler, indiquez d'abord l'entreprise de l'offre.")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Indiquer l'entreprise" }));

  expect(modifier).toHaveBeenCalledOnce();
  expect(screen.queryByRole("button", { name: "Postuler" })).toBeNull();
  expect(postuler).not.toHaveBeenCalled();
});

test("une offre qui a sa candidature la montre et y mène, sans proposer de postuler de nouveau", async () => {
  const { ouvrirLaCandidature } = afficher(offreFictive({ candidature: candidatureDeLOffreFictive({ statut: "Entretien" }) }));

  expect(await screen.findByText("Candidature envoyée le 18/11/2026 10:00 : Entretien.")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Voir la candidature" }));

  expect(ouvrirLaCandidature).toHaveBeenCalledWith(5);
  expect(screen.queryByRole("button", { name: "Postuler" })).toBeNull();
});

test("un doublon possible déjà postulé le dit, avec le statut de sa candidature", async () => {
  afficher(offreFictive({ doublonsPossibles: [offreSignaleeFictive({ candidature: candidatureDeLOffreFictive({ statut: "Refusee" }) })] }));

  const doublons = await screen.findByRole("region", { name: "Doublons possibles" });
  expect(within(doublons).getByRole("listitem").textContent).toContain("ajoutée le 10/11/2026 09:00 ; déjà postulée : Refusée");
});

test("une offre introuvable, ou illisible, le dit", async () => {
  afficher(new ErreurDeLApi(404));
  expect((await screen.findByRole("alert")).textContent).toBe("Cette offre n'existe pas.");
});
