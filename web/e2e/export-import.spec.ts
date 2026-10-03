// Posse de dados: export baixa um JSON parseável com os assets; importar o
// MESMO arquivo dobra as contagens (import aditivo) e avisa no toast.
import { readFileSync } from "node:fs";
import { test, expect } from "@playwright/test";
import { criarConta, uniqueEmail } from "./helpers";

test("export baixa JSON íntegro e import do mesmo arquivo dobra as contagens", async ({ page }) => {
  await criarConta(page, uniqueEmail("expimp"));

  // um ativo para o backup ter conteúdo
  await page.goto("/ativos/novo");
  await page.getByLabel("Nome", { exact: true }).fill("Geladeira e2e");
  await page.getByLabel("Tipo").selectOption({ label: "aparelho" });
  await page.getByRole("button", { name: "Salvar ativo" }).click();
  await expect(page.getByRole("heading", { name: "Geladeira e2e" })).toBeVisible();

  // export → download interceptado (blob do SPA via <a download>)
  await page.goto("/ajustes");
  const [download] = await Promise.all([
    page.waitForEvent("download"),
    page.getByRole("button", { name: "Exportar meus dados" }).click(),
  ]);
  const caminho = await download.path();
  if (caminho === null) throw new Error("download do export sem caminho local");
  const texto = readFileSync(caminho, "utf8");
  const backup = JSON.parse(texto) as { assets: unknown[] };
  expect(backup.assets.length).toBeGreaterThanOrEqual(1);

  // import do mesmo arquivo → preview → confirm → toast com o que veio do arquivo
  // (import é ADITIVO: as contagens do arquivo entram por cima das atuais)
  await page.setInputFiles('input[type="file"]', {
    name: download.suggestedFilename(),
    mimeType: "application/json",
    buffer: Buffer.from(texto, "utf8"),
  });
  await expect(page.getByText("Isso ADICIONA 1 ativo")).toBeVisible();
  await page.getByRole("button", { name: "Continuar", exact: true }).click();
  await expect(page.getByText("1 ativo, 0 manutenções, 0 serviços importados")).toBeVisible();

  // ... e os dados DOBRAM na conta: a home lista o ativo 2x (original + cópia)
  await page.goto("/");
  await expect(page.locator(".asset-row__nome", { hasText: "Geladeira e2e" })).toHaveCount(2);
});
