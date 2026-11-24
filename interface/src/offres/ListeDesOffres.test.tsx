import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { expect, test, vi } from "vitest";
import type { ClientDeLApi, OffreListee } from "../api/clientDeLApi.ts";
import { candidatureDeLOffreFictive, clientSimule, offreListeeFictive } from "../tests/clientSimule.ts";
import { AUCUN_FILTRE, type FiltresDesOffres } from "./affichage.ts";
import { ListeDesOffres } from "./ListeDesOffres.tsx";

const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script><b>gras</b>";

// Le 18/11/2026 à 14:30, heure de Bruxelles.
const maintenant = () => new Date("2026-11-18T13:30:00Z");

interface Rappels {
  collerUneOffre: () => void;
  importerUnFichier: () => void;
  ouvrirLOffre: (offre: number) => void;
  ouvrirLaCandidature: (candidature: number) => void;
}

// Les filtres tenus par un parent, comme l'application les garde le temps de la session.
function Avec({ client, ...rappels }: { client: ClientDeLApi } & Rappels) {
  const [filtres, setFiltres] = useState<FiltresDesOffres>(AUCUN_FILTRE);
  return (
    <ListeDesOffres
      client={client}
      filtres={filtres}
      changerLesFiltres={setFiltres}
      collerUneOffre={rappels.collerUneOffre}
      importerUnFichier={rappels.importerUnFichier}
      ouvrirLOffre={rappels.ouvrirLOffre}
      ouvrirLaCandidature={rappels.ouvrirLaCandidature}
      maintenant={maintenant}
    />
  );
}

function afficher(offres: OffreListee[] | Error) {
  const listerLesOffres = vi.fn<ClientDeLApi["listerLesOffres"]>(async () => (offres instanceof Error ? Promise.reject(offres) : offres));
  const client = clientSimule({ listerLesOffres });
  const rappels = {
    collerUneOffre: vi.fn<Rappels["collerUneOffre"]>(),
    importerUnFichier: vi.fn<Rappels["importerUnFichier"]>(),
    ouvrirLOffre: vi.fn<Rappels["ouvrirLOffre"]>(),
    ouvrirLaCandidature: vi.fn<Rappels["ouvrirLaCandidature"]>(),
  };
  render(<Avec client={client} {...rappels} />);
  return { client, ...rappels };
}

// Les postes des lignes du tableau, dans l'ordre.
function postes() {
  return within(screen.getByRole("table")).getAllByRole("row").slice(1).map((ligne) => within(ligne).getAllByRole("cell")[0]!.textContent);
}

function chercher(texte: string) {
  fireEvent.change(screen.getByLabelText("Rechercher"), { target: { value: texte } });
}

const OFFRES = [
  offreListeeFictive({ id: 5, poste: "Développeur .NET (H/F)", entreprise: { id: 1, nom: "Lumière Verte SA" }, lieu: "Liège", ajouteeLe: "2026-11-18T09:00:00Z" }),
  offreListeeFictive({ id: 4, poste: "Comptable", entreprise: null, lieu: "Namur", ajouteeLe: "2026-11-12T09:00:00Z" }),
  offreListeeFictive({
    id: 3,
    poste: "Brasseuse",
    entreprise: { id: 2, nom: "Brasserie Lune Rousse" },
    lieu: null,
    ajouteeLe: "2026-10-01T09:00:00Z",
    candidature: candidatureDeLOffreFictive({ id: 9, statut: "Entretien" }),
  }),
];

test("la liste montre les offres dans l'ordre de l'API, avec leur candidature, et chaque ligne mène à sa fiche", async () => {
  const { ouvrirLOffre, ouvrirLaCandidature, collerUneOffre, importerUnFichier } = afficher(OFFRES);

  const tableau = await screen.findByRole("table");
  expect(within(tableau).getAllByRole("columnheader").map((entete) => entete.textContent)).toEqual(["Poste", "Entreprise", "Lieu", "Ajoutée le", "Candidature"]);
  expect(postes()).toEqual(["Développeur .NET (H/F)", "Comptable", "Brasseuse"]);
  const lignes = within(tableau).getAllByRole("row").slice(1).map((ligne) => within(ligne).getAllByRole("cell").map((cellule) => cellule.textContent));
  expect(lignes[1]).toEqual(["Comptable", "Entreprise non indiquée", "Namur", "12/11/2026 10:00", "Aucune"]);
  expect(lignes[2]).toEqual(["Brasseuse", "Brasserie Lune Rousse", "", "01/10/2026 11:00", "Entretien"]);
  expect(screen.getByRole("status").textContent).toBe("3 offres");

  fireEvent.click(screen.getByRole("button", { name: "Développeur .NET (H/F), chez Lumière Verte SA : ouvrir la fiche" }));
  expect(ouvrirLOffre).toHaveBeenCalledWith(5);
  fireEvent.click(screen.getByRole("button", { name: "Candidature Entretien pour Brasseuse, chez Brasserie Lune Rousse : ouvrir sa fiche" }));
  expect(ouvrirLaCandidature).toHaveBeenCalledWith(9);
  fireEvent.click(screen.getByRole("button", { name: "Coller une offre" }));
  expect(collerUneOffre).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByRole("button", { name: "Importer un fichier CSV" }));
  expect(importerUnFichier).toHaveBeenCalledOnce();
});

