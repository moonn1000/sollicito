import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import type { ClientDeLApi, EtatDeLIA } from "../api/clientDeLApi.ts";
import type { IssueDuBrouillon } from "../hote/canalDeLHote.ts";
import { clientSimule, etatDeLIAFictif, ficheFictive, relanceFictive } from "../tests/clientSimule.ts";
import { MESSAGES_DU_BROUILLON } from "./affichage.ts";
import { PremierJetParLIA, pourquoiSansIA, TEXTES_DU_PREMIER_JET } from "./PremierJetParLIA.tsx";
import { SuiviDesRelances } from "./SuiviDesRelances.tsx";

const TEXTE = "Bonjour Jeanne Martin,\n\nJe me permets de revenir vers vous au sujet de ma candidature.\n\nBien cordialement,";

const CLAUDE_PRET = etatDeLIAFictif({ fournisseur: "Claude", cle: "Enregistree" });

function rendre(
  modifications: {
    client?: ReturnType<typeof clientSimule>;
    ouvrirLeBrouillonRelu?: (relance: number) => Promise<IssueDuBrouillon>;
  } = {},
) {
  const client = modifications.client ?? clientSimule({ lireLEtatDeLIA: vi.fn<ClientDeLApi["lireLEtatDeLIA"]>(async () => CLAUDE_PRET) });
  const ouvrirLeBrouillonRelu = modifications.ouvrirLeBrouillonRelu ?? vi.fn<(relance: number) => Promise<IssueDuBrouillon>>(async () => "ouvert");
  const apres = vi.fn();
  render(<PremierJetParLIA client={client} candidature={1} relance={7} ouvrirLeBrouillonRelu={ouvrirLeBrouillonRelu} apres={apres} />);
  return { client, ouvrirLeBrouillonRelu, apres };
}

async function rediger() {
  fireEvent.click(await screen.findByRole("button", { name: "Rédiger un premier jet avec Claude" }));
  return (await screen.findByLabelText(TEXTES_DU_PREMIER_JET.etiquette)) as HTMLTextAreaElement;
}

test.each<[string, Partial<EtatDeLIA>, string]>([
  ["aucune IA choisie", { fournisseur: "Aucun", choisi: false }, TEXTES_DU_PREMIER_JET.sansIA.aucune],
  ["« Aucune IA »", { fournisseur: "Aucun" }, TEXTES_DU_PREMIER_JET.sansIA.aucune],
  ["le modèle local", { fournisseur: "Ollama" }, TEXTES_DU_PREMIER_JET.sansIA.modeleLocal],
  ["Claude sans clé", { fournisseur: "Claude", cle: "Aucune" }, TEXTES_DU_PREMIER_JET.sansIA.sansCle],
  ["OpenAI et une clé illisible", { fournisseur: "OpenAI", cle: "Illisible" }, TEXTES_DU_PREMIER_JET.sansIA.cleIllisible],
  ["un réglage illisible", { fournisseur: "Aucun", reglageLisible: false }, TEXTES_DU_PREMIER_JET.sansIA.reglageIllisible],
])("sans IA prête (%s), le bloc le dit, sans bouton, et rien ne part", async (_cas, etat, phrase) => {
  const { client } = rendre({ client: clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif(etat) }) });

  expect(await screen.findByText(phrase)).toBeDefined();
  expect(screen.queryByRole("button")).toBeNull();
  expect(client.demanderUnPremierJet).not.toHaveBeenCalled();
});

test("pourquoi l'IA n'est pas prête : rien quand elle l'est", () => {
  expect(pourquoiSansIA(CLAUDE_PRET)).toBeUndefined();
  expect(pourquoiSansIA(etatDeLIAFictif({ fournisseur: "OpenAI", cle: "Enregistree" }))).toBeUndefined();
});

