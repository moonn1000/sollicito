import { creerClientDeLApi, type ClientDeLApi } from "./api/clientDeLApi.ts";
import { canalDeLaFenetre, demanderLeJeton, type CanalDeLHote } from "./hote/canalDeLHote.ts";

/** La page s'est ouverte hors de la fenêtre de Sollicito : aucun jeton ne lui sera remis. */
export class HorsDeLaFenetre extends Error {
  constructor() {
    super("La page ne s'affiche pas dans la fenêtre de Sollicito.");
    this.name = "HorsDeLaFenetre";
  }
}

/** Demande le jeton à la fenêtre hôte, puis rend le client de l'API qui le porte. */
export async function seConnecter(
  canal: CanalDeLHote | undefined = canalDeLaFenetre(),
  envoyer: typeof fetch = fetch,
): Promise<ClientDeLApi> {
  if (canal === undefined) {
    throw new HorsDeLaFenetre();
  }

  const jeton = await demanderLeJeton(canal);
  return creerClientDeLApi(jeton, envoyer);
}
