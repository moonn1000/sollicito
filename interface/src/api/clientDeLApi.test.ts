import { expect, test, vi } from "vitest";
import { JETON_FICTIF } from "../tests/canalSimule.ts";
import {
  candidatureDeLOffreFictive,
  candidatureFictive,
  contactFictif,
  ficheFictive,
  offreFictive,
  offreListeeFictive,
  offreSignaleeFictive,
  relanceFictive,
  relanceSignaleeFictive,
  sauvegardesFictives,
} from "../tests/clientSimule.ts";
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

test("une modification part en PUT, en JSON, vers la fiche, et rend la fiche à jour ou les refus de ses champs", async () => {
  const envoyer = apiQuiRepond(Response.json(ficheFictive({ id: 7, poste: "Comptable senior" })));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).modifierUneCandidature(7, SAISIE)).toEqual({
    issue: "modifiee",
    fiche: ficheFictive({ id: 7, poste: "Comptable senior" }),
  });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/7");
  expect(options?.method).toBe("PUT");
  expect(JSON.parse(String(options?.body))).toEqual(SAISIE);
  expect(new Headers(options?.headers).get("Content-Type")).toBe("application/json");
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
  expect(options?.credentials).toBe("omit");

  const probleme = {
    title: "La candidature n'est pas modifiée : corrigez les champs signalés.",
    status: 400,
    errors: { envoyeeLe: ["La date et l'heure d'envoi ne peuvent pas suivre le premier événement de l'historique : l'historique commence à l'envoi."], statut: ["ignoré"] },
  };
  expect(await creerClientDeLApi(JETON_FICTIF, apiQuiRepond(Response.json(probleme, { status: 400 }))).modifierUneCandidature(7, SAISIE)).toEqual({
    issue: "refusee",
    refus: { envoyeeLe: ["La date et l'heure d'envoi ne peuvent pas suivre le premier événement de l'historique : l'historique commence à l'envoi."] },
  });

  const illisible = apiQuiRepond(Response.json({ title: "La requête est illisible.", status: 400 }, { status: 400 }));
  const introuvable = apiQuiRepond(Response.json({ title: "Cette candidature n'existe pas.", status: 404 }, { status: 404 }));
  await expect(creerClientDeLApi(JETON_FICTIF, illisible).modifierUneCandidature(7, SAISIE)).rejects.toEqual(new ErreurDeLApi(400));
  await expect(creerClientDeLApi(JETON_FICTIF, introuvable).modifierUneCandidature(7, SAISIE)).rejects.toEqual(new ErreurDeLApi(404));
});

test("une suppression part en DELETE, sans corps, vers la fiche ; toute autre réponse que 204 est une erreur", async () => {
  const envoyer = apiQuiRepond(new Response(null, { status: 204 }));

  await creerClientDeLApi(JETON_FICTIF, envoyer).supprimerUneCandidature(7);

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/7");
  expect(options?.method).toBe("DELETE");
  expect(options?.body).toBeUndefined();
  expect(new Headers(options?.headers).get("Content-Type")).toBeNull();
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
  expect(options?.credentials).toBe("omit");

  const introuvable = apiQuiRepond(Response.json({ title: "Cette candidature n'existe pas.", status: 404 }, { status: 404 }));
  await expect(creerClientDeLApi(JETON_FICTIF, introuvable).supprimerUneCandidature(7)).rejects.toEqual(new ErreurDeLApi(404));
  await expect(creerClientDeLApi(JETON_FICTIF, apiQuiRepond(new Response(null, { status: 200 }))).supprimerUneCandidature(7)).rejects.toEqual(
    new ErreurDeLApi(200),
  );
  // Un identifiant qui n'est pas un entier positif n'entre pas dans une adresse.
  await expect(creerClientDeLApi(JETON_FICTIF, envoyer).supprimerUneCandidature(0)).rejects.toThrow(RangeError);
  expect(envoyer).toHaveBeenCalledOnce();
});

