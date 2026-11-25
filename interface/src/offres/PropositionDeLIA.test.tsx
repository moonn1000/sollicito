import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { ChampsProposes, ClientDeLApi, ResultatDeLExtraction } from "../api/clientDeLApi.ts";
import { clientSimule, etatDeLIAFictif } from "../tests/clientSimule.ts";
import { MARQUE_DE_L_IA, PROPOSITION_IMPOSSIBLE, SANS_IA_PRETE } from "./affichage.ts";
import { FormulaireDUneOffre } from "./FormulaireDUneOffre.tsx";
import { SaisieDUneOffre } from "./SaisieDUneOffre.tsx";

const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script><b>gras</b>";

const TEXTE = "  Lumière Verte SA recrute un comptable à Liège.\nPostulez sur https://emplois.exemple/offres/42  ";

const CHAMPS: ChampsProposes = {
  entreprise: "Lumière Verte SA",
  poste: "Comptable",
  lieu: "Liège",
  publieeLe: "2026-11-12",
  adresse: "https://emplois.exemple/offres/42",
};

const LIBELLES = {
  entreprise: "Entreprise",
  poste: "Poste",
  lieu: "Lieu",
  publieeLe: "Date de publication",
  adresse: "Adresse de l'annonce",
} as const;

const PROPOSER = "Proposer les champs avec l'IA";

function valeur(libelle: string): string {
  return screen.getByLabelText<HTMLInputElement>(libelle).value;
}

function remplir(libelle: string, texte: string) {
  fireEvent.change(screen.getByLabelText(libelle), { target: { value: texte } });
}

// La marque d'un champ : le texte que son aria-describedby désigne, outre l'aide et le refus.
function marque(libelle: string): string | undefined {
  const ids = screen.getByLabelText(libelle).getAttribute("aria-describedby")?.split(" ") ?? [];
  return ids.map((id) => document.getElementById(id)).find((element) => element?.className === "marque")?.textContent ?? undefined;
}

function afficher(modifications: Partial<ClientDeLApi> = {}, fournisseur: "Claude" | "OpenAI" = "Claude") {
  const client = clientSimule({
    lireLEtatDeLIA: vi.fn(async () => etatDeLIAFictif({ fournisseur, cle: "Enregistree" })),
    ...modifications,
  });
  const quandEnregistree = vi.fn();
  const rendu = render(<SaisieDUneOffre client={client} quandEnregistree={quandEnregistree} renoncer={() => {}} />);
  return { client, quandEnregistree, rendu };
}

function proposant(resultat: ResultatDeLExtraction) {
  return { extraireUneOffre: vi.fn<ClientDeLApi["extraireUneOffre"]>(async () => resultat) };
}

test("sans IA prête, l'écran le dit à la place du bouton, et la saisie à la main reste entière", async () => {
  const client = clientSimule();
  render(<SaisieDUneOffre client={client} quandEnregistree={() => {}} renoncer={() => {}} />);

  expect(await screen.findByText(SANS_IA_PRETE)).toBeDefined();
  expect(screen.queryByRole("button", { name: PROPOSER })).toBeNull();
  remplir("Texte de l'offre", TEXTE);
  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  await waitFor(() => expect(client.collerUneOffre).toHaveBeenCalledOnce());
  expect(client.extraireUneOffre).not.toHaveBeenCalled();
});

test.each([
  ["Claude", "Le texte part chez Anthropic, avec votre clé ; rien n'est enregistré sans votre confirmation."],
  ["OpenAI", "Le texte part chez OpenAI, avec votre clé ; rien n'est enregistré sans votre confirmation."],
] as const)("avec %s prête, le bouton dit ce que fait l'IA et où part le texte", async (fournisseur, fin) => {
  afficher({}, fournisseur);

  const bouton = await screen.findByRole("button", { name: PROPOSER });

  const aide = document.getElementById(bouton.getAttribute("aria-describedby")!)!.textContent!;
  expect(aide.startsWith("L'IA lit le texte collé et propose l'entreprise, le poste, le lieu, la date de publication et l'adresse de l'annonce.")).toBe(true);
  expect(aide.endsWith(fin)).toBe(true);
});

