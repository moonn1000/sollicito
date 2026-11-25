import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { EtatDeLIA, ResultatDeLEssai, ResultatDeLIA } from "../api/clientDeLApi.ts";
import { clientSimule, etatDeLIAFictif } from "../tests/clientSimule.ts";
import { SectionDeLIA } from "./SectionDeLIA.tsx";

// Une clé fictive, reconnaissable.
const CLE = "sk-ant-api03-SOLLICITO-ESSAI-43-cle-fictive";

const claude = (modifications: Partial<EtatDeLIA> = {}) => etatDeLIAFictif({ fournisseur: "Claude", cle: "Aucune", ...modifications });

test("la section propose les quatre choix, aucune IA par défaut, et enregistre le choix", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ choisi: false }) });
  render(<SectionDeLIA client={client} />);

  expect(await screen.findByText("Facultative : Sollicito fonctionne aussi sans IA.")).toBeDefined();
  const groupe = screen.getByRole("group", { name: "Fournisseur" });
  expect(within(groupe).getAllByRole("radio").map((choix) => choix.parentElement?.textContent)).toEqual([
    "Aucune IA",
    "Claude, d'Anthropic, avec votre clé",
    "OpenAI, avec votre clé",
    "Un modèle local, avec Ollama",
  ]);
  expect((screen.getByLabelText("Aucune IA") as HTMLInputElement).checked).toBe(true);

  fireEvent.click(screen.getByLabelText("Claude, d'Anthropic, avec votre clé"));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer ce choix" }));

  expect((await screen.findByText("Choix enregistré.")).getAttribute("role")).toBe("status");
  expect(client.choisirLeFournisseurDIA).toHaveBeenCalledWith("Claude");
  expect(screen.getByText("Aucune clé n'est enregistrée : Sollicito continue sans IA.")).toBeDefined();
  expect(screen.getByLabelText("Clé d'API de Claude")).toBeDefined();
});

test("la clé s'écrit une fois dans un champ masqué qui se vide, et la page ne la relit jamais", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude() });
  render(<SectionDeLIA client={client} />);

  const champ = (await screen.findByLabelText("Clé d'API de Claude")) as HTMLInputElement;
  expect(champ.type).toBe("password");
  expect(champ.autocomplete).toBe("off");
  expect(champ.getAttribute("spellcheck")).toBe("false");
  expect(screen.getByText("Sollicito la chiffre sur ce poste, pour votre seul compte Windows, et ne l'affiche plus ensuite.")).toBeDefined();
  fireEvent.change(champ, { target: { value: CLE } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la clé" }));

  expect(await screen.findByText("Une clé est enregistrée.")).toBeDefined();
  expect(client.enregistrerLaCleDIA).toHaveBeenCalledWith(CLE);
  expect(screen.queryByLabelText("Clé d'API de Claude")).toBeNull();
  expect(screen.getByRole("button", { name: "Remplacer la clé" })).toBeDefined();
  expect(screen.getByRole("button", { name: "Retirer la clé" })).toBeDefined();
  expect(document.body.innerHTML).not.toContain("SOLLICITO-ESSAI-43");
});

