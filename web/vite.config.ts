// Dev: /api/* repassado para a API local SEM o prefixo (a API pública vive sob
// /api apenas no nginx de produção; o código do SPA sempre chama caminhos
// relativos "/api/..."). Vitest: e2e/ (specs do Playwright) fica fora da suíte
// unitária — o default do vitest pegaria **/*.spec.ts.
import { defineConfig, configDefaults } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5080",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
  build: {
    outDir: "dist",
  },
  test: {
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
});