test("rien ne part à l'ouverture, au collage ni à la frappe : seul l'état de l'IA se lit, sur le poste", async () => {
  const { client } = afficher();
  await screen.findByRole("button", { name: PROPOSER });

  fireEvent.paste(screen.getByLabelText("Texte de l'offre"));
  remplir("Texte de l'offre", TEXTE);
  remplir("Poste", "Comptable");

  expect(client.extraireUneOffre).not.toHaveBeenCalled();
  expect(client.lireLEtatDeLIA).toHaveBeenCalledOnce();
});

test("sur un clic, le texte part tel quel, les champs vides se remplissent, marqués, et rien ne s'enregistre avant l'envoi", async () => {
  const { client, quandEnregistree } = afficher(proposant({ issue: "proposee", champs: CHAMPS }));
  await screen.findByRole("button", { name: PROPOSER });
  remplir("Texte de l'offre", TEXTE);

  fireEvent.click(screen.getByRole("button", { name: PROPOSER }));

  expect(await screen.findByText("L'IA a proposé 5 champs : relisez-les, et corrigez-les au besoin, avant d'enregistrer l'offre.")).toBeDefined();
  expect(client.extraireUneOffre).toHaveBeenLastCalledWith(TEXTE, expect.any(AbortSignal));
  for (const [champ, libelle] of Object.entries(LIBELLES)) {
    expect(valeur(libelle)).toBe(CHAMPS[champ as keyof ChampsProposes]);
    expect(marque(libelle)).toBe(MARQUE_DE_L_IA);
  }
  expect(client.collerUneOffre).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));

  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledOnce());
  expect(client.collerUneOffre).toHaveBeenCalledWith({ texte: TEXTE, ...CHAMPS }, false);
});

test("un champ déjà saisi n'est jamais remplacé, et l'écran le dit", async () => {
  afficher(proposant({ issue: "proposee", champs: CHAMPS }));
  remplir("Texte de l'offre", TEXTE);
  remplir("Poste", "Mon poste à moi");

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  expect(
    await screen.findByText(
      "L'IA a proposé 4 champs : relisez-les, et corrigez-les au besoin, avant d'enregistrer l'offre. Les champs que vous aviez remplis n'ont pas été changés.",
    ),
  ).toBeDefined();
  expect(valeur("Poste")).toBe("Mon poste à moi");
  expect(marque("Poste")).toBeUndefined();
  expect(valeur("Lieu")).toBe("Liège");
});

test("un champ rempli pendant la demande garde ce que l'utilisateur y a mis", async () => {
  let repondre: (resultat: ResultatDeLExtraction) => void = () => {};
  afficher({
    extraireUneOffre: vi.fn<ClientDeLApi["extraireUneOffre"]>(
      () =>
        new Promise((resolve) => {
          repondre = resolve;
        }),
    ),
  });
  remplir("Texte de l'offre", TEXTE);
  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));
  expect(screen.getByRole<HTMLButtonElement>("button", { name: "L'IA lit l'offre…" }).disabled).toBe(true);

  remplir("Lieu", "Namur");
  repondre({ issue: "proposee", champs: CHAMPS });

  await waitFor(() => expect(valeur("Entreprise")).toBe("Lumière Verte SA"));
  expect(valeur("Lieu")).toBe("Namur");
  expect(marque("Lieu")).toBeUndefined();
});

test("deux clics rapprochés ne font qu'une demande", async () => {
  const extraireUneOffre = vi.fn<ClientDeLApi["extraireUneOffre"]>(() => new Promise(() => {}));
  afficher({ extraireUneOffre });
  remplir("Texte de l'offre", TEXTE);
  const bouton = await screen.findByRole("button", { name: PROPOSER });

  fireEvent.click(bouton);
  fireEvent.click(bouton);

  expect(extraireUneOffre).toHaveBeenCalledOnce();
});