test("une clé refusée se dit sous le champ, qui se vide aussi", async () => {
  const client = clientSimule({
    lireLEtatDeLIA: async () => claude(),
    enregistrerLaCleDIA: vi.fn(async () => ({ issue: "refusee" as const, refus: ["Une clé de Claude commence par sk-ant-."] })),
  });
  render(<SectionDeLIA client={client} />);

  const champ = (await screen.findByLabelText("Clé d'API de Claude")) as HTMLInputElement;
  fireEvent.change(champ, { target: { value: "sk-proj-SOLLICITO-ESSAI-43-cle-fictive" } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la clé" }));

  expect(await screen.findByText("Une clé de Claude commence par sk-ant-.")).toBeDefined();
  expect(champ.value).toBe("");
  expect(champ.getAttribute("aria-invalid")).toBe("true");
  expect(document.body.innerHTML).not.toContain("SOLLICITO-ESSAI-43");
});

test("une clé tapée mais non envoyée ne passe pas dans le champ d'un autre fournisseur", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude() });
  render(<SectionDeLIA client={client} />);

  const champDeClaude = (await screen.findByLabelText("Clé d'API de Claude")) as HTMLInputElement;
  fireEvent.change(champDeClaude, { target: { value: CLE } });
  expect(champDeClaude.value).toBe(CLE);

  // Choisir un autre fournisseur vide le champ, avant même d'enregistrer le choix.
  fireEvent.click(screen.getByLabelText("OpenAI, avec votre clé"));
  expect(champDeClaude.value).toBe("");

  // Une clé retapée, puis le choix enregistré : le champ d'OpenAI est vide, et rien n'a été envoyé.
  fireEvent.change(champDeClaude, { target: { value: CLE } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer ce choix" }));

  const champDOpenAI = (await screen.findByLabelText("Clé d'API d'OpenAI")) as HTMLInputElement;
  expect(client.choisirLeFournisseurDIA).toHaveBeenCalledWith("OpenAI");
  expect(champDOpenAI.value).toBe("");
  expect(client.enregistrerLaCleDIA).not.toHaveBeenCalled();
  expect(document.body.innerHTML).not.toContain("SOLLICITO-ESSAI-43");
});

test("remplacer la clé rouvre le champ, et Annuler le referme sans rien envoyer", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude({ cle: "Enregistree" }) });
  render(<SectionDeLIA client={client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Remplacer la clé" }));
  const champ = screen.getByLabelText("Clé d'API de Claude");
  expect(document.activeElement).toBe(champ);
  fireEvent.click(screen.getByRole("button", { name: "Annuler" }));

  expect(screen.queryByLabelText("Clé d'API de Claude")).toBeNull();
  expect(screen.getByText("Une clé est enregistrée.")).toBeDefined();
  expect(client.enregistrerLaCleDIA).not.toHaveBeenCalled();
});

test("retirer la clé attend sa confirmation dans la page, Annuler en premier", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude({ cle: "Enregistree" }) });
  render(<SectionDeLIA client={client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Retirer la clé" }));
  const confirmation = screen.getByRole("group", { name: "Retirer la clé l'efface de ce poste." });
  expect(document.activeElement).toBe(within(confirmation).getByRole("button", { name: "Annuler" }));
  fireEvent.keyDown(confirmation, { key: "Escape" });
  expect(screen.queryByRole("group", { name: "Retirer la clé l'efface de ce poste." })).toBeNull();
  expect(client.retirerLaCleDIA).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Retirer la clé" }));
  fireEvent.click(within(screen.getByRole("group", { name: "Retirer la clé l'efface de ce poste." })).getByRole("button", { name: "Retirer la clé" }));

  expect(await screen.findByText("Aucune clé n'est enregistrée : Sollicito continue sans IA.")).toBeDefined();
  expect(client.retirerLaCleDIA).toHaveBeenCalledOnce();
});

test("changer de fournisseur quand une clé est enregistrée attend sa confirmation, qui dit que la clé s'efface", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude({ cle: "Enregistree" }) });
  render(<SectionDeLIA client={client} />);

  fireEvent.click(await screen.findByLabelText("OpenAI, avec votre clé"));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer ce choix" }));

  const confirmation = screen.getByRole("group", { name: "Changer de fournisseur efface la clé enregistrée." });
  expect(document.activeElement).toBe(within(confirmation).getByRole("button", { name: "Annuler" }));
  expect(client.choisirLeFournisseurDIA).not.toHaveBeenCalled();
  fireEvent.click(within(confirmation).getByRole("button", { name: "Annuler" }));
  expect(client.choisirLeFournisseurDIA).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Enregistrer ce choix" }));
  fireEvent.click(screen.getByRole("button", { name: "Changer et effacer la clé" }));

  expect(await screen.findByText("Choix enregistré.")).toBeDefined();
  expect(client.choisirLeFournisseurDIA).toHaveBeenCalledWith("OpenAI");
  expect(screen.getByLabelText("Clé d'API d'OpenAI")).toBeDefined();
});

test("sans clé enregistrée, changer de fournisseur ne demande rien", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude({ cle: "Illisible" }) });
  render(<SectionDeLIA client={client} />);

  fireEvent.click(await screen.findByLabelText("Un modèle local, avec Ollama"));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer ce choix" }));

  expect(await screen.findByText("Le modèle tourne sur votre ordinateur, sans clé.")).toBeDefined();
  expect(client.choisirLeFournisseurDIA).toHaveBeenCalledWith("Ollama");
});

