import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { App } from "./App.tsx";
import { ErreurDeLApi } from "./api/clientDeLApi.ts";
import { HorsDeLaFenetre } from "./connexion.ts";
import { candidatureFictive, changementFictif, clientSimule, ficheFictive } from "./tests/clientSimule.ts";

test("l'écran d'accueil affiche le nom de l'application", () => {
  render(<App connecter={() => new Promise(() => {})} />);

  expect(screen.getByRole("heading", { level: 1, name: "Sollicito" })).toBeDefined();
  expect(screen.getByRole("status").textContent).toBe("Connexion à l'API locale…");
  // Tant que l'API n'est pas jointe, aucune navigation.
  expect(screen.queryByRole("navigation")).toBeNull();
});

test("connectée, l'interface le dit et ouvre le tableau de bord, dans la mise en page commune", async () => {
  render(<App connecter={async () => clientSimule()} />);

  expect(await screen.findByText("Connectée à l'API locale.")).toBeDefined();
  const navigation = screen.getByRole("navigation", { name: "Navigation principale" });
  expect(within(navigation).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual([
    "Tableau de bord",
    "Nouvelle candidature",
  ]);
  expect(within(navigation).getByRole("button", { name: "Tableau de bord" }).getAttribute("aria-current")).toBe("page");
  expect(screen.getByRole("main")).toBeDefined();
  // Au premier affichage, le focus reste où il est.
  expect(document.activeElement).not.toBe(await screen.findByRole("heading", { level: 2, name: "Tableau de bord" }));
});

test("hors de la fenêtre de Sollicito, l'interface le dit", async () => {
  render(<App connecter={() => Promise.reject(new HorsDeLaFenetre())} />);

  expect(await screen.findByText("Cette page ne fonctionne que dans la fenêtre de Sollicito.")).toBeDefined();
});

test("si l'API refuse ou ne répond pas, l'interface le dit", async () => {
  const clientRefuse = clientSimule({ lireEtat: () => Promise.reject(new ErreurDeLApi(401)) });

  render(<App connecter={async () => clientRefuse} />);

  expect(await screen.findByText("L'API locale ne répond pas. Fermez Sollicito, puis relancez-le.")).toBeDefined();
  expect(screen.queryByRole("navigation")).toBeNull();
});

test("une candidature saisie s'enregistre, puis le tableau de bord la liste et l'annonce", async () => {
  const enregistrees: ReturnType<typeof candidatureFictive>[] = [];
  const client = clientSimule({
    listerLesCandidatures: async () => [...enregistrees],
    enregistrerUneCandidature: async (saisie) => {
      const candidature = candidatureFictive({ id: 7, entreprise: { id: 4, nom: saisie.entreprise.trim() }, poste: saisie.poste });
      enregistrees.unshift(candidature);
      return { enregistree: true, candidature };
    },
  });
  render(<App connecter={async () => client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Nouvelle candidature" }));
  expect(screen.getByRole("button", { name: "Nouvelle candidature" }).getAttribute("aria-current")).toBe("page");
  fireEvent.change(screen.getByLabelText("Entreprise"), { target: { value: "Brasserie Lune Rousse" } });
  fireEvent.change(screen.getByLabelText("Poste"), { target: { value: "Brasseuse" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));

  expect(await screen.findByText("Candidature enregistrée : Brasseuse, chez Brasserie Lune Rousse.")).toBeDefined();
  expect(screen.getByRole("button", { name: "Tableau de bord" }).getAttribute("aria-current")).toBe("page");
  // Mené au tableau de bord, l'utilisateur y trouve le focus, sur son titre.
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { level: 2, name: "Tableau de bord" })));
  await waitFor(() => expect(screen.getAllByRole("row")).toHaveLength(2));
  expect(within(screen.getAllByRole("row")[1]!).getAllByRole("cell").map((cellule) => cellule.textContent)).toEqual([
    "Brasserie Lune Rousse",
    "Brasseuse",
    "27/03/2026 23:30",
    "Envoyée",
  ]);
});

test("depuis le tableau de bord, on ouvre la fiche, on fait avancer la candidature, et le tableau de bord montre le statut à jour", async () => {
  let fiche = ficheFictive({ id: 7, poste: "Brasseuse", entreprise: { id: 4, nom: "Brasserie Lune Rousse" } });
  const client = clientSimule({
    listerLesCandidatures: async () => [candidatureFictive({ id: fiche.id, poste: fiche.poste, entreprise: fiche.entreprise, statut: fiche.statut })],
    lireLaFiche: async () => fiche,
    changerLeStatut: async (_, statut) => {
      fiche = ficheFictive({ ...fiche, statut, passagesPermis: ["Proposition", "Refusee", "Retiree", "SansReponse"], historique: [changementFictif()] });
      return { faite: true, fiche };
    },
  });
  render(<App connecter={async () => client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Brasseuse, chez Brasserie Lune Rousse : ouvrir la fiche" }));

  // La fiche n'est pas une entrée de la navigation : aucune n'est marquée comme la page présente.
  const titre = await screen.findByRole("heading", { level: 2, name: "Fiche de la candidature" });
  const navigation = screen.getByRole("navigation", { name: "Navigation principale" });
  expect(within(navigation).getAllByRole("button").map((bouton) => bouton.getAttribute("aria-current"))).toEqual([null, null]);
  await waitFor(() => expect(document.activeElement).toBe(titre));
  fireEvent.click(within(await screen.findByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Entretien" }));
  expect(await screen.findByText("Statut changé : Entretien.")).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Revenir au tableau de bord" }));

  await waitFor(() => expect(screen.getAllByRole("row")).toHaveLength(2));
  expect(within(screen.getAllByRole("row")[1]!).getAllByRole("cell")[3]!.textContent).toBe("Entretien");
  expect(within(navigation).getByRole("button", { name: "Tableau de bord" }).getAttribute("aria-current")).toBe("page");
});

test("depuis la fiche, on modifie la candidature, puis la fiche l'annonce avec l'entreprise rattachée, et le tableau de bord la montre à jour", async () => {
  let fiche = ficheFictive({ id: 7, poste: "Brasseuse", entreprise: { id: 4, nom: "Brasserie Lune Rousse" } });
  const client = clientSimule({
    listerLesCandidatures: async () => [candidatureFictive({ id: fiche.id, poste: fiche.poste, entreprise: fiche.entreprise, envoyeeLe: fiche.envoyeeLe })],
    lireLaFiche: async () => fiche,
    // L'API reconnaît l'entreprise par sa clé : la fiche rendue porte le nom de sa première saisie.
    modifierUneCandidature: async (_, saisie) => {
      fiche = ficheFictive({ ...fiche, poste: saisie.poste.trim(), entreprise: { id: 1, nom: "Lumière Verte SA" } });
      return { modifiee: true, fiche };
    },
  });
  render(<App connecter={async () => client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Brasseuse, chez Brasserie Lune Rousse : ouvrir la fiche" }));
  fireEvent.click(await screen.findByRole("button", { name: "Modifier la candidature" }));
  const titre = await screen.findByRole("heading", { level: 2, name: "Modifier la candidature" });
  await waitFor(() => expect(document.activeElement).toBe(titre));
  // « Annuler » ramène à la fiche, puis on y revient.
  fireEvent.click(await screen.findByRole("button", { name: "Annuler" }));
  fireEvent.click(await screen.findByRole("button", { name: "Modifier la candidature" }));
  fireEvent.change(await screen.findByLabelText("Entreprise"), { target: { value: "LUMIÈRE VERTE S.A." } });
  fireEvent.change(screen.getByLabelText("Poste"), { target: { value: "Comptable" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer les modifications" }));

  expect(await screen.findByText("Candidature modifiée : Comptable, chez Lumière Verte SA.")).toBeDefined();
  const ficheTitre = screen.getByRole("heading", { level: 2, name: "Fiche de la candidature" });
  await waitFor(() => expect(document.activeElement).toBe(ficheTitre));
  await waitFor(() => expect(document.querySelector("dl.resume dd")?.textContent).toBe("Lumière Verte SA"));

  fireEvent.click(screen.getByRole("button", { name: "Revenir au tableau de bord" }));
  await waitFor(() => expect(screen.getAllByRole("row")).toHaveLength(2));
  expect(within(screen.getAllByRole("row")[1]!).getAllByRole("cell").map((cellule) => cellule.textContent).slice(0, 2)).toEqual([
    "Lumière Verte SA",
    "Comptable",
  ]);
  // Revenu au tableau de bord par la navigation, l'annonce de la modification n'y est plus.
  expect(screen.queryByText("Candidature modifiée : Comptable, chez Lumière Verte SA.")).toBeNull();
});

test("depuis la fiche, on supprime la candidature après confirmation, puis le tableau de bord l'annonce, sans elle", async () => {
  const fiche = ficheFictive({ id: 7, poste: "Brasseuse", entreprise: { id: 4, nom: "Brasserie Lune Rousse" } });
  let supprimee = false;
  const client = clientSimule({
    listerLesCandidatures: async () =>
      supprimee ? [] : [candidatureFictive({ id: fiche.id, poste: fiche.poste, entreprise: fiche.entreprise })],
    lireLaFiche: async () => fiche,
    supprimerUneCandidature: vi.fn(async () => {
      supprimee = true;
    }),
  });
  render(<App connecter={async () => client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Brasseuse, chez Brasserie Lune Rousse : ouvrir la fiche" }));
  fireEvent.click(await screen.findByRole("button", { name: "Supprimer la candidature" }));
  fireEvent.click(within(screen.getByRole("region", { name: "Supprimer cette candidature ?" })).getByRole("button", { name: "Supprimer définitivement" }));

  expect(await screen.findByText("Candidature supprimée : Brasseuse, chez Brasserie Lune Rousse.")).toBeDefined();
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { level: 2, name: "Tableau de bord" })));
  expect(await screen.findByText("Aucune candidature enregistrée pour l'instant.")).toBeDefined();
  expect(client.supprimerUneCandidature).toHaveBeenCalledExactlyOnceWith(7);
});
