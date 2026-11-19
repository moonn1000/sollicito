import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
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
  },
});
