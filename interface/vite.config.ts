import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  css: {
    // Aucun traitement PostCSS. Écrite ici, cette configuration vide empêche Vite d'aller chercher un fichier de
    // configuration PostCSS dans les dossiers au-dessus du dépôt, où un autre projet a pu en laisser un.
    postcss: { plugins: [] },
  },
  build: {
    // Aucun fichier inliné en adresse data: : la politique de sécurité du contenu n'admet que les images et
    // les polices servies par l'origine de l'interface (img-src 'self', font-src 'self').
    assetsInlineLimit: 0,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/tests/preparation.ts"],
    // Les écrans seulement ; la vraie fenêtre a son propre lanceur (vitest.fenetre.config.ts).
    include: ["src/**/*.test.{ts,tsx}"],
    // Les heures s'affichent dans le fuseau du poste : les tests fixent celui de Bruxelles, quel que soit celui de la
    // machine (la CI tourne en UTC).
    env: { TZ: "Europe/Brussels" },
  },
});