test("une correction d'étape part en POST, en JSON, vers l'étape qu'elle corrige, et rend la fiche, les refus ou le motif", async () => {
  const saisie = { type: "Entretien" as const, survenuLe: "2026-11-11T10:00", note: "Premier entretien." };
  const envoyer = apiQuiRepond(Response.json(ficheFictive()));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).corrigerUneEtape(7, 12, saisie)).toEqual({ issue: "faite", fiche: ficheFictive() });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/7/etapes/12/correction");
  expect(options?.method).toBe("POST");
  expect(JSON.parse(String(options?.body))).toEqual(saisie);
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);

  const refus = apiQuiRepond(Response.json({ status: 400, errors: { note: ["La note ne doit pas dépasser 2000 caractères."], poste: ["ignoré"] } }, { status: 400 }));
  expect(await creerClientDeLApi(JETON_FICTIF, refus).corrigerUneEtape(7, 12, saisie)).toEqual({
    issue: "refusee",
    refus: { note: ["La note ne doit pas dépasser 2000 caractères."] },
  });

  const perimee = apiQuiRepond(Response.json({ title: "L'étape n'est pas corrigée.", detail: "Cette étape n'est pas la version en vigueur.", status: 409 }, { status: 409 }));
  expect(await creerClientDeLApi(JETON_FICTIF, perimee).corrigerUneEtape(7, 12, saisie)).toEqual({
    issue: "perimee",
    motif: "Cette étape n'est pas la version en vigueur.",
  });

  const illisible = apiQuiRepond(Response.json({ title: "La requête est illisible.", status: 400 }, { status: 400 }));
  const introuvable = apiQuiRepond(Response.json({ title: "Cette candidature n'existe pas.", status: 404 }, { status: 404 }));
  await expect(creerClientDeLApi(JETON_FICTIF, illisible).corrigerUneEtape(7, 12, saisie)).rejects.toEqual(new ErreurDeLApi(400));
  await expect(creerClientDeLApi(JETON_FICTIF, introuvable).corrigerUneEtape(7, 12, saisie)).rejects.toEqual(new ErreurDeLApi(404));
  await expect(creerClientDeLApi(JETON_FICTIF, envoyer).corrigerUneEtape(7, 0, saisie)).rejects.toThrow(RangeError);
});

test("sans accord, un nom à corriger revient avec l'entreprise et le nombre de candidatures et d'offres ; l'accord part dans le corps", async () => {
  const probleme = {
    title: "La candidature n'est pas modifiée : ce nom corrigerait celui d'une entreprise enregistrée.",
    status: 409,
    entreprise: { id: 4, nom: "Lumière Verte SA" },
    candidatures: 3,
    offres: 2,
  };
  const refus = apiQuiRepond(Response.json(probleme, { status: 409 }));

  expect(await creerClientDeLApi(JETON_FICTIF, refus).modifierUneCandidature(7, SAISIE)).toEqual({
    issue: "nomACorriger",
    entreprise: { id: 4, nom: "Lumière Verte SA" },
    candidatures: 3,
    offres: 2,
  });
  // Sans le nombre d'offres, la réponse ne se lit pas : c'est une erreur.
  const sansOffres = apiQuiRepond(Response.json({ ...probleme, offres: undefined }, { status: 409 }));
  await expect(creerClientDeLApi(JETON_FICTIF, sansOffres).modifierUneCandidature(7, SAISIE)).rejects.toEqual(new ErreurDeLApi(409));
  expect(JSON.parse(String(refus.mock.calls[0]![1]?.body))).toEqual(SAISIE);

  const envoyer = apiQuiRepond(Response.json(ficheFictive()));
  await creerClientDeLApi(JETON_FICTIF, envoyer).modifierUneCandidature(7, SAISIE, true);
  expect(JSON.parse(String(envoyer.mock.calls[0]![1]?.body))).toEqual({ ...SAISIE, corrigerLeNomDeLEntreprise: true });

  // Un 409 qui ne porte pas l'entreprise et son nombre, ou les porte mal formés, est une erreur.
  for (const mal of [{ status: 409 }, { entreprise: { id: "4", nom: "X" }, candidatures: 3 }, { entreprise: { id: 4, nom: "X" }, candidatures: 0 }]) {
    await expect(creerClientDeLApi(JETON_FICTIF, apiQuiRepond(Response.json(mal, { status: 409 }))).modifierUneCandidature(7, SAISIE)).rejects.toEqual(
      new ErreurDeLApi(409),
    );
  }
});

