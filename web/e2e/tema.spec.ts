// Ajustes → Aparência: os 3 estados do tema. "auto" REMOVE o atributo
// data-theme do html (o @media prefers-color-scheme resolve); explícitos setam.
import { test, expect } from "@playwright/test";
import { criarConta, uniqueEmail } from "./helpers";

test("escuro/claro/automático alternam o data-theme do html", async ({ page }) => {
  await criarConta(page, uniqueEmail("tema"));
  await page.goto("/ajustes");
  await expect(page.getByRole("heading", { name: "Aparência" })).toBeVisible();

  const html = page.locator("html");

  await page.getByRole("button", { name: "Escuro", exact: true }).click();
  await expect(html).toHaveAttribute("data-theme", "dark");

  await page.getByRole("button", { name: "Claro", exact: true }).click();
  await expect(html).toHaveAttribute("data-theme", "light");

  // automático: atributo removido (o SO decide via media query)
  await page.getByRole("button", { name: "Automático", exact: true }).click();
  expect(await page.evaluate(() => document.documentElement.dataset.theme ?? null)).toBeNull();
});
