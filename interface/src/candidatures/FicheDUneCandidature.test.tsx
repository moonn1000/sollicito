import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi, type FicheDUneCandidature as Fiche } from "../api/clientDeLApi.ts";
import { changementFictif, clientSimule, etapeFictive, ficheFictive } from "../tests/clientSimule.ts";
import { FicheDUneCandidature } from "./FicheDUneCandidature.tsx";

// Le 18/11/2026 à 14:30, heure de Bruxelles (le fuseau des tests).
const maintenant = () => new Date("2026-11-18T13:30:00Z");

function afficher(client = clientSimule(), revenir = vi.fn(), annonce?: string) {
  const modifier = vi.fn();
  const quandSupprimee = vi.fn();
  const { container } = render(
    <FicheDUneCandidature
      client={client}
      candidature={7}
      revenir={revenir}
      modifier={modifier}
      quandSupprimee={quandSupprimee}
      annonce={annonce}
      maintenant={maintenant}
    />,
  );
  return { client, revenir, modifier, quandSupprimee, container };
}

// La confirmation de la suppression, affichée dans la fiche.
function confirmation() {
  return screen.queryByRole("region", { name: "Supprimer cette candidature ?" });
}

// Le résumé de la candidature : chaque terme et sa définition.
function resume() {
  const liste = document.querySelector<HTMLElement>("dl.resume")!;
  const termes = within(liste).getAllByRole("term").map((terme) => terme.textContent);
  const definitions = within(liste).getAllByRole("definition").map((definition) => definition.textContent);
  return Object.fromEntries(termes.map((terme, rang) => [terme, definitions[rang]]));
}

function boutonsDePassage() {
  return within(screen.getByRole("group", { name: "Passer au statut :" }))
    .getAllByRole("button")
    .map((bouton) => bouton.textContent);
}

test("la fiche montre la candidature, son statut, et ne propose que les passages que l'API permet", async () => {
  const fiche = ficheFictive({ statut: "Entretien", passagesPermis: ["Proposition", "Refusee", "Retiree", "SansReponse"] });
  const { client } = afficher(clientSimule({ lireLaFiche: vi.fn(async () => fiche) }));

  expect(await screen.findByRole("group", { name: "Passer au statut :" })).toBeDefined();
  expect(client.lireLaFiche).toHaveBeenCalledWith(7);
  expect(resume()).toEqual({
    Entreprise: "Lumière Verte SA",
    Poste: "Comptable",
    "Envoyée le": "27/03/2026 23:30",
    Statut: "Entretien",
  });
  expect(boutonsDePassage()).toEqual(["Proposition", "Refusée", "Retirée", "Sans réponse"]);
  // Ouverte depuis le tableau de bord, la fiche prend le focus sur son titre.
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { level: 2, name: "Fiche de la candidature" })));
});

test("une candidature close ne propose aucun passage, seulement la correction de son dernier changement", async () => {
  const fiche = ficheFictive({
    statut: "Refusee",
    passagesPermis: [],
    correction: { changement: 4, de: "Refusee", retourA: "Entretien" },
  });
  afficher(clientSimule({ lireLaFiche: async () => fiche }));

  expect(await screen.findByText("La candidature est close : son statut ne change plus.")).toBeDefined();
  expect(screen.queryByRole("group", { name: "Passer au statut :" })).toBeNull();
  expect(screen.getByRole("button", { name: "Corriger le dernier changement : revenir à Entretien" })).toBeDefined();
});

test("sans changement à corriger, la fiche ne propose pas de correction", async () => {
  afficher();

  expect(await screen.findByRole("group", { name: "Passer au statut :" })).toBeDefined();
  expect(boutonsDePassage()).toEqual(["Entretien", "Refusée", "Retirée", "Sans réponse"]);
  expect(screen.queryByRole("button", { name: /Corriger le dernier changement/u })).toBeNull();
});

