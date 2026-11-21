import { expect, test, vi } from "vitest";
import { JETON_FICTIF } from "../tests/canalSimule.ts";
import { candidatureFictive, ficheFictive, sauvegardesFictives } from "../tests/clientSimule.ts";
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

test("sans accord, un nom à corriger revient avec l'entreprise et le nombre de candidatures ; l'accord part dans le corps", async () => {
  const probleme = {
    title: "La candidature n'est pas modifiée : ce nom corrigerait celui d'une entreprise enregistrée.",
    status: 409,
    entreprise: { id: 4, nom: "Lumière Verte SA" },
    candidatures: 3,
  };
  const refus = apiQuiRepond(Response.json(probleme, { status: 409 }));

  expect(await creerClientDeLApi(JETON_FICTIF, refus).modifierUneCandidature(7, SAISIE)).toEqual({
    issue: "nomACorriger",
    entreprise: { id: 4, nom: "Lumière Verte SA" },
    candidatures: 3,
  });
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
