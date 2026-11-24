import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi, type EtatDesSauvegardes } from "../api/clientDeLApi.ts";
import type { IssueDeLaRestauration, IssueDuChoixDuDossier } from "../hote/canalDeLHote.ts";
import { clientSimule, copieFictive, DOSSIER_PAR_DEFAUT_FICTIF, sauvegardesFictives } from "../tests/clientSimule.ts";
import { SectionDesSauvegardes } from "./SectionDesSauvegardes.tsx";

const CLE_FICTIVE = "E:\\Sauvegardes Sollicito";

test("le dossier par défaut est montré, avec ses avertissements : copies non chiffrées, même disque que la base", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={vi.fn()} />);

  const section = await screen.findByRole("region", { name: "Sauvegardes" });
  expect(await within(section).findByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
  expect(within(section).getByText("Les copies ne sont pas chiffrées : toute personne qui accède à leur dossier peut les lire.")).toBeDefined();
  expect(within(section).getByText(/C'est le dossier par défaut, sur le même disque que votre base : il ne vous protège pas d'une panne de ce disque\./)).toBeDefined();
  expect(within(section).getByText("18/11/2026 14:30", { selector: "dd time" })).toBeDefined();
  // Sans l'hôte, aucune restauration (#28) ; et aucun retour au dossier par défaut quand il sert déjà.
  expect(within(section).queryByRole("button", { name: /restaurer/i })).toBeNull();
  expect(within(section).queryByRole("button", { name: "Revenir au dossier par défaut" })).toBeNull();
  expect(within(section).getAllByRole("button").map((bouton) => bouton.textContent)).toEqual(["Choisir un autre dossier…", "Sauvegarder maintenant"]);
});

test("la section ne présente jamais une copie comme le moyen de rendre une candidature supprimée", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={vi.fn()} />);

  const section = await screen.findByRole("region", { name: "Sauvegardes" });
  await within(section).findByText(DOSSIER_PAR_DEFAUT_FICTIF);
  expect(section.textContent).not.toMatch(/supprim|candidature/i);
});