test("modifier un champ proposé retire sa marque ; retirer la proposition ne vide que les champs restés tels quels", async () => {
  const { client } = afficher(proposant({ issue: "proposee", champs: CHAMPS }));
  remplir("Texte de l'offre", TEXTE);
  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));
  await waitFor(() => expect(valeur("Poste")).toBe("Comptable"));

  remplir("Poste", "Comptable senior");
  expect(marque("Poste")).toBeUndefined();
  fireEvent.click(screen.getByRole("button", { name: "Retirer la proposition" }));

  expect(valeur("Poste")).toBe("Comptable senior");
  for (const libelle of ["Entreprise", "Lieu", "Date de publication", "Adresse de l'annonce"]) {
    expect(valeur(libelle)).toBe("");
    expect(marque(libelle)).toBeUndefined();
  }
  expect(screen.queryByRole("button", { name: "Retirer la proposition" })).toBeNull();
  expect(client.collerUneOffre).not.toHaveBeenCalled();
});

test("une proposition sans champ le dit, et une proposition de champs déjà remplis ne change rien", async () => {
  const extraireUneOffre = vi
    .fn<ClientDeLApi["extraireUneOffre"]>()
    .mockResolvedValueOnce({ issue: "proposee", champs: { entreprise: null, poste: null, lieu: null, publieeLe: null, adresse: null } })
    .mockResolvedValueOnce({ issue: "proposee", champs: { ...CHAMPS, lieu: null, publieeLe: null, adresse: null } });
  afficher({ extraireUneOffre });
  remplir("Texte de l'offre", TEXTE);

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));
  expect(await screen.findByText("L'IA n'a trouvé aucun de ces champs dans le texte : remplissez-les vous-même.")).toBeDefined();

  remplir("Entreprise", "Autre SA");
  remplir("Poste", "Autre poste");
  fireEvent.click(screen.getByRole("button", { name: PROPOSER }));
  expect(await screen.findByText("Tous les champs étaient déjà remplis : rien n'a été changé.")).toBeDefined();
  expect(valeur("Entreprise")).toBe("Autre SA");
});

test("un seul champ proposé se dit au singulier", async () => {
  afficher(proposant({ issue: "proposee", champs: { entreprise: null, poste: "Comptable", lieu: null, publieeLe: null, adresse: null } }));
  remplir("Texte de l'offre", TEXTE);

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  expect(await screen.findByText("L'IA a proposé 1 champ : relisez-le, et corrigez-le au besoin, avant d'enregistrer l'offre.")).toBeDefined();
});

test.each([
  [
    "un échec du fournisseur",
    {
      issue: "echec",
      message: "Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA. Vous pouvez remplir les champs vous-même.",
    },
  ],
  [
    "une réponse hors schéma",
    {
      issue: "echec",
      message: "La réponse de Claude ne correspond pas à ce qu'attend Sollicito : elle a été écartée. Réessayez, ou remplissez les champs vous-même.",
    },
  ],
  ["un refus sans requête", { issue: "refuse", motif: "Une demande à l'IA est déjà en cours : attendez sa réponse." }],
] as const)("%s se dit par le texte de l'API, et la saisie à la main reste possible", async (_, resultat) => {
  const { client } = afficher(proposant(resultat));
  remplir("Texte de l'offre", TEXTE);

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  const attendu = resultat.issue === "echec" ? resultat.message : resultat.motif;
  expect((await screen.findByRole("alert")).textContent).toBe(attendu);
  for (const libelle of Object.values(LIBELLES)) {
    expect(valeur(libelle)).toBe("");
  }
  remplir("Poste", "Comptable");
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer l'offre" }));
  await waitFor(() => expect(client.collerUneOffre).toHaveBeenCalledOnce());
});

test("un texte refusé avant toute demande se dit sous le texte, qui reçoit le focus", async () => {
  afficher(proposant({ issue: "texteRefuse", refus: ["Collez le texte de l'offre."] }));

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  const texte = screen.getByLabelText("Texte de l'offre");
  await waitFor(() => expect(document.activeElement).toBe(texte));
  expect(texte.getAttribute("aria-invalid")).toBe("true");
  expect(screen.getByText("Collez le texte de l'offre.")).toBeDefined();
});