test("l'historique commence à l'envoi, puis suit l'ordre de l'API, et marque le changement corrigé", async () => {
  const fiche = ficheFictive({
    statut: "Entretien",
    historique: [
      etapeFictive({ id: 5, survenuLe: "2026-11-10T09:00:00Z", type: "Appel", note: "Rappel de la responsable\nmardi matin" }),
      changementFictif({ id: 1, survenuLe: "2026-11-18T13:30:00Z" }),
      changementFictif({ id: 2, survenuLe: "2026-11-18T14:30:00Z", ancien: "Entretien", nouveau: "Refusee", estCorrige: true }),
      changementFictif({ id: 3, survenuLe: "2026-11-18T14:35:00Z", ancien: "Refusee", nouveau: "Entretien", corrige: 2 }),
      etapeFictive({ id: 6, survenuLe: "2026-12-01T08:30:00Z", type: "Entretien", note: null }),
    ],
  });
  afficher(clientSimule({ lireLaFiche: async () => fiche }));

  const historique = await screen.findByRole("heading", { level: 3, name: "Historique" });
  const lignes = within(historique.closest("section")!).getAllByRole("listitem");
  expect(lignes.map((ligne) => ligne.textContent)).toEqual([
    "27/03/2026 23:30Candidature envoyée",
    "10/11/2026 10:00AppelRappel de la responsable\nmardi matin",
    "18/11/2026 14:30Statut passé d'Envoyée à Entretien",
    "18/11/2026 15:30Statut passé d'Entretien à Refuséecorrigé",
    "18/11/2026 15:35Correction : retour de Refusée à Entretien",
    "01/12/2026 09:30Entretien",
  ]);
  expect(within(lignes[3]!).getByText("corrigé").className).toBe("marque");
  expect(lignes[1]!.querySelector("time")?.getAttribute("dateTime")).toBe("2026-11-10T09:00:00Z");
});

test("la note d'une étape s'affiche échappée, jamais comme du HTML", async () => {
  const fiche = ficheFictive({
    entreprise: { id: 1, nom: '<img src="x" onerror="alert(1)"> & Fils' },
    historique: [etapeFictive({ note: '<b>gras</b><script>alert(2)</script><img src="x">' })],
  });
  const { container } = afficher(clientSimule({ lireLaFiche: async () => fiche }));

  expect(await screen.findByText('<b>gras</b><script>alert(2)</script><img src="x">')).toBeDefined();
  expect(screen.getByText('<img src="x" onerror="alert(1)"> & Fils')).toBeDefined();
  expect(container.querySelector("img, b, script")).toBeNull();
});

test("choisir un passage change le statut, montre la fiche à jour, l'annonce et ramène le focus sur le statut", async () => {
  const apres = ficheFictive({
    statut: "Entretien",
    passagesPermis: ["Proposition", "Refusee", "Retiree", "SansReponse"],
    correction: { changement: 1, de: "Entretien", retourA: "Envoyee" },
    historique: [changementFictif()],
  });
  const { client } = afficher(clientSimule({ changerLeStatut: vi.fn(async () => ({ faite: true as const, fiche: apres })) }));

  fireEvent.click(within(await screen.findByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Entretien" }));

  expect((await screen.findByRole("status")).textContent).toBe("Statut changé : Entretien.");
  expect(client.changerLeStatut).toHaveBeenCalledExactlyOnceWith(7, "Entretien");
  expect(resume().Statut).toBe("Entretien");
  expect(boutonsDePassage()).toEqual(["Proposition", "Refusée", "Retirée", "Sans réponse"]);
  expect(screen.getByText("Statut passé d'Envoyée à Entretien")).toBeDefined();
  // Le bouton cliqué a disparu : le focus revient sur le titre de la section.
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole("heading", { level: 3, name: "Faire avancer la candidature" })));
});

test("pendant une action, les boutons attendent la réponse, et un second clic n'envoie rien", async () => {
  let repondre: (fiche: Fiche) => void = () => {};
  const changerLeStatut = vi.fn(() => new Promise<{ faite: true; fiche: Fiche }>((resoudre) => (repondre = (fiche) => resoudre({ faite: true, fiche }))));
  afficher(clientSimule({ changerLeStatut }));

  const groupe = await screen.findByRole("group", { name: "Passer au statut :" });
  fireEvent.click(within(groupe).getByRole("button", { name: "Refusée" }));
  fireEvent.click(within(groupe).getByRole("button", { name: "Retirée" }));

  await waitFor(() => expect(within(groupe).getByRole("button", { name: "Refusée" }).hasAttribute("disabled")).toBe(true));
  expect(changerLeStatut).toHaveBeenCalledOnce();
  repondre(ficheFictive({ statut: "Refusee", passagesPermis: [] }));
  expect(await screen.findByText("La candidature est close : son statut ne change plus.")).toBeDefined();
});

