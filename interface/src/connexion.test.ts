import { expect, test, vi } from "vitest";
import { EN_TETE_DU_JETON, ErreurDeLApi } from "./api/clientDeLApi.ts";
import { HorsDeLaFenetre, seConnecter } from "./connexion.ts";
import { CanalSimule, JETON_FICTIF } from "./tests/canalSimule.ts";

function apiSimulee(statut = 200) {
  return vi.fn<typeof fetch>(async () => Response.json({ statut: "pret" }, { status: statut }));
}

async function connecter(canal: CanalSimule, envoyer: typeof fetch) {
  const client = seConnecter(canal, envoyer);
  canal.envoyer({ type: "jeton", jeton: JETON_FICTIF });
  return client;
}

test("le jeton remis par la fenêtre part dans son en-tête dédié, vers la même origine que la page", async () => {
  const canal = new CanalSimule();
  const envoyer = apiSimulee();

  const client = await connecter(canal, envoyer);
  const etat = await client.lireEtat();

  expect(etat).toEqual({ statut: "pret" });
  expect(envoyer).toHaveBeenCalledOnce();
  const [adresse, options] = envoyer.mock.calls[0]!;
  expect(adresse).toBe("/api/etat");
  expect(new Headers(options?.headers).get(EN_TETE_DU_JETON)).toBe(JETON_FICTIF);
  expect(options?.credentials).toBe("omit");
});

test("le jeton reste en mémoire : ni stockage du navigateur, ni cookie, ni adresse", async () => {
  const canal = new CanalSimule();
  const envoyer = apiSimulee();

  const client = await connecter(canal, envoyer);
  await client.lireEtat();

  expect(localStorage.length).toBe(0);
  expect(sessionStorage.length).toBe(0);
  expect(document.cookie).toBe("");
  expect(window.location.href).not.toContain(JETON_FICTIF);
  expect(String(envoyer.mock.calls[0]![0])).not.toContain(JETON_FICTIF);
});

test("une réponse en erreur de l'API est signalée", async () => {
  const canal = new CanalSimule();

  const client = await connecter(canal, apiSimulee(401));

  await expect(client.lireEtat()).rejects.toEqual(new ErreurDeLApi(401));
});

test("hors de la fenêtre de Sollicito, aucune connexion n'est tentée", async () => {
  const envoyer = apiSimulee();

  await expect(seConnecter(undefined, envoyer)).rejects.toBeInstanceOf(HorsDeLaFenetre);
  expect(envoyer).not.toHaveBeenCalled();
});