test("les copies gardées sont listées, celles d'avant une mise à jour marquées comme telles", async () => {
  const client = clientSimule({
    lireLesSauvegardes: async () =>
      sauvegardesFictives({
        copies: [
          copieFictive(),
          copieFictive({ nom: "sollicito-1a2b3c4d-2026-11-17.db", faiteLe: "2026-11-17T17:00:00Z", id: "1123456789abcdef" }),
          copieFictive({ nom: "sollicito-avant-20261120164027_CorrectionsDesEtapes.db", sorte: "AvantMiseAJour", faiteLe: "2026-11-16T08:00:00Z", id: "2123456789abcdef" }),
        ],
      }),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);

  const tableau = await screen.findByRole("table");
  const lignes = within(tableau).getAllByRole("row").slice(1);
  expect(lignes.map((ligne) => within(ligne).getAllByRole("cell").map((cellule) => cellule.textContent))).toEqual([
    ["18/11/2026 14:30", "Copie du jour", "sollicito-1a2b3c4d-2026-11-18.db"],
    ["17/11/2026 18:00", "Copie du jour", "sollicito-1a2b3c4d-2026-11-17.db"],
    ["16/11/2026 09:00", "Avant une mise à jour", "sollicito-avant-20261120164027_CorrectionsDesEtapes.db"],
  ]);
});

test("un dossier choisi par l'hôte s'annonce, et la section relit l'état : les copies de l'ancien dossier y restent", async () => {
  let etat = sauvegardesFictives();
  const client = clientSimule({ lireLesSauvegardes: vi.fn(async () => etat) });
  const choisirLeDossier = vi.fn(async (): Promise<IssueDuChoixDuDossier> => {
    etat = sauvegardesFictives({ dossier: CLE_FICTIVE, parDefaut: false });
    return "choisi";
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={choisirLeDossier} />);

  fireEvent.click(await screen.findByRole("button", { name: "Choisir un autre dossier…" }));

  expect(await screen.findByText("Dossier des sauvegardes changé : une copie vient d'y être faite. Les copies de l'ancien dossier y restent.")).toBeDefined();
  expect(screen.getByText(CLE_FICTIVE)).toBeDefined();
  // L'avertissement est toujours là, quel que soit le dossier : pas seulement avec celui par défaut (#88, point 13).
  expect(screen.getByText("Les copies ne sont pas chiffrées : toute personne qui accède à leur dossier peut les lire.")).toBeDefined();
  expect(choisirLeDossier).toHaveBeenCalledOnce();
  // Hors du dossier par défaut : on peut y revenir, et l'écran dit où restent les copies d'avant une mise à jour.
  expect(screen.getByRole("button", { name: "Revenir au dossier par défaut" })).toBeDefined();
  expect(screen.getByText(/Les copies d'avant une mise à jour de Sollicito restent dans le dossier par défaut/)).toBeDefined();
});

test("un choix annulé ou refusé ne change pas de dossier, et l'écran le dit", async () => {
  const issues: IssueDuChoixDuDossier[] = ["annule", "refuse"];
  const choisirLeDossier = vi.fn(async () => issues.shift()!);
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={choisirLeDossier} />);

  fireEvent.click(await screen.findByRole("button", { name: "Choisir un autre dossier…" }));
  expect(await screen.findByText("Le dossier des sauvegardes n'a pas changé.")).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Choisir un autre dossier…" }));
  expect(
    await screen.findByText("Sollicito n'a pas pu faire de copie dans ce dossier : le dossier des sauvegardes n'a pas changé."),
  ).toBeDefined();
  expect(screen.getByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
});

test("hors de la fenêtre, aucun bouton ne propose de choisir un dossier", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} />);

  await screen.findByText(DOSSIER_PAR_DEFAUT_FICTIF);
  expect(screen.queryByRole("button", { name: "Choisir un autre dossier…" })).toBeNull();
});

test("le retour au dossier par défaut s'annonce ; refusé, son motif s'affiche et l'ancien dossier reste", async () => {
  const ailleurs = sauvegardesFictives({ dossier: CLE_FICTIVE, parDefaut: false });
  const client = clientSimule({
    lireLesSauvegardes: vi.fn(async () => ailleurs),
    revenirAuDossierParDefaut: vi
      .fn()
      .mockResolvedValueOnce({ issue: "refuse", motif: "Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie." })
      .mockResolvedValueOnce({ issue: "fait", etat: sauvegardesFictives() }),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: "Revenir au dossier par défaut" }));
  expect(await screen.findByText("Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie.")).toBeDefined();
  expect(screen.getByText(CLE_FICTIVE)).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Revenir au dossier par défaut" }));
  expect(
    await screen.findByText("Retour au dossier par défaut : une copie vient d'y être faite. Les copies de l'ancien dossier y restent."),
  ).toBeDefined();
  expect(screen.getByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
});

test("« Sauvegarder maintenant » refait la copie du jour ; manquée, l'alerte dit depuis quand et quoi faire", async () => {
  const client = clientSimule({
    sauvegarderMaintenant: vi
      .fn()
      .mockResolvedValueOnce(sauvegardesFictives({ derniereCopie: "2026-11-18T15:00:00Z" }))
      .mockResolvedValueOnce(sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-18T16:00:00Z" } })),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);

  fireEvent.click(await screen.findByRole("button", { name: "Sauvegarder maintenant" }));
  expect(await screen.findByText("Copie du jour faite.")).toBeDefined();
  expect(screen.getByText("18/11/2026 16:00", { selector: "dd time" })).toBeDefined();

  fireEvent.click(screen.getByRole("button", { name: "Sauvegarder maintenant" }));
  expect(
    await screen.findByText(
      "Aucune copie de votre base n'a réussi depuis le 18/11/2026 14:30 : le dossier des sauvegardes est introuvable. Rebranchez son support, ou choisissez un autre dossier.",
    ),
  ).toBeDefined();
  expect(screen.queryByText("Copie du jour faite.")).toBeNull();
});

test("un réglage illisible le dit, et invite à choisir de nouveau son dossier", async () => {
  render(<SectionDesSauvegardes client={clientSimule({ lireLesSauvegardes: async () => sauvegardesFictives({ reglageIllisible: true }) })} />);

  expect(
    await screen.findByText(
      "Le réglage du dossier des sauvegardes n'a pas pu être relu : les copies vont dans le dossier par défaut. Choisissez de nouveau votre dossier.",
    ),
  ).toBeDefined();
});

test("deux clics rapides ne lancent qu'une copie", async () => {
  let finir: () => void = () => {};
  const client = clientSimule({
    sauvegarderMaintenant: vi.fn(
      () =>
        new Promise<EtatDesSauvegardes>((resoudre) => {
          finir = () => resoudre(sauvegardesFictives());
        }),
    ),
  });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} />);
  const bouton = await screen.findByRole("button", { name: "Sauvegarder maintenant" });

  act(() => {
    bouton.click();
    bouton.click();
  });
  await act(async () => finir());

  await waitFor(() => expect(screen.getByText("Copie du jour faite.")).toBeDefined());
  expect(client.sauvegarderMaintenant).toHaveBeenCalledOnce();
});

const COPIES_DE_TOUTES_SORTES = [
  copieFictive({ nom: "sollicito-avant-20261120164027_CorrectionsDesEtapes.db", sorte: "AvantMiseAJour", faiteLe: "2026-11-16T08:00:00Z", id: "2123456789abcdef" }),
  copieFictive(),
  copieFictive({ nom: "sollicito-9f8e7d6c-2026-11-19.db", sorte: "DuJourDUneAutreInstallation", faiteLe: "2026-11-19T07:05:00Z", id: "3123456789abcdef" }),
  copieFictive({ nom: "sollicito-avant-restauration-20261120-101500000.db", sorte: "AvantRestauration", faiteLe: "2026-11-20T10:15:00Z", id: "4123456789abcdef" }),
];

test("dans la fenêtre, toutes les copies se listent des plus récentes aux plus anciennes, et chacune se restaure", async () => {
  const client = clientSimule({ lireLesSauvegardes: async () => sauvegardesFictives({ copies: COPIES_DE_TOUTES_SORTES }) });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} restaurer={vi.fn()} />);

  const tableau = await screen.findByRole("table", { name: "Les copies gardées, des plus récentes aux plus anciennes." });
  const lignes = within(tableau).getAllByRole("row").slice(1);
  expect(lignes.map((ligne) => within(ligne).getAllByRole("cell").slice(0, 2).map((cellule) => cellule.textContent))).toEqual([
    ["20/11/2026 11:15", "Avant une restauration ou un import"],
    ["19/11/2026 08:05", "Copie du jour d'une autre installation"],
    ["18/11/2026 14:30", "Copie du jour"],
    ["16/11/2026 09:00", "Avant une mise à jour"],
  ]);
  expect(within(tableau).getAllByRole("button").map((bouton) => bouton.getAttribute("aria-label"))).toEqual([
    "Restaurer la copie du 20 novembre 2026 à 11:15",
    "Restaurer la copie du 19 novembre 2026 à 08:05",
    "Restaurer la copie du 18 novembre 2026 à 14:30",
    "Restaurer la copie du 16 novembre 2026 à 09:00",
  ]);
});

