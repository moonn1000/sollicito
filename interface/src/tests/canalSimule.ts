import type { CanalDeLHote } from "../hote/canalDeLHote.ts";

/** Un jeton fictif, de la forme du jeton de lancement : 43 caractères base64url. */
export const JETON_FICTIF = "ZmljdGlmLWpldG9uLWRlLXRlc3QtcG91ci1zb2xsaWN";

/** Le canal de la fenêtre hôte, simulé : il garde ce que la page envoie et laisse le test répondre. */
export class CanalSimule implements CanalDeLHote {
  readonly messagesRecus: unknown[] = [];
  private readonly ecouteurs = new Set<(evenement: MessageEvent) => void>();

  postMessage(message: unknown): void {
    this.messagesRecus.push(message);
  }

  addEventListener(_type: "message", ecouteur: (evenement: MessageEvent) => void): void {
    this.ecouteurs.add(ecouteur);
  }

  removeEventListener(_type: "message", ecouteur: (evenement: MessageEvent) => void): void {
    this.ecouteurs.delete(ecouteur);
  }

  /** Nombre d'écouteurs encore abonnés. */
  get abonnes(): number {
    return this.ecouteurs.size;
  }

  /** L'hôte envoie un message à la page. */
  envoyer(donnee: unknown): void {
    for (const ecouteur of [...this.ecouteurs]) {
      ecouteur(new MessageEvent("message", { data: donnee }));
    }
  }
}