test("avant le clic, l'écran dit ce qui part chez Anthropic, et rien ne part à l'affichage", async () => {
  const { client } = rendre();

  expect(
    await screen.findByText(
      "Sur votre demande, le poste, l'entreprise et la date d'envoi de cette candidature partent chez Anthropic, avec les consignes de Sollicito. Rien d'autre : ni le contact, ni vos notes, ni l'historique. L'appel est décompté sur votre compte chez Anthropic.",
    ),
  ).toBeDefined();
  expect(screen.getByRole("heading", { name: "Premier jet par l'IA" })).toBeDefined();
  expect(client.demanderUnPremierJet).not.toHaveBeenCalled();
  expect(client.deposerLeBrouillonRelu).not.toHaveBeenCalled();
});

test("avec OpenAI, l'écran nomme OpenAI", async () => {
  rendre({ client: clientSimule({ lireLEtatDeLIA: async () => etatDeLIAFictif({ fournisseur: "OpenAI", cle: "Enregistree" }) }) });

  expect(await screen.findByRole("button", { name: "Rédiger un premier jet avec OpenAI" })).toBeDefined();
  expect(screen.getByText(/partent chez OpenAI, avec les consignes de Sollicito\..*sur votre compte chez OpenAI\.$/u)).toBeDefined();
});

test("le clic demande un premier jet, une fois, et le texte revient dans un champ modifiable, borné", async () => {
  const { client } = rendre();

  const champ = await rediger();

  expect(vi.mocked(client.demanderUnPremierJet).mock.calls).toEqual([[1, 7]]);
  expect(champ.tagName).toBe("TEXTAREA");
  expect(champ.value).toBe(TEXTE);
  expect(champ.maxLength).toBe(2_000);
  expect(screen.getByText(TEXTES_DU_PREMIER_JET.aide)).toBeDefined();
});

test("un second clic pendant la demande n'en envoie pas une autre", async () => {
  let repondre: (valeur: Awaited<ReturnType<ClientDeLApi["demanderUnPremierJet"]>>) => void = () => {};
  const demanderUnPremierJet = vi.fn<ClientDeLApi["demanderUnPremierJet"]>(
    () =>
      new Promise((resoudre) => {
        repondre = resoudre;
      }),
  );
  rendre({ client: clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET, demanderUnPremierJet }) });
  const bouton = await screen.findByRole("button", { name: "Rédiger un premier jet avec Claude" });

  fireEvent.click(bouton);
  fireEvent.click(bouton);

  expect(screen.getByRole("button", { name: "Rédaction en cours…" })).toHaveProperty("disabled", true);
  expect(demanderUnPremierJet).toHaveBeenCalledOnce();
  repondre({ issue: "redige", texte: TEXTE });
  expect(await screen.findByLabelText(TEXTES_DU_PREMIER_JET.etiquette)).toBeDefined();
});

test("un texte piégé s'affiche comme du texte : aucun élément n'en naît, rien ne s'exécute", async () => {
  const piege = '<img src="x" onerror="document.title=\'piege\'"><script>document.title="piege"</script><b>gras</b>';
  rendre({
    client: clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET, demanderUnPremierJet: async () => ({ issue: "redige", texte: piege }) }),
  });
  const titre = document.title;

  const champ = await rediger();

  expect(champ.value).toBe(piege);
  expect(document.querySelector("img")).toBeNull();
  expect(document.querySelector("b")).toBeNull();
  expect(document.querySelectorAll("script")).toHaveLength(0);
  expect(document.title).toBe(titre);
});

test("le texte modifié se dépose, puis la fenêtre ouvre le brouillon relu, et l'issue s'affiche", async () => {
  const { client, ouvrirLeBrouillonRelu } = rendre();
  const champ = await rediger();
  const ordre: string[] = [];
  vi.mocked(client.deposerLeBrouillonRelu).mockImplementation(async () => {
    ordre.push("depot");
    return { issue: "depose" };
  });
  vi.mocked(ouvrirLeBrouillonRelu).mockImplementation(async () => {
    ordre.push("ouverture");
    return "ouvert";
  });

  fireEvent.change(champ, { target: { value: "Bonjour Jeanne,\n\nMon texte, corrigé.\n\nBien cordialement," } });
  fireEvent.click(screen.getByRole("button", { name: "Ouvrir dans ma messagerie" }));

  expect((await screen.findByRole("status")).textContent).toBe(MESSAGES_DU_BROUILLON.ouvert);
  expect(vi.mocked(client.deposerLeBrouillonRelu).mock.calls).toEqual([[1, 7, "Bonjour Jeanne,\n\nMon texte, corrigé.\n\nBien cordialement,"]]);
  expect(vi.mocked(ouvrirLeBrouillonRelu).mock.calls).toEqual([[7]]);
  expect(ordre).toEqual(["depot", "ouverture"]);
});

