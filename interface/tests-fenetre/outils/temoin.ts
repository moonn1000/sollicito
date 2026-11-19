import { createServer } from "node:http";

/**
 * Un serveur témoin, sur une autre origine que l'interface (127.0.0.1, un autre port) : il note chaque requête
 * qu'il reçoit. Aucune ne doit lui parvenir de la fenêtre.
 */
export interface Temoin {
  readonly origine: string;
  readonly requetes: readonly string[];
  arreter(): Promise<void>;
}

export async function demarrerLeTemoin(): Promise<Temoin> {
  const requetes: string[] = [];
  const serveur = createServer((requete, reponse) => {
    requetes.push(`${requete.method} ${requete.url}`);
    reponse.end("témoin");
  });
  await new Promise<void>((resoudre) => serveur.listen(0, "127.0.0.1", resoudre));
  const adresse = serveur.address();
  const port = typeof adresse === "object" && adresse !== null ? adresse.port : 0;

  return {
    origine: `http://127.0.0.1:${port}`,
    requetes,
    arreter: () => new Promise((resoudre) => serveur.close(() => resoudre())),
  };
}