test("l'état des sauvegardes se lit en GET, avec le jeton", async () => {
  const etat = sauvegardesFictives();
  const envoyer = apiQuiRepond(Response.json(etat));

  await expect(creerClientDeLApi(JETON_FICTIF, envoyer).lireLesSauvegardes()).resolves.toEqual(etat);

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/sauvegardes");
  expect(options?.method).toBeUndefined();
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
});

test("« Sauvegarder maintenant » part en POST, sans corps, et rend l'état à jour", async () => {
  const etat = sauvegardesFictives({ echec: { motif: "DossierIntrouvable", depuis: "2026-11-18T13:30:00Z" } });
  const envoyer = apiQuiRepond(Response.json(etat));

  await expect(creerClientDeLApi(JETON_FICTIF, envoyer).sauvegarderMaintenant()).resolves.toEqual(etat);

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/sauvegardes");
  expect(options?.method).toBe("POST");
  expect(options?.body).toBeUndefined();
});

test("le retour au dossier par défaut rend l'état à jour, ou le motif d'un refus ; aucun chemin ne part", async () => {
  const fait = apiQuiRepond(Response.json(sauvegardesFictives()));
  const refuse = apiQuiRepond(
    Response.json({ title: "Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie.", status: 409 }, { status: 409 }),
  );
  const enPanne = apiQuiRepond(new Response(null, { status: 500 }));

  await expect(creerClientDeLApi(JETON_FICTIF, fait).revenirAuDossierParDefaut()).resolves.toEqual({ issue: "fait", etat: sauvegardesFictives() });
  await expect(creerClientDeLApi(JETON_FICTIF, refuse).revenirAuDossierParDefaut()).resolves.toEqual({
    issue: "refuse",
    motif: "Le dossier des sauvegardes n'a pas changé : Sollicito n'a pas pu y faire de copie.",
  });
  await expect(creerClientDeLApi(JETON_FICTIF, enPanne).revenirAuDossierParDefaut()).rejects.toEqual(new ErreurDeLApi(500));
  const [adresse, options] = fait.mock.calls[0]!;
  expect(adresse).toBe("/api/sauvegardes/dossier-par-defaut");
  expect(options?.method).toBe("POST");
  expect(options?.body).toBeUndefined();
});

test("les contacts se lisent, s'ajoutent, se modifient et se suppriment par leurs seuls identifiants (#14)", async () => {
  const envoyer = vi.fn<typeof fetch>(async (_adresse, options) => {
    if (options?.method === "DELETE") {
      return new Response(null, { status: 204 });
    }

    if (options?.method === "POST") {
      return Response.json(contactFictif(), { status: 201 });
    }

    return Response.json(options?.method === "PUT" ? contactFictif() : [contactFictif()]);
  });
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.listerLesContacts(1)).toEqual([contactFictif()]);
  expect(await client.ajouterUnContact(1, { nom: "Jeanne Martin", email: "jeanne.martin@exemple.invalid" })).toEqual({ issue: "enregistre", contact: contactFictif() });
  expect(await client.modifierUnContact(4, { nom: "Jeanne Martin", email: "jeanne.martin@exemple.invalid" })).toEqual({ issue: "enregistre", contact: contactFictif() });
  await client.supprimerUnContact(4);

  expect(envoyer.mock.calls.map(([adresse, options]) => [adresse, options?.method ?? "GET"])).toEqual([
    ["/api/entreprises/1/contacts", "GET"],
    ["/api/entreprises/1/contacts", "POST"],
    ["/api/contacts/4", "PUT"],
    ["/api/contacts/4", "DELETE"],
  ]);
  expect(JSON.parse(String(envoyer.mock.calls[1]![1]?.body))).toEqual({ nom: "Jeanne Martin", email: "jeanne.martin@exemple.invalid" });
  await expect(client.listerLesContacts(0)).rejects.toThrow(RangeError);
});

