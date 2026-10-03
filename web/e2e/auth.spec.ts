// Ciclo completo de sessão pela UI: criar conta → home vazia → sair → entrar.
import { test, expect } from "@playwright/test";
import { SENHA_E2E, uniqueEmail } from "./helpers";

test("criar conta, sair e entrar de novo", async ({ page }) => {
  const email = uniqueEmail("auth");

  await page.goto("/entrar");
  await expect(page.getByRole("heading", { name: "Entrar" })).toBeVisible();

  // primeira vez → criar conta
  await page.getByRole("button", { name: "Criar conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(SENHA_E2E);
  await page.getByRole("button", { name: "Criar conta", exact: true }).click();

  // cai na home vazia
  await expect(page.getByText("Nenhum ativo ainda")).toBeVisible();

  // sair em Ajustes volta para /entrar
  await page.getByRole("link", { name: "Ajustes" }).click();
  await expect(page.getByRole("heading", { name: "Ajustes" })).toBeVisible();
  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole("heading", { name: "Entrar" })).toBeVisible();

  // login de novo com a mesma conta
  await page.getByLabel("E-mail", { exact: true }).fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(SENHA_E2E);
  await page.getByRole("button", { name: "Entrar", exact: true }).click();
  await expect(page.getByText("Nenhum ativo ainda")).toBeVisible();
});
