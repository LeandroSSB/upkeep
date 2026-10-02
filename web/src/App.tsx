import type { ReactNode } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import AssetDetail from "./pages/AssetDetail";
import AssetForm from "./pages/AssetForm";
import Home from "./pages/Home";
import Login from "./pages/Login";
import ServiceForm from "./pages/ServiceForm";
import TemplateForm from "./pages/TemplateForm";
import { useSession } from "./state/session";

// Carregando a sessão: shell vazio (header só) — não pisca "/entrar" em quem
// já tem refresh salvo nem Home em quem não tem.
function LoadingShell() {
  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>
    </div>
  );
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { user, loading } = useSession();
  if (loading) return <LoadingShell />;
  if (!user) return <Navigate to="/entrar" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/entrar" element={<Login />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <Home />
          </RequireAuth>
        }
      />
      {/* novo é estático e vence :id no ranking do router — ordem aqui não decide */}
      <Route
        path="/ativos/novo"
        element={
          <RequireAuth>
            <AssetForm />
          </RequireAuth>
        }
      />
      <Route
        path="/ativos/:id"
        element={
          <RequireAuth>
            <AssetDetail />
          </RequireAuth>
        }
      />
      {/* AssetForm decide criar/editar pela presença do :id na rota */}
      <Route
        path="/ativos/:id/editar"
        element={
          <RequireAuth>
            <AssetForm />
          </RequireAuth>
        }
      />
      {/* TemplateForm decide nova/editar pela presença do :templateId */}
      <Route
        path="/ativos/:id/templates/novo"
        element={
          <RequireAuth>
            <TemplateForm />
          </RequireAuth>
        }
      />
      <Route
        path="/ativos/:id/templates/:templateId"
        element={
          <RequireAuth>
            <TemplateForm />
          </RequireAuth>
        }
      />
      <Route
        path="/ativos/:id/servicos/novo"
        element={
          <RequireAuth>
            <ServiceForm />
          </RequireAuth>
        }
      />
    </Routes>
  );
}