test.each<IssueDuBrouillon>(["impossible", "sansReponse"])("une ouverture « %s » se dit, et le texte reste", async (issue) => {
  const { apres } = rendre({ ouvrirLeBrouillonRelu: async () => issue });
  await rediger();

  fireEvent.click(screen.getByRole("button", { name: "Ouvrir dans ma messagerie" }));

  expect((await screen.findByRole("alert")).textContent).toBe(MESSAGES_DU_BROUILLON[issue]);
  expect(screen.getByLabelText(TEXTES_DU_PREMIER_JET.etiquette)).toBeDefined();
  expect(apres).not.toHaveBeenCalled();
});

test("une ouverture refusée, la relance n'étant plus à faire, se dit et fait relire la fiche", async () => {
  const { apres } = rendre({ ouvrirLeBrouillonRelu: async () => "refuse" });
  await rediger();

  fireEvent.click(screen.getByRole("button", { name: "Ouvrir dans ma messagerie" }));

  expect((await screen.findByRole("alert")).textContent).toBe(MESSAGES_DU_BROUILLON.refuse);
  expect(apres).toHaveBeenCalledOnce();
});

test("un texte relu refusé par sa règle se dit sous le champ, et rien ne s'ouvre", async () => {
  const deposerLeBrouillonRelu = vi.fn<ClientDeLApi["deposerLeBrouillonRelu"]>(async () => ({
    issue: "refusee",
    refus: ["Le brouillon est trop long pour s'ouvrir dans votre messagerie : raccourcissez-le."],
  }));
  const { ouvrirLeBrouillonRelu } = rendre({ client: clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET, deposerLeBrouillonRelu }) });
  const champ = await rediger();

  fireEvent.click(screen.getByRole("button", { name: "Ouvrir dans ma messagerie" }));

  expect(await screen.findByText("Le brouillon est trop long pour s'ouvrir dans votre messagerie : raccourcissez-le.")).toBeDefined();
  expect(champ.getAttribute("aria-invalid")).toBe("true");
  expect(ouvrirLeBrouillonRelu).not.toHaveBeenCalled();
  // Corriger le texte efface le refus.
  fireEvent.change(champ, { target: { value: "Plus court." } });
  expect(champ.getAttribute("aria-invalid")).toBeNull();
});

test("un dépôt refusé par la relance se dit, fait relire la fiche, et rien ne s'ouvre", async () => {
  const motif = "Cette relance n'est plus à faire : elle a déjà été marquée faite ou abandonnée, ou la fiche a changé entre-temps.";
  const { ouvrirLeBrouillonRelu, apres } = rendre({
    client: clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET, deposerLeBrouillonRelu: async () => ({ issue: "refuse", motif }) }),
  });
  await rediger();

  fireEvent.click(screen.getByRole("button", { name: "Ouvrir dans ma messagerie" }));

  expect((await screen.findByRole("alert")).textContent).toBe(motif);
  expect(ouvrirLeBrouillonRelu).not.toHaveBeenCalled();
  expect(apres).toHaveBeenCalledOnce();
});

test("un dépôt qui échoue se dit", async () => {
  rendre({
    client: clientSimule({
      lireLEtatDeLIA: async () => CLAUDE_PRET,
      deposerLeBrouillonRelu: async () => {
        throw new Error("Réseau local coupé.");
      },
    }),
  });
  await rediger();

  fireEvent.click(screen.getByRole("button", { name: "Ouvrir dans ma messagerie" }));

  expect((await screen.findByRole("alert")).textContent).toBe(TEXTES_DU_PREMIER_JET.depotImpossible);
});

