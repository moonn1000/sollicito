// Les champs de texte courts et les notes s'arrêtent à 10 000 caractères (#98, point 3) : bien au-delà de toute limite de
// l'API, si bien qu'un texte collé trop long garde son refus lisible, et assez bas pour qu'aucune saisie n'atteigne la
// borne des corps, 256 Kio (BornesDesCorpsTests).
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { NoterUneEtape } from "../candidatures/NoterUneEtape.tsx";
import { SaisieDUneCandidature } from "../candidatures/SaisieDUneCandidature.tsx";
import { SaisieDUneOffre } from "../offres/SaisieDUneOffre.tsx";
import { ContactsDeLEntreprise } from "../relances/ContactsDeLEntreprise.tsx";
import { clientSimule } from "../tests/clientSimule.ts";

const longueurDe = (libelle: string) => screen.getByLabelText(libelle).getAttribute("maxlength");

test("les champs de texte et la note s'arrêtent à 10 000 caractères ; le texte d'une offre n'a pas de borne", () => {
  render(<SaisieDUneCandidature client={clientSimule()} quandEnregistree={vi.fn()} />);
  expect(["Entreprise", "Poste"].map(longueurDe)).toEqual(["10000", "10000"]);
  cleanup();

  render(<NoterUneEtape client={clientSimule()} candidature={7} quandNotee={vi.fn()} />);
  expect(longueurDe("Note (facultative)")).toBe("10000");
  cleanup();

  render(<ContactsDeLEntreprise client={clientSimule()} entreprise={{ id: 1, nom: "Lumière Verte SA" }} contacts={[]} remplacer={vi.fn()} quandChanges={vi.fn()} />);
  fireEvent.click(screen.getByRole("button", { name: "Ajouter un contact" }));
  expect(["Nom", "Adresse électronique"].map(longueurDe)).toEqual(["10000", "10000"]);
  cleanup();

  render(<SaisieDUneOffre client={clientSimule()} quandEnregistree={vi.fn()} renoncer={vi.fn()} />);
  expect(["Entreprise", "Poste", "Lieu", "Adresse de l'annonce"].map(longueurDe)).toEqual(["10000", "10000", "10000", "10000"]);
  expect(longueurDe("Texte de l'offre")).toBeNull();
});
