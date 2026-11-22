import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { adresseDuWeb, LienExterne } from "./LienExterne.tsx";

test.each([
  ["https://emploi.exemple.invalid/offres/123?source=fictive#detail", "https://emploi.exemple.invalid/offres/123?source=fictive#detail"],
  ["http://exemple.invalid", "http://exemple.invalid/"],
  ["HTTPS://Exemple.Invalid:8443/offre", "https://exemple.invalid:8443/offre"],
])("une adresse en http ou https devient un lien en nouvelle fenêtre : %s", (adresse, lien) => {
  render(<LienExterne adresse={adresse} />);

  const ancre = screen.getByRole("link", { name: adresse });
  expect(ancre.getAttribute("href")).toBe(lien);
  expect(ancre.getAttribute("target")).toBe("_blank");
  expect(ancre.getAttribute("rel")).toBe("noopener noreferrer");
  expect(ancre.textContent).toBe(adresse);
});

test.each([
  "javascript:alert('fictif')",
  "java\tscript:alert('fictif')",
  "JAVASCRIPT:alert('fictif')",
  "data:text/html,<b>fictif</b>",
  "mailto:contact@exemple.invalid",
  "ftp://exemple.invalid/offre",
  "file:///C:/offre.html",
  "/offres/12",
  "//exemple.invalid/offre",
  "exemple.invalid/offre",
  "https://nom@exemple.invalid/offre",
  "https://nom:motdepasse@exemple.invalid/offre",
  "pas une adresse",
  "",
])("une autre adresse s'affiche en texte, sans lien : %s", (adresse) => {
  const { container } = render(
    <p>
      <LienExterne adresse={adresse} />
    </p>,
  );

  expect(screen.queryByRole("link")).toBeNull();
  expect(container.querySelector("a")).toBeNull();
  expect(container.textContent).toBe(adresse);
  expect(adresseDuWeb(adresse)).toBeUndefined();
});

test("une adresse piégée s'affiche échappée : aucune balise ne se crée", () => {
  const piege = "https://exemple.invalid/<img src=x onerror=alert(1)>";
  const { container } = render(<LienExterne adresse={piege} />);

  expect(container.querySelector("img")).toBeNull();
  expect(screen.getByRole("link").textContent).toBe(piege);
});
