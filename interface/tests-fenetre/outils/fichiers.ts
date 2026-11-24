import { readdir, readFile, rename } from "node:fs/promises";
import { join } from "node:path";
import { attendre } from "./fenetre.ts";

/**
 * Le navigateur intégré finit de s'arrêter après l'exe, et écrit encore dans le profil. Le dossier du profil ne se
 * renomme qu'une fois tous ses fichiers fermés : c'est le signal que le navigateur l'a relâché.
 */
export async function profilRelache(dossierDeDonnees: string): Promise<string> {
  const profil = join(dossierDeDonnees, "WebView2");
  const relache = join(dossierDeDonnees, "WebView2-relache");
  for (let essai = 1; ; essai++) {
    try {
      await rename(profil, relache);
      return relache;
    } catch (erreur) {
      if (essai >= 120) {
        throw erreur;
      }
      await attendre(250);
    }
  }
}

/** Les fichiers du dossier, sous-dossiers compris, qui contiennent le texte, en UTF-8 ou en UTF-16. */
export async function fichiersQuiContiennent(dossier: string, texte: string): Promise<string[]> {
  const motifs = [Buffer.from(texte, "utf8"), Buffer.from(texte, "utf16le")];
  const trouves: string[] = [];
  for (const entree of await readdir(dossier, { recursive: true, withFileTypes: true })) {
    if (!entree.isFile()) {
      continue;
    }
    const chemin = join(entree.parentPath, entree.name);
    const contenu = await readFile(chemin);
    if (motifs.some((motif) => contenu.includes(motif))) {
      trouves.push(chemin);
    }
  }
  return trouves;
}
