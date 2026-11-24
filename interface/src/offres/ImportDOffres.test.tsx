import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { COPIE_AVANT_L_IMPORT_IMPOSSIBLE, FICHIER_TROP_GROS, type ClientDeLApi } from "../api/clientDeLApi.ts";
import {
  clientSimule,
  comptesFictifs,
  compteRenduFictif,
  ligneDuCompteRenduFictive,
  offreFictive,
  offreSignaleeFictive,
} from "../tests/clientSimule.ts";
import { ImportDOffres } from "./ImportDOffres.tsx";

const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script><b>gras</b>";

// Un aperçu qui passe par chaque sort : à importer, piégée, doublon possible, déjà enregistrée, en double, invalide.
const APERCU = compteRenduFictif({
  lignes: [
    ligneDuCompteRenduFictive({ ligne: 2, poste: PIEGE, entreprise: `=HYPERLINK("http://exemple.invalid")`, lieu: "Liège" }),
    ligneDuCompteRenduFictive({
      ligne: 3,
      sort: "doublonPossible",
      poste: "Comptable",
      doublons: [offreSignaleeFictive({ id: 7, poste: "Comptable", lieu: "Huy" })],
      lignesDuFichier: [],
    }),
    ligneDuCompteRenduFictive({ ligne: 4, sort: "doublonPossible", poste: "Brasseur", entreprise: "Brasserie Lune Rousse", doublons: [], lignesDuFichier: [2] }),
    ligneDuCompteRenduFictive({ ligne: 5, sort: "dejaEnregistree", trouvee: { id: 9, ajouteeLe: "2026-11-18T09:00:00Z" } }),
    ligneDuCompteRenduFictive({ ligne: 6, sort: "enDouble", ligneDOrigine: 2 }),
    ligneDuCompteRenduFictive({
      ligne: 7,
      sort: "invalide",
      poste: null,
      entreprise: null,
      lieu: null,
      refus: { publieeLe: ["La date de publication s'écrit JJ/MM/AAAA ou AAAA-MM-JJ, par exemple 18/11/2026."] },
    }),
    ligneDuCompteRenduFictive({ ligne: 8, sort: "invalide", poste: null, entreprise: null, lieu: null, refus: { ligne: ["La ligne n'a pas autant de cellules que l'en-tête."] } }),
  ],
  comptes: comptesFictifs({ lignes: 7, lignesVides: 1, aImporter: 1, dejaEnregistrees: 1, enDouble: 1, doublonsPossibles: 2, invalides: 2 }),
});

function fichier(contenu = "poste\nComptable\n", nom = "offres-fictives.csv"): File {
  return new File([contenu], nom, { type: "text/csv" });
}

function afficher(client: ClientDeLApi = clientSimule({ examinerUnFichierCsv: vi.fn(async () => ({ issue: "lu" as const, compteRendu: APERCU })) })) {
  const revenir = vi.fn();
  const ouvrirLOffre = vi.fn();
  render(<ImportDOffres client={client} revenir={revenir} ouvrirLOffre={ouvrirLOffre} />);
  return { client, revenir, ouvrirLOffre };
}

function choisir(choisi: File) {
  fireEvent.change(screen.getByLabelText("Fichier CSV"), { target: { files: [choisi] } });
}

test("l'écran dit le format, l'en-tête à recopier, et propose de choisir un fichier CSV", () => {
  afficher();

  expect(screen.getByRole("heading", { level: 2, name: "Importer des offres" })).toBeDefined();
  expect(screen.getByText("poste;entreprise;lieu;publieeLe;adresse;texte")).toBeDefined();
  expect(screen.getByText("La date de publication s'écrit JJ/MM/AAAA ou AAAA-MM-JJ, par exemple 18/11/2026.")).toBeDefined();
  expect(screen.getByText(/2 Mo et 1 000 offres au plus/)).toBeDefined();
  const champ = screen.getByLabelText<HTMLInputElement>("Fichier CSV");
  expect(champ.type).toBe("file");
  expect(champ.accept).toBe(".csv,text/csv");
});

