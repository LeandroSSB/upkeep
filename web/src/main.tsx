import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";

import "@fontsource/barlow-condensed/600.css";
import "@fontsource/barlow-condensed/700.css";
import "@fontsource/barlow/400.css";
import "@fontsource/barlow/500.css";
import "@fontsource/barlow/600.css";
import "@fontsource/ibm-plex-mono/400.css";
import "@fontsource/ibm-plex-mono/500.css";

import "./tokens.css";
import "./app.css";
import App from "./App";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { watchSystemTheme } from "./lib/theme";
import { SessionProvider } from "./state/session";
import { registerServiceWorker } from "./sw-register";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <ErrorBoundary>
        <SessionProvider>
          <App />
        </SessionProvider>
      </ErrorBoundary>
    </BrowserRouter>
  </StrictMode>,
);

// PWA: registra o SW só em produção (dentro do módulo há o gate duplo
// import.meta.env.PROD + 'serviceWorker' in navigator).
registerServiceWorker();

// Tema "auto" ao vivo: o SO trocar de tema re-resolve o meta theme-color
// (as cores em si já acompanham via @media do tokens.css). Vive a app toda.
watchSystemTheme();