test("restaurer n'envoie que l'identifiant de la copie, dit qu'il attend le oui de l'hôte, puis que la restauration est en cours", async () => {
  let enCours: () => void = () => {};
  let finir: (issue: IssueDeLaRestauration) => void = () => {};
  const restaurer = vi.fn(
    (_copie: string, quandEnCours: () => void) =>
      new Promise<IssueDeLaRestauration>((resoudre) => {
        enCours = quandEnCours;
        finir = resoudre;
      }),
  );
  const quandRestauree = vi.fn();
  const client = clientSimule({ lireLesSauvegardes: async () => sauvegardesFictives({ copies: COPIES_DE_TOUTES_SORTES }) });
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} restaurer={restaurer} quandRestauree={quandRestauree} />);

  fireEvent.click(await screen.findByRole("button", { name: "Restaurer la copie du 19 novembre 2026 à 08:05" }));

  expect(restaurer).toHaveBeenCalledWith("3123456789abcdef", expect.any(Function));
  const section = screen.getByRole("region", { name: "Sauvegardes" });
  expect(await within(section).findByText("Confirmez la restauration dans la fenêtre de Sollicito.")).toBeDefined();
  expect(section.getAttribute("aria-busy")).toBe("true");
  // Pendant la restauration, aucune autre action ne part.
  expect(within(section).getAllByRole("button").every((bouton) => (bouton as HTMLButtonElement).disabled)).toBe(true);

  act(() => enCours());
  expect(await within(section).findByText("Restauration en cours : ne fermez pas Sollicito.")).toBeDefined();

  await act(async () => finir("restauree"));
  expect(quandRestauree).toHaveBeenCalledWith("restauree", COPIES_DE_TOUTES_SORTES[2]);
});

