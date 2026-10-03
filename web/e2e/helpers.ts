// Helpers compartilhados das specs E2E — fluxos pela UI real (mesmos seletores
// por role/label do QA manual do controller), sem atalhos de API.
import { expect, type Page } from "@playwright/test";

export const SENHA_E2E = "senha-e2e-123";

/** E-mail único por spec — nada de colisão entre specs paralelas nem re-runs. */
export function uniqueEmail(prefix: string): string {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@test.local`;
}

/**
 * Cria conta pela UI (troca para o modo "criar conta") e espera a home vazia —
 * o ponto de partida comum das specs que precisam de sessão.
 */
export async function criarConta(page: Page, email: string, senha = SENHA_E2E): Promise<void> {
  await page.goto("/entrar");
  await page.getByRole("button", { name: "Criar conta" }).click(); // link de troca de modo
  await page.getByLabel("E-mail", { exact: true }).fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(senha);
  await page.getByRole("button", { name: "Criar conta", exact: true }).click();
  await expect(page.getByText("Nenhum ativo ainda")).toBeVisible();
}
