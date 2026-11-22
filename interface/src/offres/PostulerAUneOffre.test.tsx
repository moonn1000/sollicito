import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { ErreurDeLApi, type ClientDeLApi, type ResultatDeLaPostulation } from "../api/clientDeLApi.ts";
import { candidatureDeLOffreFictive, candidatureFictive, clientSimule, offreFictive, offreSignaleeFictive } from "../tests/clientSimule.ts";
import { PostulerAUneOffre } from "./PostulerAUneOffre.tsx";

const PIEGE = "<img src=x onerror=alert(1)><script>alert('fictif')</script><b>gras</b>";

// Le 18/11/2026 à 14:30, heure de Bruxelles.
const maintenant = () => new Date("2026-11-18T13:30:00Z");

function afficher(client: ClientDeLApi = clientSimule()) {
  const revenir = vi.fn();
  const quandEnregistree = vi.fn();
  const ouvrirLaCandidature = vi.fn();
  const indiquerLEntreprise = vi.fn();
  render(
    <PostulerAUneOffre
      client={client}
      offre={3}
      revenir={revenir}
      quandEnregistree={quandEnregistree}
      ouvrirLaCandidature={ouvrirLaCandidature}
      indiquerLEntreprise={indiquerLEntreprise}
      maintenant={maintenant}
    />,
  );
  return { client, revenir, quandEnregistree, ouvrirLaCandidature, indiquerLEntreprise };
}

function envoyer() {
  fireEvent.click(screen.getByRole("button", { name: "Enregistrer la candidature" }));
}

test("l'écran rappelle l'offre et ne demande que la date d'envoi, préremplie : la candidature part d'Envoyée", async () => {
  const { client, quandEnregistree } = afficher();

  const titre = await screen.findByRole("heading", { level: 2, name: "Postuler à cette offre" });
  await waitFor(() => expect(document.activeElement).toBe(titre));
  const resume = Array.from(document.querySelectorAll("dt")).map((dt) => [dt.textContent, dt.nextElementSibling?.textContent]);
  expect(resume).toEqual([
    ["Poste", "Développeur .NET (H/F)"],
    ["Entreprise", "Lumière Verte SA"],
    ["Lieu", "Liège"],
  ]);
  const date = await screen.findByLabelText<HTMLInputElement>("Date et heure d'envoi");
  expect(date.type).toBe("datetime-local");
  expect(date.value).toBe("2026-11-18T14:30");
  expect(screen.getByText(/^Statut de départ/u).textContent).toBe("Statut de départ : Envoyée");
  // Ni entreprise ni poste à saisir : l'API les reprend de l'offre.
  expect(screen.queryByLabelText("Entreprise")).toBeNull();
  expect(screen.queryByLabelText("Poste")).toBeNull();

  fireEvent.change(date, { target: { value: "2026-11-18T10:00" } });
  envoyer();

  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledOnce());
  expect(client.postuler).toHaveBeenCalledExactlyOnceWith(3, "2026-11-18T10:00");
});

test("une date refusée s'affiche à côté du champ, qui reprend le focus ; la modifier retire le refus", async () => {
  afficher(clientSimule({ postuler: async () => ({ issue: "refusee", refus: ["La date et l'heure d'envoi ne peuvent pas être dans le futur."] }) }));

  const date = await screen.findByLabelText("Date et heure d'envoi");
  envoyer();

  await waitFor(() => expect(date.getAttribute("aria-invalid")).toBe("true"));
  await waitFor(() => expect(document.activeElement).toBe(date));
  expect(document.getElementById(date.getAttribute("aria-describedby")!)?.textContent).toBe("La date et l'heure d'envoi ne peuvent pas être dans le futur.");
  fireEvent.change(date, { target: { value: "2026-11-18T10:00" } });
  expect(date.getAttribute("aria-invalid")).toBeNull();
});

test("une offre qui a déjà sa candidature, dite par l'API, mène à elle, sans formulaire", async () => {
  const { ouvrirLaCandidature, revenir } = afficher(clientSimule({ postuler: async () => ({ issue: "dejaPostulee", candidature: 9 }) }));

  await screen.findByLabelText("Date et heure d'envoi");
  envoyer();

  const signal = await screen.findByRole("alert");
  expect(within(signal).getByText("Cette offre a déjà sa candidature : rien n'a été ajouté.")).toBeDefined();
  expect(screen.queryByLabelText("Date et heure d'envoi")).toBeNull();
  fireEvent.click(within(signal).getByRole("button", { name: "Voir la candidature" }));
  expect(ouvrirLaCandidature).toHaveBeenCalledWith(9);
  fireEvent.click(within(signal).getByRole("button", { name: "Annuler" }));
  expect(revenir).toHaveBeenCalledOnce();
});

test("une offre déjà postulée à l'ouverture le dit d'emblée, sans formulaire", async () => {
  const { ouvrirLaCandidature, client } = afficher(
    clientSimule({ lireUneOffre: async () => offreFictive({ candidature: candidatureDeLOffreFictive({ id: 5 }) }) }),
  );

  const signal = await screen.findByRole("alert");
  expect(within(signal).getByText("Cette offre a déjà sa candidature : rien n'a été ajouté.")).toBeDefined();
  expect(screen.queryByLabelText("Date et heure d'envoi")).toBeNull();
  fireEvent.click(within(signal).getByRole("button", { name: "Voir la candidature" }));
  expect(ouvrirLaCandidature).toHaveBeenCalledWith(5);
  expect(client.postuler).not.toHaveBeenCalled();
});