test("une restauration qui n'a pas changé la base le dit, puis la section relit l'état", async () => {
  const issues: IssueDeLaRestauration[] = [
    "annulee",
    "abimee",
    "etrangere",
    "plusRecente",
    "introuvable",
    "changee",
    "baseChangee",
    "occupee",
    "copieDAvantImpossible",
    "impossible",
  ];
  const textes = [
    "Restauration annulée : votre base n'a pas changé.",
    "Cette copie est abîmée : Sollicito ne l'a pas restaurée. Votre base n'a pas changé.",
    "Ce fichier n'est pas une copie de Sollicito que cette version sait lire : il n'a pas été restauré. Votre base n'a pas changé.",
    "Cette copie vient d'une version plus récente de Sollicito, que celle-ci ne sait pas lire : elle n'a pas été restaurée. Votre base n'a pas changé.",
    "Cette copie est introuvable : son support a peut-être été débranché. Votre base n'a pas changé.",
    "Cette copie a changé depuis l'affichage de la liste : choisissez-la de nouveau. Votre base n'a pas changé.",
    "L'état de votre base a changé depuis votre confirmation : demandez de nouveau la restauration. Votre base n'a pas changé.",
    "Sollicito est occupé : réessayez dans un instant. Votre base n'a pas changé.",
    "Sollicito n'a pas pu copier votre base actuelle : il ne l'a pas remplacée. Vérifiez la place libre sur le disque, puis réessayez.",
    "La restauration n'a pas pu se faire : votre base n'a pas changé. Vérifiez la place libre sur le disque, puis réessayez.",
  ];
  const restaurer = vi.fn(async () => issues.shift()!);
  const quandRestauree = vi.fn();
  const client = clientSimule();
  render(<SectionDesSauvegardes client={client} choisirLeDossier={vi.fn()} restaurer={restaurer} quandRestauree={quandRestauree} />);

  for (const texte of textes) {
    fireEvent.click(await screen.findByRole("button", { name: "Restaurer la copie du 18 novembre 2026 à 14:30" }));
    expect(await screen.findByText(texte)).toBeDefined();
  }

  expect(quandRestauree).not.toHaveBeenCalled();
  // L'état se relit après chaque issue, et une fois au montage.
  expect(client.lireLesSauvegardes).toHaveBeenCalledTimes(textes.length + 1);
});

test("avant la première vérification du lancement, la section le dit et relit l'état jusqu'à ce qu'elle soit faite", async () => {
  const lireLesSauvegardes = vi
    .fn<() => Promise<EtatDesSauvegardes>>()
    .mockResolvedValueOnce(sauvegardesFictives({ verifie: false, derniereCopie: null, copies: [] }))
    .mockResolvedValue(sauvegardesFictives());
  render(<SectionDesSauvegardes client={clientSimule({ lireLesSauvegardes })} choisirLeDossier={vi.fn()} restaurer={vi.fn()} />);

  expect(await screen.findByText("Vérification des copies…")).toBeDefined();
  expect(screen.queryByText("Aucune copie gardée pour l'instant.")).toBeNull();
  expect(screen.queryByText("Aucune copie pour l'instant.")).toBeNull();
  // L'état de départ que l'API rend avant la première vérification porte le dossier par défaut, même quand reglages.json en
  // désigne un autre : la section ne le montre pas, ni son aide, au lieu de faire croire le choix perdu (#88, point 8).
  expect(screen.queryByText("Dossier des copies")).toBeNull();
  expect(screen.queryByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeNull();
  expect(screen.queryByText(/C'est le dossier par défaut/u)).toBeNull();

  expect(await screen.findByText("18/11/2026 14:30", { selector: "dd time" }, { timeout: 3_000 })).toBeDefined();
  expect(screen.getByText("Dossier des copies")).toBeDefined();
  expect(screen.getByText(DOSSIER_PAR_DEFAUT_FICTIF)).toBeDefined();
  expect(screen.getByRole("button", { name: "Restaurer la copie du 18 novembre 2026 à 14:30" })).toBeDefined();
  expect(lireLesSauvegardes).toHaveBeenCalledTimes(2);
});

test("avec la restauration, la section ne présente toujours pas une copie comme le moyen de rendre une candidature supprimée", async () => {
  render(<SectionDesSauvegardes client={clientSimule()} choisirLeDossier={vi.fn()} restaurer={vi.fn()} />);

  const section = await screen.findByRole("region", { name: "Sauvegardes" });
  await within(section).findByRole("button", { name: /Restaurer la copie/ });
  expect(section.textContent).not.toMatch(/supprim|candidature/i);
});

test("si l'état ne se lit pas, ou si une action échoue, l'écran le dit", async () => {
  const { unmount } = render(<SectionDesSauvegardes client={clientSimule({ lireLesSauvegardes: () => Promise.reject(new ErreurDeLApi(500)) })} />);
  expect(await screen.findByText("Les sauvegardes n'ont pas pu être lues. Fermez Sollicito, puis relancez-le.")).toBeDefined();
  unmount();

  render(<SectionDesSauvegardes client={clientSimule({ sauvegarderMaintenant: () => Promise.reject(new ErreurDeLApi(500)) })} />);
  fireEvent.click(await screen.findByRole("button", { name: "Sauvegarder maintenant" }));
  expect(await screen.findByText("L'action n'a pas abouti. Réessayez ; si rien ne change, fermez Sollicito, puis relancez-le.")).toBeDefined();
});
