import { expect, test, vi } from "vitest";
import { JETON_FICTIF } from "../tests/canalSimule.ts";
import { creerClientDeLApi, EN_TETE_DU_JETON, ErreurDeLApi } from "./clientDeLApi.ts";

// Le premier jet d'une relance (#47) et le dépôt de son texte relu, vus du client de l'API.

function apiQuiRepond(reponse: Response) {
  return vi.fn<typeof fetch>(async () => reponse);
}

const TEXTE = "Bonjour Jeanne Martin,\n\nJe me permets de revenir vers vous.\n\nBien cordialement,";

test("le premier jet part en POST sans corps, et rend le texte à relire", async () => {
  const envoyer = apiQuiRepond(Response.json({ reussi: true, texte: TEXTE, echec: null, message: null }));

  const resultat = await creerClientDeLApi(JETON_FICTIF, envoyer).demanderUnPremierJet(3, 7);

  expect(resultat).toEqual({ issue: "redige", texte: TEXTE });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/3/relances/7/premier-jet");
  expect(options?.method).toBe("POST");
  expect(options?.body).toBeUndefined();
  const enTetes = new Headers(options?.headers);
  expect(enTetes.get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
  expect(enTetes.get("Content-Type")).toBeNull();
});

test("un premier jet échoué rend le texte de l'échec, sans rien lire d'autre de la réponse", async () => {
  const message = "Claude n'a pas répondu à temps. Réessayez plus tard.";
  const envoyer = apiQuiRepond(Response.json({ reussi: false, texte: null, echec: "DelaiDepasse", message, autre: "ignoré" }));

  expect(await creerClientDeLApi(JETON_FICTIF, envoyer).demanderUnPremierJet(3, 7)).toEqual({ issue: "echoue", message });
});

test("un premier jet refusé rend son motif, et toute autre réponse est une erreur de l'API", async () => {
  const motif = "Aucune clé n'est enregistrée : enregistrez d'abord votre clé.";
  const refuse = apiQuiRepond(Response.json({ title: "Le premier jet n'est pas rédigé.", detail: motif, status: 409 }, { status: 409 }));
  const sansTexte = apiQuiRepond(Response.json({ reussi: true, texte: "" }));
  const sansMessage = apiQuiRepond(Response.json({ reussi: false }));
  const avecUnCorps = apiQuiRepond(Response.json({ title: "La demande d'un premier jet ne prend aucun corps.", status: 400 }, { status: 400 }));
  const porteFermee = apiQuiRepond(Response.json({ title: "Une restauration de la base est en cours : réessayez dans un instant.", status: 503 }, { status: 503 }));

  expect(await creerClientDeLApi(JETON_FICTIF, refuse).demanderUnPremierJet(3, 7)).toEqual({ issue: "refuse", motif });
  for (const envoyer of [sansTexte, sansMessage, avecUnCorps, porteFermee]) {
    await expect(creerClientDeLApi(JETON_FICTIF, envoyer).demanderUnPremierJet(3, 7)).rejects.toBeInstanceOf(ErreurDeLApi);
  }
});

test("le texte relu part en PUT, en JSON, et son dépôt rend « déposé »", async () => {
  const envoyer = apiQuiRepond(new Response(null, { status: 204 }));

  const resultat = await creerClientDeLApi(JETON_FICTIF, envoyer).deposerLeBrouillonRelu(3, 7, TEXTE);

  expect(resultat).toEqual({ issue: "depose" });
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/candidatures/3/relances/7/brouillon-relu");
  expect(options?.method).toBe("PUT");
  expect(JSON.parse(String(options?.body))).toEqual({ texte: TEXTE });
  expect(new Headers(options?.headers).get("Content-Type")).toBe("application/json");
});

test("un dépôt refusé rend le refus du champ, ou le motif d'une relance qui n'est plus à faire", async () => {
  const champ = apiQuiRepond(
    Response.json(
      {
        title: "Le brouillon n'est pas prêt : corrigez le champ signalé.",
        status: 400,
        errors: { texte: ["Le brouillon ne doit pas dépasser 2000 caractères."] },
      },
      { status: 400 },
    ),
  );
  const motif = "Cette relance n'est plus à faire : elle a déjà été marquée faite ou abandonnée, ou la fiche a changé entre-temps.";
  const relance = apiQuiRepond(Response.json({ title: "Le brouillon n'est pas prêt.", detail: motif, status: 409 }, { status: 409 }));
  const autreChamp = apiQuiRepond(Response.json({ title: "Refus", status: 400, errors: { objet: ["Inconnu."] } }, { status: 400 }));
  const introuvable = apiQuiRepond(Response.json({ title: "Cette candidature n'existe pas.", status: 404 }, { status: 404 }));

  expect(await creerClientDeLApi(JETON_FICTIF, champ).deposerLeBrouillonRelu(3, 7, "x")).toEqual({
    issue: "refusee",
    refus: ["Le brouillon ne doit pas dépasser 2000 caractères."],
  });
  expect(await creerClientDeLApi(JETON_FICTIF, relance).deposerLeBrouillonRelu(3, 7, "x")).toEqual({ issue: "refuse", motif });
  await expect(creerClientDeLApi(JETON_FICTIF, autreChamp).deposerLeBrouillonRelu(3, 7, "x")).rejects.toBeInstanceOf(ErreurDeLApi);
  await expect(creerClientDeLApi(JETON_FICTIF, introuvable).deposerLeBrouillonRelu(3, 7, "x")).rejects.toBeInstanceOf(ErreurDeLApi);
});