test("une demande qui n'aboutit pas le dit, sans rien perdre de la saisie", async () => {
  afficher({
    extraireUneOffre: vi.fn<ClientDeLApi["extraireUneOffre"]>(async () => {
      throw new Error("API muette");
    }),
  });
  remplir("Texte de l'offre", TEXTE);
  remplir("Poste", "Comptable");

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  expect((await screen.findByRole("alert")).textContent).toBe(PROPOSITION_IMPOSSIBLE);
  expect(valeur("Poste")).toBe("Comptable");
});

test("des valeurs piégées se proposent comme du texte : aucune balise ne se crée, et l'adresse n'est pas un lien", async () => {
  afficher(
    proposant({
      issue: "proposee",
      champs: { entreprise: PIEGE, poste: PIEGE, lieu: PIEGE, publieeLe: null, adresse: "https://pirate.exemple/<script>" },
    }),
  );
  remplir("Texte de l'offre", TEXTE);

  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  await waitFor(() => expect(valeur("Poste")).toBe(PIEGE));
  expect(valeur("Entreprise")).toBe(PIEGE);
  expect(valeur("Adresse de l'annonce")).toBe("https://pirate.exemple/<script>");
  expect(document.querySelector("img, script, b, a")).toBeNull();
});

test("quitter l'écran pendant la demande l'interrompt", async () => {
  let signal: AbortSignal | undefined;
  const { rendu } = afficher({
    extraireUneOffre: vi.fn<ClientDeLApi["extraireUneOffre"]>((_, annulation) => {
      signal = annulation;
      return new Promise(() => {});
    }),
  });
  remplir("Texte de l'offre", TEXTE);
  fireEvent.click(await screen.findByRole("button", { name: PROPOSER }));

  rendu.unmount();

  expect(signal?.aborted).toBe(true);
});

test("un état de l'IA illisible montre le bouton, sans dire où part le texte : le clic dira ce qu'il en est", async () => {
  const { client } = afficher({
    lireLEtatDeLIA: vi.fn(async () => {
      throw new Error("API muette");
    }),
    ...proposant({ issue: "refuse", motif: "Aucune IA n'est prête : choisissez-en une et enregistrez sa clé dans les réglages, ou remplissez les champs vous-même." }),
  });

  const bouton = await screen.findByRole("button", { name: PROPOSER });

  expect(bouton.getAttribute("aria-describedby")).toBeNull();
  fireEvent.click(bouton);
  expect((await screen.findByRole("alert")).textContent).toBe(
    "Aucune IA n'est prête : choisissez-en une et enregistrez sa clé dans les réglages, ou remplissez les champs vous-même.",
  );
  expect(client.extraireUneOffre).toHaveBeenCalledOnce();
});

test("la correction d'une offre enregistrée ne propose pas l'IA, et ne lit même pas son état", async () => {
  const client = clientSimule({ lireLEtatDeLIA: vi.fn(async () => etatDeLIAFictif({ fournisseur: "Claude", cle: "Enregistree" })) });
  render(
    <FormulaireDUneOffre
      client={client}
      depart={{ texte: "", entreprise: "", poste: "Comptable", lieu: "", publieeLe: "", adresse: "" }}
      avecLeTexte={false}
      envoyer={client.collerUneOffre}
      quandEnregistree={() => {}}
      renoncer={() => {}}
      libelleDuBouton="Enregistrer les modifications"
      libelleEnCours="Enregistrement…"
      libelleDuRenoncement="Renoncer à la modification"
      messageDEchec="Échec fictif."
    />,
  );

  await waitFor(() => expect(client.listerLesEntreprises).toHaveBeenCalled());
  expect(screen.queryByRole("button", { name: PROPOSER })).toBeNull();
  expect(screen.queryByText(SANS_IA_PRETE)).toBeNull();
  expect(client.lireLEtatDeLIA).not.toHaveBeenCalled();
});
