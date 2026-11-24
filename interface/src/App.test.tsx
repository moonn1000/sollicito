import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { App } from "./App.tsx";
import { ErreurDeLApi } from "./api/clientDeLApi.ts";
import { HorsDeLaFenetre } from "./connexion.ts";
import { candidatureFictive, changementFictif, clientSimule, etatDeLIAFictif, ficheFictive, offreListeeFictive, sauvegardesFictives } from "./tests/clientSimule.ts";

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
    "Offres",
    "Réglages",
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
  expect(within(navigation).getAllByRole("button").map((bouton) => bouton.getAttribute("aria-current"))).toEqual([null, null, null, null]);
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
      return { issue: "modifiee", fiche };
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

test("la navigation mène aux réglages, dont la section des sauvegardes choisit un dossier par la fenêtre hôte", async () => {
  const choisirLeDossierDesSauvegardes = vi.fn(async () => "annule" as const);
  render(<App connecter={async () => clientSimule()} choisirLeDossierDesSauvegardes={choisirLeDossierDesSauvegardes} />);

  fireEvent.click(await screen.findByRole("button", { name: "Réglages" }));

  const titre = await screen.findByRole("heading", { level: 2, name: "Réglages" });
  await waitFor(() => expect(document.activeElement).toBe(titre));
  expect(screen.getByRole("button", { name: "Réglages" }).getAttribute("aria-current")).toBe("page");
  fireEvent.click(await within(screen.getByRole("region", { name: "Sauvegardes" })).findByRole("button", { name: "Choisir un autre dossier…" }));
  expect(await screen.findByText("Le dossier des sauvegardes n'a pas changé.")).toBeDefined();
  expect(choisirLeDossierDesSauvegardes).toHaveBeenCalledOnce();
});

test("l'alerte des sauvegardes du tableau de bord mène aux réglages", async () => {
  const client = clientSimule({
    lireLesSauvegardes: async () => sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-19T08:00:00Z" } }),
  });
  render(<App connecter={async () => client} />);

  fireEvent.click(await within(await screen.findByRole("alert")).findByRole("button", { name: "Ouvrir les réglages" }));

  expect(await screen.findByRole("heading", { level: 2, name: "Réglages" })).toBeDefined();
  expect(await screen.findByRole("region", { name: "Sauvegardes" })).toBeDefined();
});

test("une copie restaurée ramène au tableau de bord, qui relit la base et annonce la restauration (#28)", async () => {
  const client = clientSimule({
    listerLesCandidatures: vi
      .fn()
      .mockResolvedValueOnce([candidatureFictive()])
      .mockResolvedValue([candidatureFictive({ id: 9, poste: "Poste fictif de la copie" })]),
  });
  const restaurerUneSauvegarde = vi.fn(async (_copie: string, quandEnCours: () => void) => {
    quandEnCours();
    return "restauree" as const;
  });
  render(<App connecter={async () => client} restaurerUneSauvegarde={restaurerUneSauvegarde} />);
  await screen.findByRole("heading", { level: 2, name: "Tableau de bord" });
  await waitFor(() => expect(client.listerLesCandidatures).toHaveBeenCalledOnce());

  fireEvent.click(screen.getByRole("button", { name: "Réglages" }));
  fireEvent.click(await screen.findByRole("button", { name: "Restaurer la copie du 18 novembre 2026 à 14:30" }));

  expect(
    await screen.findByText(
      "Base restaurée : vous retrouvez la copie du 18 novembre 2026 à 14:30. Votre base d'avant est gardée dans la copie « Avant une restauration ou un import ».",
    ),
  ).toBeDefined();
  expect(screen.getByRole("heading", { level: 2, name: "Tableau de bord" })).toBeDefined();
  expect(await screen.findByText("Poste fictif de la copie")).toBeDefined();
  expect(restaurerUneSauvegarde).toHaveBeenCalledWith("0123456789abcdef", expect.any(Function));
  expect(client.listerLesCandidatures).toHaveBeenCalledTimes(2);
});

