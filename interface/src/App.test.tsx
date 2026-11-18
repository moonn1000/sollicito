import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { App } from "./App.tsx";

test("l'écran d'accueil affiche le nom de l'application", () => {
  render(<App />);

  expect(screen.getByRole("heading", { level: 1, name: "Sollicito" })).toBeDefined();
});