test("les refus d'un contact reviennent champ par champ ; une autre erreur est signalée", async () => {
  const refuse = apiQuiRepond(Response.json({ status: 400, errors: { email: ["Indiquez l'adresse électronique."], autre: ["ignoré"] } }, { status: 400 }));
  const introuvable = apiQuiRepond(Response.json({ status: 404, title: "Ce contact n'existe pas." }, { status: 404 }));

  expect(await creerClientDeLApi(JETON_FICTIF, refuse).ajouterUnContact(1, { nom: "Jeanne", email: "" })).toEqual({
    issue: "refuse",
    refus: { email: ["Indiquez l'adresse électronique."] },
  });
  await expect(creerClientDeLApi(JETON_FICTIF, introuvable).modifierUnContact(4, { nom: "Jeanne", email: "j@x.be" })).rejects.toEqual(new ErreurDeLApi(404));
});

test("une relance se planifie, se change et se clôt par les routes de sa candidature, et rend la fiche, les refus ou le motif", async () => {
  const fiche = ficheFictive({ relances: [relanceFictive()] });
  const envoyer = vi.fn<typeof fetch>(async () => Response.json(fiche));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.planifierUneRelance(1, { delai: 7, destinataire: 4 })).toEqual({ issue: "faite", fiche });
  expect(await client.changerLaRelance(1, 7, { delai: 10, destinataire: null })).toEqual({ issue: "faite", fiche });
  expect(await client.marquerLaRelanceFaite(1, 7)).toEqual({ faite: true, fiche });
  expect(await client.abandonnerLaRelance(1, 7)).toEqual({ faite: true, fiche });

  expect(envoyer.mock.calls.map(([adresse, options]) => [adresse, options?.method, options?.body ?? null])).toEqual([
    ["/api/candidatures/1/relances", "POST", JSON.stringify({ delai: 7, destinataire: 4 })],
    ["/api/candidatures/1/relances/7", "PUT", JSON.stringify({ delai: 10, destinataire: null })],
    ["/api/candidatures/1/relances/7/faite", "POST", null],
    ["/api/candidatures/1/relances/7/abandon", "POST", null],
  ]);

  const refusee = apiQuiRepond(Response.json({ status: 400, errors: { delai: ["Le délai d'une relance compte de 1 à 365 jours."] } }, { status: 400 }));
  expect(await creerClientDeLApi(JETON_FICTIF, refusee).planifierUneRelance(1, { delai: 0, destinataire: null })).toEqual({
    issue: "refusee",
    refus: { delai: ["Le délai d'une relance compte de 1 à 365 jours."] },
  });
  const nonPermise = apiQuiRepond(
    Response.json({ status: 409, title: "La relance n'est pas planifiée.", detail: "La candidature est close : elle ne reçoit plus de relance." }, { status: 409 }),
  );
  expect(await creerClientDeLApi(JETON_FICTIF, nonPermise).planifierUneRelance(1, { delai: 7, destinataire: null })).toEqual({
    issue: "nonPermise",
    motif: "La candidature est close : elle ne reçoit plus de relance.",
  });
});

test("les relances du tableau de bord et le délai par défaut se lisent ; le délai se change en PUT", async () => {
  const relances = { aujourdhui: "2026-11-18", echues: [relanceSignaleeFictive()], deLaSemaine: [] };
  const reglage = { delaiParDefaut: 10, delaiLisible: true, fichierLisible: true };
  const envoyer = vi.fn<typeof fetch>(async (adresse) => Response.json(adresse === "/api/relances" ? relances : reglage));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.listerLesRelances()).toEqual(relances);
  expect(await client.lireLeReglageDesRelances()).toEqual(reglage);
  expect(await client.changerLeDelaiParDefaut(10)).toEqual({ issue: "change", reglage });
  expect(envoyer.mock.calls[2]![0]).toBe("/api/reglages/relances");
  expect(envoyer.mock.calls[2]![1]?.method).toBe("PUT");
  expect(JSON.parse(String(envoyer.mock.calls[2]![1]?.body))).toEqual({ delaiParDefaut: 10 });

  const refuse = apiQuiRepond(Response.json({ status: 400, errors: { delaiParDefaut: ["Le délai d'une relance compte de 1 à 365 jours."] } }, { status: 400 }));
  expect(await creerClientDeLApi(JETON_FICTIF, refuse).changerLeDelaiParDefaut(0)).toEqual({ issue: "refuse", refus: ["Le délai d'une relance compte de 1 à 365 jours."] });
  const illisible = apiQuiRepond(Response.json({ status: 409, title: "Le délai par défaut n'a pas changé : les réglages n'ont pas pu être relus." }, { status: 409 }));
  expect(await creerClientDeLApi(JETON_FICTIF, illisible).changerLeDelaiParDefaut(10)).toEqual({
    issue: "impossible",
    motif: "Le délai par défaut n'a pas changé : les réglages n'ont pas pu être relus.",
  });
});