test.each([
  [
    claude({ cle: "Illisible" }),
    "La clé enregistrée ne peut pas être relue sur ce poste, par exemple après le passage à un autre ordinateur ou à un autre compte Windows. Sollicito continue sans IA : saisissez-la de nouveau.",
  ],
  [
    etatDeLIAFictif({ reglageLisible: false }),
    "Le choix de l'IA n'a pas pu être relu : Sollicito continue sans IA. Choisissez de nouveau.",
  ],
  [
    etatDeLIAFictif({ reglageLisible: false, fichierLisible: false }),
    "Les réglages n'ont pas pu être relus : Sollicito continue sans IA. Choisissez de nouveau le dossier des sauvegardes, qui les réécrit, puis l'IA.",
  ],
])("ce qui ne se relit pas se dit, et Sollicito continue sans IA (%#)", async (etat, texte) => {
  render(<SectionDeLIA client={clientSimule({ lireLEtatDeLIA: async () => etat })} />);

  expect((await screen.findByRole("alert")).textContent).toBe(texte);
});

test("un refus de l'API se dit, sans rien changer à l'écran", async () => {
  const motif = "Le fournisseur n'a pas changé : la clé enregistrée n'a pas pu être effacée. Réessayez.";
  const client = clientSimule({
    lireLEtatDeLIA: async () => claude(),
    choisirLeFournisseurDIA: vi.fn(async () => ({ issue: "refuse" as const, motif })),
  });
  render(<SectionDeLIA client={client} />);

  fireEvent.click(await screen.findByLabelText("OpenAI, avec votre clé"));
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer ce choix" }));

  expect((await screen.findByRole("alert")).textContent).toBe(motif);
  expect(screen.getByLabelText("Clé d'API de Claude")).toBeDefined();
});

test("une écriture manquée le dit", async () => {
  const client = clientSimule({
    lireLEtatDeLIA: async () => claude(),
    enregistrerLaCleDIA: vi.fn(async () => {
      throw new Error("Panne simulée");
    }),
  });
  render(<SectionDeLIA client={client} />);

  fireEvent.change(await screen.findByLabelText("Clé d'API de Claude"), { target: { value: CLE } });
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la clé" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "La clé n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect((screen.getByLabelText("Clé d'API de Claude") as HTMLInputElement).value).toBe("");
});

test("une lecture manquée le dit, et Réessayer relit l'état", async () => {
  let essais = 0;
  const client = clientSimule({
    lireLEtatDeLIA: vi.fn(async () => {
      essais += 1;
      if (essais === 1) {
        throw new Error("Panne simulée");
      }

      return etatDeLIAFictif();
    }),
  });
  render(<SectionDeLIA client={client} />);

  expect((await screen.findByRole("alert")).textContent).toBe("L'état de l'IA n'a pas pu être lu. Réessayez.");
  fireEvent.click(screen.getByRole("button", { name: "Réessayer" }));

  expect(await screen.findByText("Facultative : Sollicito fonctionne aussi sans IA.")).toBeDefined();
});

test("deux clics dans le même rendu n'envoient qu'une fois", async () => {
  let rendre: () => void = () => {};
  const client = clientSimule({
    lireLEtatDeLIA: async () => etatDeLIAFictif(),
    choisirLeFournisseurDIA: vi.fn(
      () =>
        new Promise<ResultatDeLIA>((resolution) => {
          rendre = () => resolution({ issue: "fait" as const, etat: etatDeLIAFictif() });
        }),
    ),
  });
  render(<SectionDeLIA client={client} />);

  const bouton = await screen.findByRole("button", { name: "Enregistrer ce choix" });
  fireEvent.click(bouton);
  fireEvent.click(bouton);
  await act(async () => rendre());

  expect(client.choisirLeFournisseurDIA).toHaveBeenCalledOnce();
});

test("venue de la carte du tableau de bord, la section prend le focus", async () => {
  render(<SectionDeLIA client={clientSimule()} prendreLeFocus />);

  expect(document.activeElement).toBe(await screen.findByRole("heading", { name: "IA" }));
});

