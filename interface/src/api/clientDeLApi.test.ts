import { expect, test, vi } from "vitest";
import { JETON_FICTIF } from "../tests/canalSimule.ts";
import { candidatureFictive, ficheFictive } from "../tests/clientSimule.ts";
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

test("la fiche se lit par l'identifiant de la candidature, seule donnée de son adresse", async () => {
  const envoyer = apiQuiRepond(Response.json(ficheFictive({ id: 7 })));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.lireLaFiche(7)).toEqual(ficheFictive({ id: 7 }));
  expect(envoyer.mock.calls[0]![0]).toBe("/api/candidatures/7");
  expect(new Headers(envoyer.mock.calls[0]![1]?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);

  await expect(creerClientDeLApi(JETON_FICTIF, apiQuiRepond(new Response(null, { status: 404 }))).lireLaFiche(7)).rejects.toEqual(
    new ErreurDeLApi(404),
  );
  // Un identifiant qui n'est pas un entier positif n'entre pas dans une adresse.
  for (const invalide of [0, -1, 1.5, Number.NaN]) {
    await expect(client.lireLaFiche(invalide)).rejects.toThrow(RangeError);
  }
  expect(envoyer).toHaveBeenCalledOnce();
});

test("un changement de statut part en POST, en JSON, et rend la fiche à jour ou le motif du refus", async () => {
  const envoyer = apiQuiRepond(Response.json(ficheFictive({ statut: "Entretien" })));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.changerLeStatut(7, "Entretien")).toEqual({ faite: true, fiche: ficheFictive({ statut: "Entretien" }) });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/7/statut");
  expect(options?.method).toBe("POST");
  expect(JSON.parse(String(options?.body))).toEqual({ statut: "Entretien" });
  expect(new Headers(options?.headers).get("Content-Type")).toBe("application/json");
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
  expect(options?.credentials).toBe("omit");

  const refus = apiQuiRepond(
    Response.json(
      { title: "Le statut n'a pas changé.", detail: "La candidature est déjà au statut Entretien.", status: 409 },
      { status: 409 },
    ),
  );
  expect(await creerClientDeLApi(JETON_FICTIF, refus).changerLeStatut(7, "Entretien")).toEqual({
    faite: false,
    motif: "La candidature est déjà au statut Entretien.",
  });
});

test("une correction part en POST, sans corps, vers le changement qu'elle corrige", async () => {
  const envoyer = apiQuiRepond(Response.json(ficheFictive()));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).corrigerLeDernierChangement(7, 12)).toEqual({ faite: true, fiche: ficheFictive() });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/7/changements/12/correction");
  expect(options?.method).toBe("POST");
  expect(options?.body).toBeUndefined();
  expect(new Headers(options?.headers).get("Content-Type")).toBeNull();
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);

  const sansDetail = apiQuiRepond(Response.json({ title: "Le changement de statut n'est pas corrigé.", status: 409 }, { status: 409 }));
  expect(await creerClientDeLApi(JETON_FICTIF, sansDetail).corrigerLeDernierChangement(7, 12)).toEqual({
    faite: false,
    motif: "Le changement de statut n'est pas corrigé.",
  });
});

test("une action refusée sans motif lisible, ou en erreur, est signalée comme une erreur de l'API", async () => {
  const sansCorps = apiQuiRepond(new Response("pas du JSON", { status: 409 }));
  const introuvable = apiQuiRepond(Response.json({ title: "Cette candidature n'existe pas.", status: 404 }, { status: 404 }));
  const illisible = apiQuiRepond(Response.json({ title: "La requête est illisible.", status: 400 }, { status: 400 }));

  await expect(creerClientDeLApi(JETON_FICTIF, sansCorps).changerLeStatut(7, "Refusee")).rejects.toEqual(new ErreurDeLApi(409));
  await expect(creerClientDeLApi(JETON_FICTIF, introuvable).corrigerLeDernierChangement(7, 1)).rejects.toEqual(new ErreurDeLApi(404));
  await expect(creerClientDeLApi(JETON_FICTIF, illisible).changerLeStatut(7, "Refusee")).rejects.toEqual(new ErreurDeLApi(400));
});

test("une étape part telle que saisie, et ses refus reviennent champ par champ", async () => {
  const saisie = { type: "Entretien" as const, survenuLe: "2026-11-20T10:00", note: "Premier entretien." };
  const envoyer = apiQuiRepond(Response.json(ficheFictive()));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).noterUneEtape(7, saisie)).toEqual({ notee: true, fiche: ficheFictive() });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/7/etapes");
  expect(options?.method).toBe("POST");
  expect(JSON.parse(String(options?.body))).toEqual(saisie);

  const probleme = {
    title: "L'étape n'est pas ajoutée à l'historique : corrigez les champs signalés.",
    status: 400,
    errors: { survenuLe: ["Indiquez la date et l'heure."], poste: ["ignoré"] },
  };
  const refus = apiQuiRepond(Response.json(probleme, { status: 400 }));
  expect(await creerClientDeLApi(JETON_FICTIF, refus).noterUneEtape(7, saisie)).toEqual({
    notee: false,
    refus: { survenuLe: ["Indiquez la date et l'heure."] },
  });

  const sansChamp = apiQuiRepond(Response.json({ title: "La requête est illisible.", status: 400 }, { status: 400 }));
  await expect(creerClientDeLApi(JETON_FICTIF, sansChamp).noterUneEtape(7, saisie)).rejects.toEqual(new ErreurDeLApi(400));
});