test("l'aperçu et l'import d'un fichier CSV envoient ses seuls octets, en base64, et l'import ses lignes gardées (#40)", async () => {
  const envoyer = vi.fn<typeof fetch>(async () => Response.json({ lignes: [], comptes: {} }, { status: 200 }));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);
  const octets = new Uint8Array([0xef, 0xbb, 0xbf, ...new TextEncoder().encode("poste\nDéveloppeur\n"), 0xff]);

  expect((await client.examinerUnFichierCsv(octets.buffer)).issue).toBe("lu");
  expect((await client.importerUnFichierCsv(octets.buffer, [4, 9])).issue).toBe("lu");

  const [[cheminDeLApercu, apercu], [cheminDeLImport, importer]] = envoyer.mock.calls;
  expect(cheminDeLApercu).toBe("/api/offres/import/apercu");
  expect(cheminDeLImport).toBe("/api/offres/import");
  expect(apercu?.method).toBe("POST");
  const corpsDeLApercu = JSON.parse(apercu!.body as string) as Record<string, unknown>;
  expect(Object.keys(corpsDeLApercu)).toEqual(["contenu"]);
  expect(Uint8Array.from(atob(corpsDeLApercu.contenu as string), (caractere) => caractere.charCodeAt(0))).toEqual(octets);
  expect(JSON.parse(importer!.body as string)).toEqual({ contenu: corpsDeLApercu.contenu, garder: [4, 9] });
});

test("un fichier de 2 Mo passe en base64 sans dépasser la pile", async () => {
  const envoyer = vi.fn<typeof fetch>(async () => Response.json({ lignes: [], comptes: {} }, { status: 200 }));
  const octets = new Uint8Array(2 * 1024 * 1024).map((_, i) => i % 251);

  await creerClientDeLApi(JETON_FICTIF, envoyer).examinerUnFichierCsv(octets.buffer);

  const { contenu } = JSON.parse(envoyer.mock.calls[0][1]!.body as string) as { contenu: string };
  expect(contenu.length).toBe(Math.ceil(octets.length / 3) * 4);
  expect(contenu.slice(0, 8)).toBe(btoa(String.fromCharCode(...octets.subarray(0, 6))));
});

test("un fichier refusé rend son motif ; un corps trop gros, celui de la taille ; une copie d'avant manquée, le sien ; une autre réponse lève une erreur (#40)", async () => {
  const refuse = Response.json({ errors: { contenu: ["Le fichier est vide."] } }, { status: 400 });
  expect(await creerClientDeLApi(JETON_FICTIF, apiQuiRepond(refuse)).examinerUnFichierCsv(new ArrayBuffer(0))).toEqual({
    issue: "refuse",
    motif: "Le fichier est vide.",
  });
  expect(await creerClientDeLApi(JETON_FICTIF, apiQuiRepond(new Response(null, { status: 413 }))).importerUnFichierCsv(new ArrayBuffer(1), [])).toEqual({
    issue: "refuse",
    motif: "Le fichier dépasse 2 Mo : découpez-le en plusieurs fichiers.",
  });
  const copieManquee = Response.json({ title: "L'import n'est pas fait : la copie de la base, avant lui, n'a pas pu se faire.", status: 409 }, { status: 409 });
  expect(await creerClientDeLApi(JETON_FICTIF, apiQuiRepond(copieManquee)).importerUnFichierCsv(new ArrayBuffer(1), [])).toEqual({
    issue: "refuse",
    motif: "Sollicito n'a pas pu copier votre base avant l'import : vérifiez la place libre sur le disque, puis réessayez.",
  });
  const accordRefuse = Response.json({ errors: { garder: ["Les lignes gardées doivent être des lignes d'offres du fichier, chacune une fois."] } }, { status: 400 });
  await expect(creerClientDeLApi(JETON_FICTIF, apiQuiRepond(accordRefuse)).importerUnFichierCsv(new ArrayBuffer(1), [99])).rejects.toBeInstanceOf(ErreurDeLApi);
  await expect(creerClientDeLApi(JETON_FICTIF, apiQuiRepond(new Response(null, { status: 503 }))).examinerUnFichierCsv(new ArrayBuffer(1))).rejects.toBeInstanceOf(ErreurDeLApi);
});

