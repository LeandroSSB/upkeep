// E2E contra o stack REAL via compose (docker-compose.e2e.yml): nginx publica o
// SPA e /api/* (strip do prefixo) em 127.0.0.1:14090 — mesma topologia do prod.
// webServer NÃO: o stack sobe pelo compose (CI: job e2e; local: npm run e2e:up).
import { defineConfig } from "@playwright/test";

const baseURL = process.env.E2E_BASE_URL ?? "http://127.0.0.1:14090";

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  retries: 1,
  fullyParallel: true,
  reporter: [["list"]],
  use: {
    baseURL,
    acceptDownloads: true,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { browserName: "chromium" } }],
});