// L'offre sans entreprise : le message, et le bouton qui mène à sa correction.
async function attendreLOffreSansEntreprise(indiquerLEntreprise: ReturnType<typeof vi.fn>) {
  const signal = await screen.findByRole("alert");
  expect(within(signal).getByText("Indiquez d'abord l'entreprise de l'offre : la candidature la reprend.")).toBeDefined();
  expect(screen.queryByLabelText("Date et heure d'envoi")).toBeNull();
  fireEvent.click(within(signal).getByRole("button", { name: "Indiquer l'entreprise" }));
  expect(indiquerLEntreprise).toHaveBeenCalledOnce();
}

test("une offre sans entreprise à l'ouverture mène à sa correction, sans formulaire", async () => {
  const { indiquerLEntreprise, client } = afficher(clientSimule({ lireUneOffre: async () => offreFictive({ entreprise: null }) }));

  await attendreLOffreSansEntreprise(indiquerLEntreprise);
  expect(client.postuler).not.toHaveBeenCalled();
});

test("une offre sans entreprise, dite par l'API, mène à sa correction", async () => {
  const { indiquerLEntreprise } = afficher(clientSimule({ postuler: async () => ({ issue: "entrepriseManquante" }) }));

  await screen.findByLabelText("Date et heure d'envoi");
  envoyer();

  await attendreLOffreSansEntreprise(indiquerLEntreprise);
});

test("un poste d'offre à revoir le dit, sans rien enregistrer", async () => {
  const { quandEnregistree } = afficher(clientSimule({ postuler: async () => ({ issue: "posteARevoir" }) }));

  await screen.findByLabelText("Date et heure d'envoi");
  envoyer();

  expect((await screen.findByRole("alert")).textContent).toBe("Le poste de l'offre ne peut pas être repris tel quel : corrigez l'offre, puis postulez.");
  expect(quandEnregistree).not.toHaveBeenCalled();
});

test("un doublon possible déjà postulé se montre avant l'envoi, sans bloquer, et les textes piégés restent du texte", async () => {
  const { quandEnregistree } = afficher(
    clientSimule({
      lireUneOffre: async () =>
        offreFictive({
          poste: PIEGE,
          lieu: PIEGE,
          entreprise: { id: 1, nom: PIEGE },
          doublonsPossibles: [
            offreSignaleeFictive({ id: 2, poste: PIEGE, entreprise: { id: 1, nom: PIEGE }, candidature: candidatureDeLOffreFictive({ statut: "Entretien" }) }),
            offreSignaleeFictive({ id: 4, poste: "Sans candidature" }),
          ],
        }),
    }),
  );

  const semblables = await screen.findByRole("region", { name: "Vous avez déjà postulé à une offre semblable :" });
  expect(within(semblables).getAllByRole("listitem").map((item) => item.textContent)).toEqual([
    `${PIEGE}, chez ${PIEGE} (Namur), ajoutée le 10/11/2026 09:00 ; déjà postulée : Entretien`,
  ]);
  expect(document.querySelector("img, script, b")).toBeNull();
  expect(screen.getAllByText(PIEGE).length).toBe(3);

  envoyer();
  await waitFor(() => expect(quandEnregistree).toHaveBeenCalledOnce());
});

test("deux clics rapprochés n'envoient qu'une fois", async () => {
  let repondre: (resultat: ResultatDeLaPostulation) => void = () => {};
  const postuler = vi.fn<ClientDeLApi["postuler"]>(() => new Promise((resoudre) => (repondre = resoudre)));
  const { quandEnregistree } = afficher(clientSimule({ postuler }));

  await screen.findByLabelText("Date et heure d'envoi");
  const bouton = screen.getByRole("button", { name: "Enregistrer la candidature" });
  act(() => {
    bouton.click();
    bouton.click();
  });
  expect(screen.getByRole("button", { name: "Enregistrement…" })).toBeDefined();
  await act(async () => repondre({ issue: "enregistree", candidature: candidatureFictive({ id: 5 }) }));

  expect(postuler).toHaveBeenCalledOnce();
  expect(quandEnregistree).toHaveBeenCalledOnce();
});

test("un envoi qui échoue le dit, et une offre disparue aussi", async () => {
  afficher(clientSimule({ postuler: async () => Promise.reject(new Error("panne fictive")) }));
  await screen.findByLabelText("Date et heure d'envoi");
  envoyer();
  expect((await screen.findByRole("alert")).textContent).toBe(
    "La candidature n'a pas pu être enregistrée. Réessayez ; si l'erreur revient, fermez Sollicito, puis relancez-le.",
  );
});

test("une offre introuvable, à l'ouverture ou à l'envoi, le dit à la place du formulaire", async () => {
  afficher(clientSimule({ postuler: async () => Promise.reject(new ErreurDeLApi(404)) }));
  await screen.findByLabelText("Date et heure d'envoi");
  envoyer();
  expect((await screen.findByRole("alert")).textContent).toBe("Cette offre n'existe pas, ou plus.");
  expect(screen.queryByLabelText("Date et heure d'envoi")).toBeNull();
});
