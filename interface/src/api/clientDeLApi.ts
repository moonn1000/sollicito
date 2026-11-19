/** Le seul en-tête où l'API lit le jeton (ADR-0001, D9) : jamais un cookie, jamais l'adresse. */
export const EN_TETE_DU_JETON = "Sollicito-Jeton";

/** Réponse de `GET /api/etat`. */
export interface EtatDeLApplication {
  statut: string;
}

/** Une réponse de l'API en erreur : jeton refusé, origine refusée, route inconnue… */
export class ErreurDeLApi extends Error {
  readonly statut: number;

  constructor(statut: number) {
    super(`L'API locale a répondu ${statut}.`);
    this.name = "ErreurDeLApi";
    this.statut = statut;
  }
}

/** Les appels de l'interface à l'API locale. */
export interface ClientDeLApi {
  lireEtat(): Promise<EtatDeLApplication>;
}

/**
 * Le client de l'API locale. Le jeton reste dans sa fermeture, en mémoire : ni stockage du navigateur, ni
 * cookie, ni adresse. Chaque appel le porte dans son en-tête dédié, vers la même origine que la page.
 */
export function creerClientDeLApi(jeton: string, envoyer: typeof fetch = fetch): ClientDeLApi {
  const lire = async (chemin: string): Promise<unknown> => {
    const reponse = await envoyer(chemin, {
      headers: { [EN_TETE_DU_JETON]: jeton },
      cache: "no-store",
      credentials: "omit",
      redirect: "error",
    });
    if (!reponse.ok) {
      throw new ErreurDeLApi(reponse.status);
    }

    return reponse.json();
  };

  return {
    lireEtat: async () => (await lire("/api/etat")) as EtatDeLApplication,
  };
}
