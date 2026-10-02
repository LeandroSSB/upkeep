import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev: /api/* repassado para a API local SEM o prefixo (a API pública vive sob
// /api apenas no nginx de produção; o código do SPA sempre chama caminhos
// relativos "/api/...").
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
});