test("la page envoie les octets du fichier, et rien d'autre : ni son nom, ni son chemin", async () => {
  const { client } = afficher();

  choisir(fichier("poste;lieu\nComptable;Liège\n", "C:\\Users\\Compte.Fictif\\offres-fictives.csv"));

  await waitFor(() => expect(client.examinerUnFichierCsv).toHaveBeenCalledOnce());
  const [contenu, ...reste] = vi.mocked(client.examinerUnFichierCsv).mock.calls[0];
  expect(reste).toEqual([]);
  expect(new TextDecoder().decode(contenu)).toBe("poste;lieu\nComptable;Liège\n");
});

test("l'aperçu montre les comptes et le sort de chaque ligne, cellules piégées affichées en texte", async () => {
  afficher();

  choisir(fichier());

  expect(await screen.findByRole("heading", { level: 3, name: "Aperçu" })).toBeDefined();
  for (const compte of [
    "1 offre à importer",
    "1 déjà enregistrée : écartée",
    "1 en double dans le fichier : écartée",
    "2 doublons possibles : importés seulement si vous les gardez",
    "2 lignes invalides : non importées",
    "1 ligne vide ignorée",
  ]) {
    expect(screen.getByText(compte)).toBeDefined();
  }

  const tableau = screen.getByRole("table", { name: "Ce que Sollicito ferait de chaque ligne." });
  expect(within(tableau).getByText(PIEGE)).toBeDefined();
  expect(within(tableau).getByText(`=HYPERLINK("http://exemple.invalid")`)).toBeDefined();
  expect(document.querySelector("table img, table script, table b")).toBeNull();
  expect(within(tableau).getByText("la ligne 2 du fichier")).toBeDefined();
  expect(within(tableau).getByText(/En double : la même offre qu'à la ligne 2, écartée/)).toBeDefined();
  expect(within(tableau).getByText("colonne publieeLe : La date de publication s'écrit JJ/MM/AAAA ou AAAA-MM-JJ, par exemple 18/11/2026.")).toBeDefined();
  expect(within(tableau).getByText("La ligne n'a pas autant de cellules que l'en-tête.")).toBeDefined();
  expect(screen.getByRole("button", { name: "Importer 1 offre" })).toBeDefined();
});

test("chaque doublon possible se garde à part, décoché d'office, et l'import reçoit les lignes gardées", async () => {
  const { client } = afficher();
  choisir(fichier());
  const garderLaLigne3 = await screen.findByRole<HTMLInputElement>("checkbox", { name: "Garder aussi l'offre de la ligne 3" });
  const garderLaLigne4 = screen.getByRole<HTMLInputElement>("checkbox", { name: "Garder aussi l'offre de la ligne 4" });
  expect(garderLaLigne3.checked).toBe(false);
  expect(garderLaLigne4.checked).toBe(false);

  fireEvent.click(garderLaLigne4);
  fireEvent.click(garderLaLigne3);
  fireEvent.click(garderLaLigne3);
  fireEvent.click(screen.getByRole("button", { name: "Importer 2 offres" }));

  await waitFor(() => expect(client.importerUnFichierCsv).toHaveBeenCalledOnce());
  const [contenu, garder] = vi.mocked(client.importerUnFichierCsv).mock.calls[0];
  expect(new TextDecoder().decode(contenu)).toBe("poste\nComptable\n");
  expect(garder).toEqual([4]);
});

test("le compte rendu dit ce qui a été importé, mène à chaque offre importée, et ramène aux offres", async () => {
  const { ouvrirLOffre, revenir } = afficher();
  choisir(fichier());
  fireEvent.click(await screen.findByRole("button", { name: "Importer 1 offre" }));

  expect(await screen.findByRole("heading", { level: 3, name: "Compte rendu" })).toBeDefined();
  expect(screen.getByRole("status").textContent).toBe("1 offre importée.");
  fireEvent.click(screen.getByRole("button", { name: "Voir l'offre importée de la ligne 2" }));
  expect(ouvrirLOffre).toHaveBeenCalledWith(21);
  fireEvent.click(screen.getByRole("button", { name: "Revenir aux offres" }));
  expect(revenir).toHaveBeenCalledOnce();
});

test("si la base a changé depuis l'aperçu, le compte rendu le dit", async () => {
  afficher(
    clientSimule({
      examinerUnFichierCsv: vi.fn(async () => ({ issue: "lu" as const, compteRendu: compteRenduFictif() })),
      importerUnFichierCsv: vi.fn(async () => ({
        issue: "lu" as const,
        compteRendu: compteRenduFictif({
          lignes: [ligneDuCompteRenduFictive({ sort: "doublonPossible", doublons: [offreSignaleeFictive()], lignesDuFichier: [] })],
          comptes: comptesFictifs({ aImporter: 0, doublonsPossibles: 1 }),
        }),
      })),
    }),
  );
  choisir(fichier());
  fireEvent.click(await screen.findByRole("button", { name: "Importer 1 offre" }));

  expect(await screen.findByText("La base a changé depuis l'aperçu : voici ce qui a été fait.")).toBeDefined();
  expect(screen.getByRole("status").textContent).toBe("Aucune offre importée.");
  expect(screen.getByText("Écartée : doublon possible non gardé")).toBeDefined();
});

test("un fichier de plus de 2 Mo est refusé avant d'être lu", async () => {
  const { client } = afficher();
  const gros = fichier();
  Object.defineProperty(gros, "size", { value: 2 * 1024 * 1024 + 1 });

  choisir(gros);

  expect((await screen.findByRole("alert")).textContent).toBe(FICHIER_TROP_GROS);
  expect(client.examinerUnFichierCsv).not.toHaveBeenCalled();
});

test("un fichier refusé par l'API se dit, sans rien importer, et un autre fichier se choisit", async () => {
  afficher(
    clientSimule({
      examinerUnFichierCsv: vi.fn(async () => ({
        issue: "refuse" as const,
        motif: "Le fichier n'est pas en UTF-8 : enregistrez-le au format CSV UTF-8, puis réessayez.",
      })),
    }),
  );

  choisir(fichier());

  expect((await screen.findByRole("alert")).textContent).toBe("Le fichier n'est pas en UTF-8 : enregistrez-le au format CSV UTF-8, puis réessayez.");
  expect(screen.getByRole("button", { name: "Choisir un autre fichier" })).toBeDefined();
  expect(screen.queryByRole("button", { name: /Importer/ })).toBeNull();
});

test("un fichier refusé à l'import dit que rien n'a été importé", async () => {
  afficher(
    clientSimule({
      examinerUnFichierCsv: vi.fn(async () => ({ issue: "lu" as const, compteRendu: compteRenduFictif() })),
      importerUnFichierCsv: vi.fn(async () => ({ issue: "refuse" as const, motif: FICHIER_TROP_GROS })),
    }),
  );
  choisir(fichier());
  fireEvent.click(await screen.findByRole("button", { name: "Importer 1 offre" }));

  expect((await screen.findByRole("alert")).textContent).toBe(`${FICHIER_TROP_GROS} Rien n'a été importé.`);
});

test("un import que la copie de la base n'a pas pu précéder dit que rien n'a été importé (#40, ADR-0015)", async () => {
  afficher(
    clientSimule({
      examinerUnFichierCsv: vi.fn(async () => ({ issue: "lu" as const, compteRendu: compteRenduFictif() })),
      importerUnFichierCsv: vi.fn(async () => ({ issue: "refuse" as const, motif: COPIE_AVANT_L_IMPORT_IMPOSSIBLE })),
    }),
  );
  choisir(fichier());
  fireEvent.click(await screen.findByRole("button", { name: "Importer 1 offre" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "Sollicito n'a pas pu copier votre base avant l'import : vérifiez la place libre sur le disque, puis réessayez. Rien n'a été importé.",
  );
  expect(screen.getByRole("button", { name: "Choisir un autre fichier" })).toBeDefined();
});

test("une API injoignable se dit, à l'examen comme à l'import", async () => {
  afficher(clientSimule({ examinerUnFichierCsv: vi.fn(async () => Promise.reject(new Error("injoignable"))) }));
  choisir(fichier());
  expect((await screen.findByRole("alert")).textContent).toBe(
    "Le fichier n'a pas pu être examiné. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
});

test("après un échec de l'import, on peut choisir un autre fichier ou revenir aux offres", async () => {
  const { revenir } = afficher(
    clientSimule({
      examinerUnFichierCsv: vi.fn(async () => ({ issue: "lu" as const, compteRendu: compteRenduFictif() })),
      importerUnFichierCsv: vi.fn(async () => Promise.reject(new Error("injoignable"))),
    }),
  );
  choisir(fichier());
  fireEvent.click(await screen.findByRole("button", { name: "Importer 1 offre" }));

  expect((await screen.findByRole("alert")).textContent).toBe(
    "L'import n'a pas pu se faire : rien n'a été importé. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
  expect(screen.getByRole("button", { name: "Choisir un autre fichier" })).toBeDefined();
  fireEvent.click(screen.getByRole("button", { name: "Annuler" }));
  expect(revenir).toHaveBeenCalledOnce();
});

test("le champ se vide dès le choix : le même fichier se choisit de nouveau, même si sa lecture a échoué", async () => {
  const { client } = afficher();
  const illisible = fichier();
  illisible.arrayBuffer = () => Promise.reject(new Error("fichier déplacé"));
  const champ = screen.getByLabelText<HTMLInputElement>("Fichier CSV");
  const vider = vi.spyOn(champ, "value", "set");

  choisir(illisible);

  await screen.findByRole("alert");
  expect(vider).toHaveBeenCalledWith("");
  expect(client.examinerUnFichierCsv).not.toHaveBeenCalled();
});

test("deux offres trouvées décrites de même gardent chacune leur bouton, distinct pour un lecteur d'écran", async () => {
  const jumelles = { poste: "Comptable", lieu: "Huy", ajouteeLe: "2026-11-18T09:00:00Z" };
  const client = clientSimule({
    examinerUnFichierCsv: vi.fn(async () => ({
      issue: "lu" as const,
      compteRendu: compteRenduFictif({
        lignes: [
          ligneDuCompteRenduFictive({
            sort: "doublonPossible",
            poste: "Comptable",
            doublons: [offreSignaleeFictive({ id: 7, ...jumelles }), offreSignaleeFictive({ id: 8, ...jumelles })],
            lignesDuFichier: [],
          }),
        ],
        comptes: comptesFictifs({ aImporter: 0, doublonsPossibles: 1 }),
      }),
    })),
  });
  afficher(client);
  choisir(fichier());

  fireEvent.click(await screen.findByRole("button", { name: /\(2 sur 2\)$/ }));
  expect(document.querySelectorAll(".doublons-d-une-ligne li")).toHaveLength(2);

  await waitFor(() => expect(client.lireUneOffre).toHaveBeenCalledWith(8));
  expect(screen.getByRole("button", { name: /\(1 sur 2\)$/ })).toBeDefined();
});

test("sans offre à importer, l'import ne se propose pas", async () => {
  afficher(
    clientSimule({
      examinerUnFichierCsv: vi.fn(async () => ({
        issue: "lu" as const,
        compteRendu: compteRenduFictif({
          lignes: [ligneDuCompteRenduFictive({ sort: "dejaEnregistree", trouvee: { id: 9, ajouteeLe: "2026-11-18T09:00:00Z" } })],
          comptes: comptesFictifs({ aImporter: 0, dejaEnregistrees: 1 }),
        }),
      })),
    }),
  );

  choisir(fichier());

  expect(await screen.findByText("Aucune offre à importer.")).toBeDefined();
  expect(screen.getByRole<HTMLButtonElement>("button", { name: "Importer" }).disabled).toBe(true);
});

test("« Voir cette offre » ouvre l'offre trouvée sur place, sans quitter l'aperçu", async () => {
  const client = clientSimule({
    examinerUnFichierCsv: vi.fn(async () => ({ issue: "lu" as const, compteRendu: APERCU })),
    lireUneOffre: vi.fn(async (offre: number) => offreFictive({ id: offre, poste: "Comptable", texte: `Texte de l'offre trouvée ${PIEGE}` })),
  });
  afficher(client);
  choisir(fichier());

  fireEvent.click(await screen.findByRole("button", { name: /^Voir l'offre Comptable/ }));

  await waitFor(() => expect(client.lireUneOffre).toHaveBeenCalledWith(7));
  expect(await screen.findByText(`Texte de l'offre trouvée ${PIEGE}`)).toBeDefined();
  expect(screen.getByRole("heading", { level: 3, name: "Aperçu" })).toBeDefined();
  expect(document.querySelector("img, script, b")).toBeNull();
});