const OFFRE = { texte: "Offre fictive.", entreprise: "Lumière Verte SA", poste: "Comptable", lieu: "", publieeLe: "", adresse: "" };

test("une offre se colle en POST, l'accord ne part qu'avec garderLesDeux, et sa fiche se lit par son seul identifiant (#36)", async () => {
  // Une réponse neuve à chaque appel : un corps ne se lit qu'une fois.
  const envoyer = vi.fn<typeof fetch>(async () => Response.json(offreFictive(), { status: 201 }));
  const client = creerClientDeLApi(JETON_FICTIF, envoyer);

  expect(await client.collerUneOffre(OFFRE)).toEqual({ issue: "enregistree", fiche: offreFictive() });
  await client.collerUneOffre(OFFRE, true);

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/offres");
  expect(options?.method).toBe("POST");
  expect(JSON.parse(String(options?.body))).toEqual(OFFRE);
  expect(JSON.parse(String(envoyer.mock.calls[1]![1]?.body))).toEqual({ ...OFFRE, garderLesDeux: true });

  const lecture = apiQuiRepond(Response.json(offreFictive()));
  expect(await creerClientDeLApi(JETON_FICTIF, lecture).lireUneOffre(3)).toEqual(offreFictive());
  expect(lecture.mock.calls[0]![0]).toBe("/api/offres/3");
  await expect(creerClientDeLApi(JETON_FICTIF, lecture).lireUneOffre(0)).rejects.toBeInstanceOf(RangeError);
});

test("la correction d'une offre part en PUT, sans texte, et rend sa fiche", async () => {
  const envoyer = apiQuiRepond(Response.json(offreFictive(), { status: 200 }));
  const { texte: _texte, ...correction } = OFFRE;

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).corrigerUneOffre(3, correction)).toEqual({ issue: "enregistree", fiche: offreFictive() });

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/offres/3");
  expect(options?.method).toBe("PUT");
  expect(JSON.parse(String(options?.body))).toEqual(correction);
});

test("les refus d'une offre reviennent champ par champ, et ses deux conflits se lisent ; un conflit mal formé est une erreur", async () => {
  const refus = apiQuiRepond(Response.json({ status: 400, errors: { texte: ["Collez le texte de l'offre."], inconnu: ["ignoré"] } }, { status: 400 }));
  const deja = apiQuiRepond(Response.json({ status: 409, motif: "dejaEnregistree", offre: { id: 7, ajouteeLe: "2026-11-18T13:30:00Z" } }, { status: 409 }));
  const doublon = apiQuiRepond(Response.json({ status: 409, motif: "doublonPossible", doublons: [offreSignaleeFictive()] }, { status: 409 }));
  const malForme = apiQuiRepond(Response.json({ status: 409, motif: "doublonPossible", doublons: [{ id: "2" }] }, { status: 409 }));
  const inconnu = apiQuiRepond(Response.json({ status: 409, motif: "autre" }, { status: 409 }));

  expect(await creerClientDeLApi(JETON_FICTIF, refus).collerUneOffre(OFFRE)).toEqual({ issue: "refusee", refus: { texte: ["Collez le texte de l'offre."] } });
  expect(await creerClientDeLApi(JETON_FICTIF, deja).collerUneOffre(OFFRE)).toEqual({
    issue: "dejaEnregistree",
    offre: { id: 7, ajouteeLe: "2026-11-18T13:30:00Z" },
  });
  expect(await creerClientDeLApi(JETON_FICTIF, doublon).collerUneOffre(OFFRE)).toEqual({ issue: "doublonPossible", doublons: [offreSignaleeFictive()] });
  await expect(creerClientDeLApi(JETON_FICTIF, malForme).collerUneOffre(OFFRE)).rejects.toEqual(new ErreurDeLApi(409));
  await expect(creerClientDeLApi(JETON_FICTIF, inconnu).collerUneOffre(OFFRE)).rejects.toEqual(new ErreurDeLApi(409));
  // Un corps trop gros (413) dit que le texte est trop long : réessayer ne servirait à rien.
  expect(await creerClientDeLApi(JETON_FICTIF, apiQuiRepond(new Response(null, { status: 413 }))).collerUneOffre(OFFRE)).toEqual({
    issue: "refusee",
    refus: { texte: ["Le texte de l'offre ne doit pas dépasser 50 000 caractères."] },
  });
});

