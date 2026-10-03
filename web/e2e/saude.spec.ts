// Smoke da infra: pela stack da UI (nginx → api) o /api/health responde e o
// SPA está sendo servido — garante que a topologia pública do e2e está de pé.
import { test, expect } from "@playwright/test";

test("GET /api/health responde 200 e o SPA é servido", async ({ request }) => {
  const health = await request.get("/api/health");
  expect(health.status()).toBe(200);
  expect(await health.json()).toEqual({ status: "ok" });

  const spa = await request.get("/");
  expect(spa.status()).toBe(200);
  expect(await spa.text()).toContain('<div id="root">');
});
