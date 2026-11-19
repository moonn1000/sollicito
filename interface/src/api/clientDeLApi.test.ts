import { expect, test, vi } from "vitest";
import { JETON_FICTIF } from "../tests/canalSimule.ts";
import { candidatureFictive } from "../tests/clientSimule.ts";
import { creerClientDeLApi, EN_TETE_DU_JETON, ErreurDeLApi } from "./clientDeLApi.ts";

const SAISIE = { entreprise: "Lumière Verte SA", poste: "Comptable", envoyeeLe: "2026-03-27T23:30" };

function apiQuiRepond(reponse: Response) {
  return vi.fn<typeof fetch>(async () => reponse);
}

test("un enregistrement part en POST, en JSON, avec le jeton dans son en-tête et sans cookie", async () => {
  const envoyer = apiQuiRepond(Response.json(candidatureFictive(), { status: 201 }));

  const resultat = await creerClientDeLApi(JETON_FICTIF, envoyer).enregistrerUneCandidature(SAISIE);

  expect(resultat).toEqual({ enregistree: true, candidature: candidatureFictive() });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures");
  expect(options?.method).toBe("POST");
  expect(JSON.parse(String(options?.body))).toEqual(SAISIE);
  const enTetes = new Headers(options?.headers);
  expect(enTetes.get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
  expect(enTetes.get("Content-Type")).toBe("application/json");
  expect(options?.credentials).toBe("omit");
  expect(options?.cache).toBe("no-store");
});

test("les refus de la validation reviennent champ par champ", async () => {
  const probleme = {
    title: "La candidature n'est pas enregistrée : corrigez les champs signalés.",
    status: 400,
    errors: { poste: ["Indiquez le poste."], inconnu: ["ignoré"], envoyeeLe: ["La date et l'heure d'envoi ne peuvent pas être dans le futur."] },
  };
  const envoyer = apiQuiRepond(Response.json(probleme, { status: 400, headers: { "Content-Type": "application/problem+json" } }));

  const resultat = await creerClientDeLApi(JETON_FICTIF, envoyer).enregistrerUneCandidature(SAISIE);

  expect(resultat).toEqual({
    enregistree: false,
    refus: { poste: ["Indiquez le poste."], envoyeeLe: ["La date et l'heure d'envoi ne peuvent pas être dans le futur."] },
  });
});

test("un refus sans détail des champs, ou une autre erreur, est signalé comme une erreur de l'API", async () => {
  const sansDetail = apiQuiRepond(Response.json({ title: "La requête est illisible.", status: 400 }, { status: 400 }));
  const enPanne = apiQuiRepond(new Response(null, { status: 500 }));
  const champInconnu = apiQuiRepond(Response.json({ status: 400, errors: { contact: ["Indiquez le contact."] } }, { status: 400 }));

  await expect(creerClientDeLApi(JETON_FICTIF, sansDetail).enregistrerUneCandidature(SAISIE)).rejects.toEqual(new ErreurDeLApi(400));
  await expect(creerClientDeLApi(JETON_FICTIF, champInconnu).enregistrerUneCandidature(SAISIE)).rejects.toEqual(new ErreurDeLApi(400));
  await expect(creerClientDeLApi(JETON_FICTIF, enPanne).enregistrerUneCandidature(SAISIE)).rejects.toEqual(new ErreurDeLApi(500));
});

test("les listes se lisent avec le jeton, et une liste refusée est signalée", async () => {
  const envoyer = apiQuiRepond(Response.json([candidatureFictive()]));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.listerLesCandidatures()).toEqual([candidatureFictive()]);
  expect(envoyer.mock.calls[0]![0]).toBe("/api/candidatures");
  expect(new Headers(envoyer.mock.calls[0]![1]?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);

  await expect(creerClientDeLApi(JETON_FICTIF, apiQuiRepond(new Response(null, { status: 401 }))).listerLesEntreprises()).rejects.toEqual(
    new ErreurDeLApi(401),
  );
});
