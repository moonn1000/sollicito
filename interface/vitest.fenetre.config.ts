import { defineConfig } from "vitest/config";

// La vraie fenêtre, pilotée par Playwright (tests-fenetre), à part des tests des écrans : elle demande Windows et
// la version de test de l'exe, que construit `pnpm run test:fenetre`.
export default defineConfig({
  test: {
    include: ["tests-fenetre/**/*.test.ts"],
    environment: "node",
    fileParallelism: false,
    testTimeout: 60_000,
    hookTimeout: 120_000,
  },
});