test("tester la clé ne part que sur le clic, et dit le résultat sans la réponse du fournisseur", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude({ cle: "Enregistree" }) });
  render(<SectionDeLIA client={client} />);

  const bouton = await screen.findByRole("button", { name: "Tester la clé" });
  expect(client.testerLaCleDIA).not.toHaveBeenCalled();
  expect(bouton.getAttribute("aria-describedby")).toBe("aide-de-l-essai");
  expect(document.getElementById("aide-de-l-essai")?.textContent).toBe(
    "L'essai envoie à Claude une courte question fixe, sans aucune de vos données. Comme tout appel, il est décompté sur votre compte chez Anthropic.",
  );

  fireEvent.click(bouton);

  expect((await screen.findByText("Claude a répondu : la clé fonctionne.")).getAttribute("role")).toBe("status");
  expect(client.testerLaCleDIA).toHaveBeenCalledOnce();
});

test("l'aide de l'essai d'OpenAI nomme OpenAI", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ fournisseur: "OpenAI", cle: "Enregistree" }) });
  render(<SectionDeLIA client={client} />);

  expect(await screen.findByText(
    "L'essai envoie à OpenAI une courte question fixe, sans aucune de vos données. Comme tout appel, il est décompté sur votre compte chez OpenAI.",
  )).toBeDefined();
});

test("un essai qui échoue se dit en alerte, et un refus relit l'état", async () => {
  const client = clientSimule({
    lireLEtatDeLIA: vi.fn(async () => claude({ cle: "Enregistree" })),
    testerLaCleDIA: vi
      .fn<() => Promise<ResultatDeLEssai>>()
      .mockResolvedValueOnce({ issue: "fait", reussi: false, message: "Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA." })
      .mockResolvedValueOnce({ issue: "refuse", motif: "Une demande à l'IA est déjà en cours : attendez sa réponse." }),
  });
  render(<SectionDeLIA client={client} />);

  fireEvent.click(await screen.findByRole("button", { name: "Tester la clé" }));
  expect((await screen.findByText("Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA.")).getAttribute("role")).toBe(
    "alert",
  );

  const lectures = vi.mocked(client.lireLEtatDeLIA).mock.calls.length;
  fireEvent.click(screen.getByRole("button", { name: "Tester la clé" }));
  expect((await screen.findByText("Une demande à l'IA est déjà en cours : attendez sa réponse.")).getAttribute("role")).toBe("alert");
  expect(vi.mocked(client.lireLEtatDeLIA).mock.calls.length).toBeGreaterThan(lectures);
});

test("un essai qui ne part pas se dit, et pendant l'essai les boutons attendent", async () => {
  let rendre: (resultat: ResultatDeLEssai) => void = () => {};
  const client = clientSimule({
    lireLEtatDeLIA: async () => claude({ cle: "Enregistree" }),
    testerLaCleDIA: vi
      .fn<() => Promise<ResultatDeLEssai>>()
      .mockImplementationOnce(
        () =>
          new Promise<ResultatDeLEssai>((resolution) => {
            rendre = resolution;
          }),
      )
      .mockRejectedValueOnce(new Error("hors ligne")),
  });
  render(<SectionDeLIA client={client} />);

  const bouton = await screen.findByRole("button", { name: "Tester la clé" });
  fireEvent.click(bouton);
  fireEvent.click(bouton);
  expect(await screen.findByRole("button", { name: "Essai en cours…" })).toBeDefined();
  expect((screen.getByRole("button", { name: "Remplacer la clé" }) as HTMLButtonElement).disabled).toBe(true);
  expect((screen.getByRole("button", { name: "Retirer la clé" }) as HTMLButtonElement).disabled).toBe(true);
  await act(async () => rendre({ issue: "fait", reussi: true, message: "Claude a répondu : la clé fonctionne." }));
  expect(client.testerLaCleDIA).toHaveBeenCalledOnce();

  fireEvent.click(await screen.findByRole("button", { name: "Tester la clé" }));
  expect(
    await screen.findByText("L'essai n'a pas pu être lancé. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le."),
  ).toBeDefined();
});

test("sans clé enregistrée, aucun bouton d'essai", async () => {
  const client = clientSimule({ lireLEtatDeLIA: async () => claude() });
  render(<SectionDeLIA client={client} />);

  await screen.findByLabelText("Clé d'API de Claude");
  expect(screen.queryByRole("button", { name: "Tester la clé" })).toBeNull();
});