test("une offre collée s'enregistre, puis sa fiche s'ouvre avec l'annonce, et mène à sa correction (#36)", async () => {
  const client = clientSimule();
  render(<App connecter={async () => client} />);
  await screen.findByRole("heading", { level: 2, name: "Tableau de bord" });

  // « Coller une offre » s'ouvre depuis la liste des offres (#38), qui reste l'entrée de la navigation.
  fireEvent.click(screen.getByRole("button", { name: "Offres" }));
  fireEvent.click(await screen.findByRole("button", { name: "Coller une offre" }));
  expect(screen.getByRole("heading", { level: 2, name: "Coller une offre" })).toBeDefined();
  expect(screen.getByRole("button", { name: "Offres" }).getAttribute("aria-current")).toBe("page");
  fireEvent.change(await screen.findByLabelText("Texte de l'offre"), { target: { value: "Offre fictive." } });
  fireEvent.change(screen.getByLabelText("Poste"), { target: { value: "Comptable" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  expect(await screen.findByText("Offre enregistrée : Comptable, chez Lumière Verte SA.")).toBeDefined();
  expect(await screen.findByRole("heading", { level: 2, name: "Développeur .NET (H/F)" })).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Modifier l'offre" }));
  expect(await screen.findByRole("heading", { level: 2, name: "Modifier l'offre" })).toBeDefined();
  expect(client.lireUneOffre).toHaveBeenCalledWith(12);
});

test("depuis la liste des offres, « Postuler » enregistre la candidature et ouvre sa fiche, qui mène à l'offre (#38)", async () => {
  const client = clientSimule({
    listerLesOffres: async () => [offreListeeFictive()],
    lireLaFiche: async (candidature) => ficheFictive({ id: candidature, poste: "Développeur .NET (H/F)", offre: 3 }),
  });
  render(<App connecter={async () => client} />);
  await screen.findByRole("heading", { level: 2, name: "Tableau de bord" });

  fireEvent.click(screen.getByRole("button", { name: "Offres" }));
  fireEvent.click(await screen.findByRole("button", { name: "Développeur .NET (H/F), chez Lumière Verte SA : ouvrir la fiche" }));
  fireEvent.click(await screen.findByRole("button", { name: "Postuler" }));
  expect(await screen.findByRole("heading", { level: 2, name: "Postuler à cette offre" })).toBeDefined();
  fireEvent.click(await screen.findByRole("button", { name: "Enregistrer la candidature" }));

  expect(await screen.findByText("Candidature enregistrée depuis l'offre : Développeur .NET (H/F), chez Lumière Verte SA.")).toBeDefined();
  expect(client.postuler).toHaveBeenCalledWith(3, expect.stringMatching(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/u));
  fireEvent.click(await screen.findByRole("button", { name: "Voir l'offre" }));
  expect(await screen.findByRole("heading", { level: 2, name: "Développeur .NET (H/F)" })).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Revenir aux offres" }));
  expect(await screen.findByRole("heading", { level: 2, name: "Offres" })).toBeDefined();
});

test("les filtres de la liste des offres restent quand on ouvre une fiche, puis qu'on revient (#38)", async () => {
  const listerLesOffres = vi.fn(async () => [offreListeeFictive(), offreListeeFictive({ id: 4, poste: "Comptable", lieu: "Namur" })]);
  const client = clientSimule({ listerLesOffres });
  render(<App connecter={async () => client} />);
  await screen.findByRole("heading", { level: 2, name: "Tableau de bord" });

  fireEvent.click(screen.getByRole("button", { name: "Offres" }));
  fireEvent.change(await screen.findByLabelText("Rechercher"), { target: { value: "namur" } });
  fireEvent.click(await screen.findByRole("button", { name: "Comptable, chez Lumière Verte SA : ouvrir la fiche" }));
  // La fiche lue d'abord : son bouton de retour remplace celui du chargement.
  await screen.findByRole("heading", { level: 2, name: "Développeur .NET (H/F)" });
  fireEvent.click(screen.getByRole("button", { name: "Revenir aux offres" }));

  expect(await screen.findByText("1 offre sur 2")).toBeDefined();
  expect(screen.getByLabelText<HTMLInputElement>("Rechercher").value).toBe("namur");
  // Lue à chaque ouverture, toujours sans paramètre : la recherche ne quitte pas la page.
  expect(listerLesOffres).toHaveBeenCalledTimes(2);
  expect(listerLesOffres.mock.calls.every((appel) => appel.length === 0)).toBe(true);
});

test("la carte « Connecter une IA ? » mène à la section « IA » des réglages, qui prend le focus (#43)", async () => {
  render(<App connecter={async () => clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ choisi: false }) })} />);

  fireEvent.click(await screen.findByRole("button", { name: "Choisir une IA" }));

  const section = await screen.findByRole("heading", { level: 3, name: "IA" });
  await waitFor(() => expect(document.activeElement).toBe(section));
  expect(screen.getByRole("heading", { level: 2, name: "Réglages" })).toBeDefined();
  const navigation = screen.getByRole("navigation", { name: "Navigation principale" });
  expect(within(navigation).getByRole("button", { name: "Réglages" }).getAttribute("aria-current")).toBe("page");
});