test("un doublon possible déjà postulé garde sa candidature ; une candidature mal formée rend le conflit illisible (#38)", async () => {
  const postulee = offreSignaleeFictive({ candidature: candidatureDeLOffreFictive() });
  const doublon = apiQuiRepond(Response.json({ status: 409, motif: "doublonPossible", doublons: [postulee] }, { status: 409 }));
  const malForme = apiQuiRepond(Response.json({ status: 409, motif: "doublonPossible", doublons: [{ ...postulee, candidature: { id: "5" } }] }, { status: 409 }));

  expect(await creerClientDeLApi(JETON_FICTIF, doublon).collerUneOffre(OFFRE)).toEqual({ issue: "doublonPossible", doublons: [postulee] });
  await expect(creerClientDeLApi(JETON_FICTIF, malForme).collerUneOffre(OFFRE)).rejects.toEqual(new ErreurDeLApi(409));
});

test("la liste des offres se lit en GET, sans aucun paramètre dans son adresse (#38)", async () => {
  const envoyer = apiQuiRepond(Response.json([offreListeeFictive()]));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).listerLesOffres()).toEqual([offreListeeFictive()]);

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/offres");
  expect(options?.method).toBeUndefined();
  expect(options?.body).toBeUndefined();
});

test("« Postuler » part en POST vers l'offre, avec la seule date d'envoi, et rend la candidature (#38)", async () => {
  const envoyer = apiQuiRepond(Response.json(candidatureFictive({ id: 5 }), { status: 201 }));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).postuler(3, "2026-11-18T10:00")).toEqual({ issue: "enregistree", candidature: candidatureFictive({ id: 5 }) });

  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/offres/3/candidature");
  expect(options?.method).toBe("POST");
  expect(JSON.parse(String(options?.body))).toEqual({ envoyeeLe: "2026-11-18T10:00" });
  await expect(creerClientDeLApi(JETON_FICTIF, envoyer).postuler(0, "2026-11-18T10:00")).rejects.toThrow(RangeError);
});

test("les refus de « Postuler » se lisent, et une réponse qui ne se lit pas est une erreur (#38)", async () => {
  const repondre = (corps: unknown, status: number) => creerClientDeLApi(JETON_FICTIF, apiQuiRepond(Response.json(corps, { status }))).postuler(3, "2026-11-18T10:00");

  expect(await repondre({ status: 400, errors: { envoyeeLe: ["Indiquez la date et l'heure d'envoi."] } }, 400)).toEqual({
    issue: "refusee",
    refus: ["Indiquez la date et l'heure d'envoi."],
  });
  expect(await repondre({ status: 409, motif: "dejaPostulee", candidature: 9 }, 409)).toEqual({ issue: "dejaPostulee", candidature: 9 });
  expect(await repondre({ status: 409, motif: "entrepriseManquante" }, 409)).toEqual({ issue: "entrepriseManquante" });
  expect(await repondre({ status: 409, motif: "posteARevoir" }, 409)).toEqual({ issue: "posteARevoir" });
  await expect(repondre({ status: 409, motif: "dejaPostulee", candidature: "9" }, 409)).rejects.toEqual(new ErreurDeLApi(409));
  await expect(repondre({ status: 409, motif: "autre" }, 409)).rejects.toEqual(new ErreurDeLApi(409));
  await expect(repondre({ status: 400, errors: { autre: ["ignoré"] } }, 400)).rejects.toEqual(new ErreurDeLApi(400));
  await expect(repondre({ title: "Cette offre n'existe pas.", status: 404 }, 404)).rejects.toEqual(new ErreurDeLApi(404));
});