test("un passage que l'API refuse affiche son motif, et la fiche est relue", async () => {
  const lireLaFiche = vi
    .fn<ReturnType<typeof clientSimule>["lireLaFiche"]>()
    .mockResolvedValueOnce(ficheFictive())
    .mockResolvedValueOnce(ficheFictive({ statut: "Refusee", passagesPermis: [] }));
  const changerLeStatut = vi.fn(async () => ({ faite: false as const, motif: "La candidature est déjà au statut Refusée." }));
  afficher(clientSimule({ lireLaFiche, changerLeStatut }));

  fireEvent.click(within(await screen.findByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Refusée" }));

  expect((await screen.findByRole("alert")).textContent).toBe("La candidature est déjà au statut Refusée.");
  await waitFor(() => expect(resume().Statut).toBe("Refusée"));
  expect(lireLaFiche).toHaveBeenCalledTimes(2);
});

test("si l'API ne répond pas, la fiche le dit sans rien changer", async () => {
  afficher(clientSimule({ changerLeStatut: () => Promise.reject(new Error("panne")) }));

  fireEvent.click(within(await screen.findByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Entretien" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "Le statut n'a pas pu être changé. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(resume().Statut).toBe("Envoyée");
  expect(within(screen.getByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Entretien" }).hasAttribute("disabled")).toBe(false);
});

test("la correction vise le changement que la fiche propose de corriger, et s'annonce", async () => {
  const fiche = ficheFictive({
    statut: "Entretien",
    passagesPermis: ["Proposition", "Refusee", "Retiree", "SansReponse"],
    correction: { changement: 12, de: "Entretien", retourA: "Envoyee" },
    historique: [changementFictif({ id: 12 })],
  });
  // La correction s'ajoute à l'historique : la fiche rendue a un événement de plus.
  const corrigee = ficheFictive({
    historique: [changementFictif({ id: 12, estCorrige: true }), changementFictif({ id: 13, ancien: "Entretien", nouveau: "Envoyee", corrige: 12 })],
  });
  const { client } = afficher(
    clientSimule({ lireLaFiche: async () => fiche, corrigerLeDernierChangement: vi.fn(async () => ({ faite: true as const, fiche: corrigee })) }),
  );

  fireEvent.click(await screen.findByRole("button", { name: "Corriger le dernier changement : revenir à Envoyée" }));

  expect((await screen.findByRole("status")).textContent).toBe("Dernier changement corrigé : retour à Envoyée.");
  expect(client.corrigerLeDernierChangement).toHaveBeenCalledExactlyOnceWith(7, 12);
  expect(resume().Statut).toBe("Envoyée");
});

test("une candidature introuvable le dit", async () => {
  afficher(clientSimule({ lireLaFiche: () => Promise.reject(new ErreurDeLApi(404)) }));
  expect((await screen.findByRole("alert")).textContent).toBe("Cette candidature n'existe pas, ou plus.");
});

test("une fiche qui ne se charge pas le dit", async () => {
  afficher(clientSimule({ lireLaFiche: () => Promise.reject(new ErreurDeLApi(500)) }));
  expect((await screen.findByRole("alert")).textContent).toBe("La fiche n'a pas pu être chargée. Fermez Sollicito, puis relancez-le.");
});

test("le bouton de retour mène au tableau de bord", async () => {
  const { revenir } = afficher();

  fireEvent.click(screen.getByRole("button", { name: "Revenir au tableau de bord" }));

  expect(revenir).toHaveBeenCalledOnce();
});

test("une étape notée entre dans l'historique sans changer le statut", async () => {
  const apres = ficheFictive({ historique: [etapeFictive({ type: "Echange", note: "Relance par courriel" })] });
  const { client } = afficher(clientSimule({ noterUneEtape: vi.fn(async () => ({ notee: true as const, fiche: apres })) }));

  fireEvent.change(await screen.findByLabelText("Type"), { target: { value: "Echange" } });
  fireEvent.change(screen.getByLabelText("Note (facultative)"), { target: { value: "Relance par courriel" } });
  fireEvent.click(screen.getByRole("button", { name: "Ajouter à l'historique" }));

  expect(await screen.findByText("Relance par courriel", { selector: "p.note" })).toBeDefined();
  expect(client.noterUneEtape).toHaveBeenCalledExactlyOnceWith(7, { type: "Echange", survenuLe: "2026-11-18T14:30", note: "Relance par courriel" });
  expect(resume().Statut).toBe("Envoyée");
  expect(boutonsDePassage()).toEqual(["Entretien", "Refusée", "Retirée", "Sans réponse"]);
});

test("une action sur le statut efface l'annonce de la dernière étape ajoutée, et inversement", async () => {
  afficher();

  fireEvent.click(await screen.findByRole("button", { name: "Ajouter à l'historique" }));
  expect(await screen.findByText("Étape ajoutée à l'historique : Entretien.")).toBeDefined();
  fireEvent.click(within(screen.getByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Refusée" }));

  expect(await screen.findByText("Statut changé : Refusée.")).toBeDefined();
  expect(screen.queryByText("Étape ajoutée à l'historique : Entretien.")).toBeNull();
  expect(screen.getAllByRole("status")).toHaveLength(1);
});

test("une réponse plus ancienne, arrivée après une plus récente, ne remplace pas la fiche montrée", async () => {
  let repondreALaNote: () => void = () => {};
  const ancienne = ficheFictive({ historique: [etapeFictive({ id: 2 })] });
  const recente = ficheFictive({
    statut: "Refusee",
    passagesPermis: [],
    historique: [etapeFictive({ id: 2 }), changementFictif({ id: 3, nouveau: "Refusee" })],
  });
  afficher(
    clientSimule({
      noterUneEtape: () => new Promise((resoudre) => (repondreALaNote = () => resoudre({ notee: true, fiche: ancienne }))),
      changerLeStatut: async () => ({ faite: true, fiche: recente }),
    }),
  );

  fireEvent.click(await screen.findByRole("button", { name: "Ajouter à l'historique" }));
  fireEvent.click(within(screen.getByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Refusée" }));
  expect(await screen.findByText("Statut changé : Refusée.")).toBeDefined();
  repondreALaNote();

  await waitFor(() => expect(screen.getByRole("button", { name: "Ajouter à l'historique" })).toBeDefined());
  await new Promise((resolve) => setTimeout(resolve, 20));
  expect(resume().Statut).toBe("Refusée");
  expect(screen.getByText("La candidature est close : son statut ne change plus.")).toBeDefined();
});

test("si la fiche ne peut être relue après un refus, la fiche le dit avec le motif", async () => {
  const lireLaFiche = vi
    .fn<ReturnType<typeof clientSimule>["lireLaFiche"]>()
    .mockResolvedValueOnce(ficheFictive())
    .mockRejectedValueOnce(new ErreurDeLApi(500));
  afficher(clientSimule({ lireLaFiche, changerLeStatut: async () => ({ faite: false, motif: "La candidature est déjà au statut Refusée." }) }));

  fireEvent.click(within(await screen.findByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Refusée" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "La candidature est déjà au statut Refusée. La fiche n'a pas pu être relue : fermez Sollicito, puis relancez-le.",
  );
});

test("« Modifier la candidature » mène à l'écran de modification", async () => {
  const { modifier } = afficher();

  fireEvent.click(await screen.findByRole("button", { name: "Modifier la candidature" }));

  expect(modifier).toHaveBeenCalledOnce();
});

test("« Supprimer la candidature » demande une confirmation dans la fiche, sans boîte du navigateur ; « Annuler » ne supprime rien", async () => {
  const boiteDuNavigateur = vi.spyOn(window, "confirm").mockReturnValue(true);
  const { client, quandSupprimee } = afficher();
  const supprimer = await screen.findByRole("button", { name: "Supprimer la candidature" });
  expect(confirmation()).toBeNull();

  fireEvent.click(supprimer);

  const question = confirmation()!;
  expect(within(question).getByText("La suppression est définitive : elle emporte tout l'historique de la candidature.")).toBeDefined();
  expect(supprimer.getAttribute("aria-expanded")).toBe("true");
  // La question reçoit le focus : on lit ce qu'on s'apprête à faire avant de choisir.
  await waitFor(() => expect(document.activeElement).toBe(within(question).getByRole("heading", { level: 3, name: "Supprimer cette candidature ?" })));

  fireEvent.click(within(question).getByRole("button", { name: "Annuler" }));

  expect(confirmation()).toBeNull();
  await waitFor(() => expect(document.activeElement).toBe(supprimer));
  expect(client.supprimerUneCandidature).not.toHaveBeenCalled();
  expect(quandSupprimee).not.toHaveBeenCalled();
  expect(boiteDuNavigateur).not.toHaveBeenCalled();
  boiteDuNavigateur.mockRestore();
});

test("Échap ferme la confirmation sans rien supprimer", async () => {
  const { client } = afficher();

  fireEvent.click(await screen.findByRole("button", { name: "Supprimer la candidature" }));
  fireEvent.keyDown(within(confirmation()!).getByRole("button", { name: "Supprimer définitivement" }), { key: "Escape" });

  expect(confirmation()).toBeNull();
  expect(client.supprimerUneCandidature).not.toHaveBeenCalled();
});

test("confirmée, la suppression part vers l'API, puis la fiche passe la main avec la candidature supprimée", async () => {
  const fiche = ficheFictive({ id: 7, poste: "Brasseuse", entreprise: { id: 4, nom: "Brasserie Lune Rousse" } });
  const { client, quandSupprimee } = afficher(clientSimule({ lireLaFiche: async () => fiche }));

  fireEvent.click(await screen.findByRole("button", { name: "Supprimer la candidature" }));
  fireEvent.click(within(confirmation()!).getByRole("button", { name: "Supprimer définitivement" }));

  await waitFor(() => expect(quandSupprimee).toHaveBeenCalledExactlyOnceWith(fiche));
  expect(client.supprimerUneCandidature).toHaveBeenCalledExactlyOnceWith(7);
});

test("pendant la suppression, les boutons attendent la réponse ; un échec se signale, et la confirmation reste ouverte", async () => {
  let repondre: (erreur: Error) => void = () => {};
  const supprimerUneCandidature = vi.fn(() => new Promise<void>((_, rejeter) => (repondre = rejeter)));
  const { quandSupprimee } = afficher(clientSimule({ supprimerUneCandidature }));

  fireEvent.click(await screen.findByRole("button", { name: "Supprimer la candidature" }));
  fireEvent.click(within(confirmation()!).getByRole("button", { name: "Supprimer définitivement" }));

  const enCours = await within(confirmation()!).findByRole("button", { name: "Suppression…" });
  expect(enCours.hasAttribute("disabled")).toBe(true);
  expect(screen.getByRole("button", { name: "Modifier la candidature" }).hasAttribute("disabled")).toBe(true);
  fireEvent.click(enCours);
  fireEvent.keyDown(enCours, { key: "Escape" });
  expect(supprimerUneCandidature).toHaveBeenCalledOnce();
  expect(confirmation()).not.toBeNull();

  repondre(new Error("panne"));
  expect((await screen.findByRole("alert")).textContent).toBe(
    "La candidature n'a pas pu être supprimée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(quandSupprimee).not.toHaveBeenCalled();
  expect(within(confirmation()!).getByRole("button", { name: "Supprimer définitivement" }).hasAttribute("disabled")).toBe(false);
});

test("une candidature déjà supprimée ailleurs le dit, au lieu de la fiche", async () => {
  const { quandSupprimee } = afficher(clientSimule({ supprimerUneCandidature: () => Promise.reject(new ErreurDeLApi(404)) }));

  fireEvent.click(await screen.findByRole("button", { name: "Supprimer la candidature" }));
  fireEvent.click(within(confirmation()!).getByRole("button", { name: "Supprimer définitivement" }));

  expect((await screen.findByRole("alert")).textContent).toBe("Cette candidature n'existe pas, ou plus.");
  expect(screen.queryByRole("button", { name: "Modifier la candidature" })).toBeNull();
  expect(quandSupprimee).not.toHaveBeenCalled();
});

test("l'annonce d'une modification s'affiche sur la fiche, jusqu'à la prochaine action", async () => {
  afficher(clientSimule(), vi.fn(), "Candidature modifiée : Comptable, chez Lumière Verte SA.");

  expect((await screen.findByRole("status")).textContent).toBe("Candidature modifiée : Comptable, chez Lumière Verte SA.");
  fireEvent.click(within(screen.getByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Entretien" }));

  expect(await screen.findByText("Statut changé : Entretien.")).toBeDefined();
  expect(screen.queryByText("Candidature modifiée : Comptable, chez Lumière Verte SA.")).toBeNull();
  expect(screen.getAllByRole("status")).toHaveLength(1);
});

test("une autre action ferme la confirmation de la suppression, qui portait sur la fiche d'avant", async () => {
  const { client } = afficher();

  fireEvent.click(await screen.findByRole("button", { name: "Supprimer la candidature" }));
  expect(confirmation()).not.toBeNull();
  fireEvent.click(within(screen.getByRole("group", { name: "Passer au statut :" })).getByRole("button", { name: "Entretien" }));

  expect(await screen.findByText("Statut changé : Entretien.")).toBeDefined();
  expect(confirmation()).toBeNull();
  expect(client.supprimerUneCandidature).not.toHaveBeenCalled();
});