test("un échec de l'IA se dit, et renvoie au brouillon de Sollicito", async () => {
  const message = "Claude a refusé la clé : vérifiez-la, ou remplacez-la. Sollicito continue sans IA.";
  rendre({ client: clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET, demanderUnPremierJet: async () => ({ issue: "echoue", message }) }) });

  fireEvent.click(await screen.findByRole("button", { name: "Rédiger un premier jet avec Claude" }));

  expect((await screen.findByRole("alert")).textContent).toBe(`${message} Le brouillon de Sollicito reste disponible : « Préparer le brouillon ».`);
  expect(screen.queryByLabelText(TEXTES_DU_PREMIER_JET.etiquette)).toBeNull();
  expect(screen.getByRole("button", { name: "Rédiger un premier jet avec Claude" })).toHaveProperty("disabled", false);
});

test("un refus sans requête se dit par son motif", async () => {
  const motif = "Une demande à l'IA est déjà en cours : attendez sa réponse.";
  rendre({ client: clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET, demanderUnPremierJet: async () => ({ issue: "refuse", motif }) }) });

  fireEvent.click(await screen.findByRole("button", { name: "Rédiger un premier jet avec Claude" }));

  expect((await screen.findByRole("alert")).textContent).toBe(motif);
});

test("une demande qui ne part pas se dit", async () => {
  rendre({
    client: clientSimule({
      lireLEtatDeLIA: async () => CLAUDE_PRET,
      demanderUnPremierJet: async () => {
        throw new Error("Réseau local coupé.");
      },
    }),
  });

  fireEvent.click(await screen.findByRole("button", { name: "Rédiger un premier jet avec Claude" }));

  expect((await screen.findByRole("alert")).textContent).toBe(TEXTES_DU_PREMIER_JET.demandeImpossible);
});

test("effacer le premier jet retire le champ", async () => {
  rendre();
  await rediger();

  fireEvent.click(screen.getByRole("button", { name: "Effacer le premier jet" }));

  expect(screen.queryByLabelText(TEXTES_DU_PREMIER_JET.etiquette)).toBeNull();
  expect(screen.getByRole("button", { name: "Rédiger un premier jet avec Claude" })).toBeDefined();
});

test("l'état de l'IA illisible laisse le bouton, et le clic dira ce qu'il en est", async () => {
  rendre({
    client: clientSimule({
      lireLEtatDeLIA: async () => {
        throw new Error("API injoignable.");
      },
    }),
  });

  expect(await screen.findByRole("button", { name: "Rédiger un premier jet" })).toBeDefined();
  expect(screen.getByText(TEXTES_DU_PREMIER_JET.avantLeClic(undefined))).toBeDefined();
});

test("dans la fiche, le bloc suit la relance à faire, et n'existe pas hors de la fenêtre", async () => {
  const fiche = ficheFictive({ relances: [relanceFictive()] });
  const client = clientSimule({ lireLEtatDeLIA: async () => CLAUDE_PRET });
  const proprietes = { client, fiche, contacts: [], montrer: vi.fn(), relire: vi.fn(async () => true) };

  const { unmount } = render(<SuiviDesRelances {...proprietes} ouvrirLeBrouillon={async () => "ouvert"} ouvrirLeBrouillonRelu={async () => "ouvert"} />);
  const section = await screen.findByRole("region", { name: "Premier jet par l'IA" });
  expect(within(section).getByRole("button", { name: "Rédiger un premier jet avec Claude" })).toBeDefined();
  unmount();

  render(<SuiviDesRelances {...proprietes} />);
  expect(screen.queryByRole("region", { name: "Premier jet par l'IA" })).toBeNull();
  expect(client.demanderUnPremierJet).not.toHaveBeenCalled();
});
