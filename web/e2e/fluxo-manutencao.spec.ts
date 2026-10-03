// O fluxo de manutenção por km do começo ao fim: veículo com odômetro →
// manutenção 10.000km/12m (km base) → sticker ok → odômetro estoura → vencido
// (home e detalhe, sob reload) → serviço lançado reseta a baseline → ok de novo,
// histórico alimentado e relatório por ativo com o valor.
import { test, expect } from "@playwright/test";
import { criarConta, uniqueEmail } from "./helpers";

test("manutenção por km vence com o odômetro e volta ao ok após serviço", async ({ page }) => {
  await criarConta(page, uniqueEmail("fluxo"));

  // veículo com odômetro 50.000
  await page.goto("/ativos/novo");
  await page.getByLabel("Nome", { exact: true }).fill("Fusca e2e");
  await page.getByLabel("Tipo").selectOption({ label: "veículo" });
  await page.getByLabel("Odômetro atual (opcional)").fill("50000");
  await page.getByRole("button", { name: "Salvar ativo" }).click();
  await expect(page.getByRole("heading", { name: "Fusca e2e" })).toBeVisible();

  // manutenção: repetir 10.000 km / 12 meses, km base 50.000
  await page.getByRole("link", { name: "Nova manutenção" }).click();
  await page.getByLabel("Título", { exact: true }).fill("Troca de óleo");
  await page.getByRole("checkbox", { name: "Repetir por km" }).check();
  await page.getByLabel("Intervalo em km").fill("10000");
  await page.getByRole("checkbox", { name: "Repetir por meses" }).check();
  await page.getByLabel("Intervalo em meses").fill("12");
  await page.getByLabel("Km base (opcional)").fill("50000");
  await page.getByRole("button", { name: "Salvar manutenção" }).click();
  await expect(page.getByRole("heading", { name: "Fusca e2e" })).toBeVisible();
  await expect(page.locator(".tag--ok").first()).toBeVisible();

  // odômetro 61.500 → estourou 1.500 km → vencido no detalhe
  await page.getByRole("button", { name: "atualizar km" }).click();
  await page.getByLabel("Odômetro atual").fill("61500");
  await page.getByRole("button", { name: "Salvar", exact: true }).click();
  await expect(page.locator(".tag--vencido").first()).toBeVisible();
  await expect(page.getByText("estourou 1.500 km")).toBeVisible();

  // reload mantém o vencido (status é do servidor, não de estado local)
  await page.reload();
  await expect(page.locator(".tag--vencido").first()).toBeVisible();

  // ... e a home mostra o herói vencido (mostrador HeroPanel)
  await page.goto("/");
  await expect(page.locator(".panel--vencido").first()).toBeVisible();

  // serviço vinculado (odômetro 62.000, custo 350,50) reseta a baseline → ok
  await page.getByRole("link", { name: /Fusca e2e/ }).first().click();
  await page.getByRole("button", { name: "Lançar serviço" }).click();
  await page.getByLabel("Manutenção (opcional)").selectOption({ label: "Troca de óleo" });
  await page.getByLabel("Odômetro (opcional)").fill("62000");
  await page.getByLabel("Custo", { exact: true }).fill("350,50");
  await page.getByRole("button", { name: "Lançar serviço", exact: true }).click();
  await expect(page.getByText("Serviço lançado")).toBeVisible();
  await expect(page.locator(".tag--ok").first()).toBeVisible();

  // histórico tem a linha do serviço
  await expect(page.getByText("62.000 km").first()).toBeVisible();
  await expect(page.getByText("R$ 350,50").first()).toBeVisible();

  // relatório por ativo mostra o valor (linha da barra do ativo; o <option>
  // escondido do filtro também tem o nome — por isso o escopo .bar-row)
  await page.goto("/relatorios");
  const linha = page.locator(".bar-row", { hasText: "Fusca e2e" });
  await expect(linha).toBeVisible();
  await expect(linha.getByText("R$ 350,50")).toBeVisible();
});