test("des textes piégés s'affichent tels quels et échappés dans la liste", async () => {
  afficher([offreListeeFictive({ poste: PIEGE, entreprise: { id: 1, nom: PIEGE }, lieu: `${PIEGE} ${"à Liège, ".repeat(20)}` })]);

  await screen.findByRole("table");
  expect(screen.getAllByText(PIEGE).length).toBe(2);
  expect(document.querySelector("table img, table script, table b")).toBeNull();
});

test("sans offre, la liste le dit et mène à « Coller une offre », sans filtres", async () => {
  afficher([]);

  expect(await screen.findByText("Aucune offre enregistrée pour l'instant.")).toBeDefined();
  expect(screen.getByRole("button", { name: "Coller une offre" })).toBeDefined();
  expect(screen.queryByRole("search")).toBeNull();
});

test("une liste illisible le dit", async () => {
  afficher(new Error("panne fictive"));

  expect((await screen.findByRole("alert")).textContent).toBe("Les offres n'ont pas pu être chargées. Fermez Sollicito, puis relancez-le.");
});

test("le texte cherché trouve chaque mot dans le poste, l'entreprise ou le lieu, sans accents, majuscules ni blancs", async () => {
  afficher(OFFRES);
  await screen.findByRole("table");

  chercher("  DEVELOPPEUR   liege ");
  expect(postes()).toEqual(["Développeur .NET (H/F)"]);
  expect(screen.getByRole("status").textContent).toBe("1 offre sur 3");

  chercher("brasserie");
  expect(postes()).toEqual(["Brasseuse"]);
  chercher("namur comptable");
  expect(postes()).toEqual(["Comptable"]);
  chercher("lumière namur");
  expect(screen.queryByRole("table")).toBeNull();
  expect(screen.getByText("Aucune offre ne correspond à ces filtres.")).toBeDefined();
  expect(screen.getByRole("status").textContent).toBe("0 offre sur 3");
});

test("un texte cherché piégé ne se reprend nulle part, et rien de la recherche ne part vers l'API", async () => {
  const { client } = afficher(OFFRES);
  await screen.findByRole("table");

  chercher(PIEGE);

  expect(screen.getByText("Aucune offre ne correspond à ces filtres.")).toBeDefined();
  expect(document.querySelector("img, script, b")).toBeNull();
  expect(screen.getByLabelText<HTMLInputElement>("Rechercher").value).toBe(PIEGE);
  expect(screen.getByLabelText("Rechercher").getAttribute("maxlength")).toBe("200");
  // La liste s'est lue une fois, sans paramètre ; aucun autre appel n'a porté le texte cherché.
  expect(client.listerLesOffres).toHaveBeenCalledOnce();
  expect(client.listerLesOffres).toHaveBeenCalledWith();
  for (const appel of Object.values(client) as unknown as { mock: { calls: unknown[][] } }[]) {
    for (const parametres of appel.mock.calls) {
      expect(JSON.stringify(parametres)).not.toContain("onerror");
    }
  }
});

test("le filtre par date d'ajout garde les derniers jours, aujourd'hui compris, en date locale", async () => {
  afficher(OFFRES);
  await screen.findByRole("table");

  const periode = screen.getByLabelText<HTMLSelectElement>("Ajoutées");
  expect(Array.from(periode.options).map((option) => option.textContent)).toEqual(["Toutes", "Ces 7 derniers jours", "Ces 30 derniers jours", "Ces 90 derniers jours"]);
  fireEvent.change(periode, { target: { value: "7" } });
  // Le 12/11 est le premier des sept derniers jours, le 18/11 compris.
  expect(postes()).toEqual(["Développeur .NET (H/F)", "Comptable"]);
  fireEvent.change(periode, { target: { value: "30" } });
  expect(postes()).toEqual(["Développeur .NET (H/F)", "Comptable"]);
  fireEvent.change(periode, { target: { value: "90" } });
  expect(postes()).toEqual(["Développeur .NET (H/F)", "Comptable", "Brasseuse"]);
});

test("le filtre par source propose les sources, et « Effacer les filtres » rend toute la liste", async () => {
  afficher(OFFRES);
  await screen.findByRole("table");

  const source = screen.getByLabelText<HTMLSelectElement>("Source");
  expect(Array.from(source.options).map((option) => option.textContent)).toEqual(["Toutes les sources", "Texte collé", "Fichier CSV"]);
  expect(screen.queryByRole("button", { name: "Effacer les filtres" })).toBeNull();
  fireEvent.change(source, { target: { value: "TexteColle" } });
  chercher("comptable");
  expect(postes()).toEqual(["Comptable"]);

  fireEvent.click(screen.getByRole("button", { name: "Effacer les filtres" }));

  await waitFor(() => expect(postes()).toEqual(["Développeur .NET (H/F)", "Comptable", "Brasseuse"]));
  // Un texte de blancs ne restreint rien, mais le champ n'est pas vide : le bouton le remet à zéro.
  chercher("   ");
  expect(postes()).toEqual(["Développeur .NET (H/F)", "Comptable", "Brasseuse"]);
  fireEvent.click(screen.getByRole("button", { name: "Effacer les filtres" }));
  expect(screen.getByLabelText<HTMLInputElement>("Rechercher").value).toBe("");
  expect(screen.getByLabelText<HTMLSelectElement>("Source").value).toBe("toutes");
  expect(screen.getByRole("status").textContent).toBe("3 offres");
});
