import type { ReactNode } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import AssetDetail from "./pages/AssetDetail";
import AssetForm from "./pages/AssetForm";
import EmConstrucao from "./pages/EmConstrucao";
import Home from "./pages/Home";
import Login from "./pages/Login";
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
      {/* rotas da Task 5 — placeholder até os formulários existirem */}
      <Route
        path="/ativos/:id/templates/novo"
        element={
          <RequireAuth>
            <EmConstrucao titulo="Nova manutenção" />
          </RequireAuth>
        }
      />
      <Route
        path="/ativos/:id/templates/:templateId"
        element={
          <RequireAuth>
            <EmConstrucao titulo="Manutenção" />
          </RequireAuth>
        }
      />
      <Route
        path="/ativos/:id/servicos/novo"
        element={
          <RequireAuth>
            <EmConstrucao titulo="Lançar serviço" />
          </RequireAuth>
        }
      />
      <Route
        path="/ativos/:id/editar"
        element={
          <RequireAuth>
            <EmConstrucao titulo="Editar ativo" />
          </RequireAuth>
        }
      />
    </Routes>
  );
}
