import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Chaque test part d'un document vide.
afterEach(() => {
  cleanup();
});
