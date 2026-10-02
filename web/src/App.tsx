import type { ReactNode } from "react";
import { Navigate, Outlet, Route, Routes } from "react-router-dom";
import { TabBar } from "./components/TabBar";
import AssetDetail from "./pages/AssetDetail";
import AssetForm from "./pages/AssetForm";
import Home from "./pages/Home";
import Login from "./pages/Login";
import Reports from "./pages/Reports";
import ServiceForm from "./pages/ServiceForm";
import Settings from "./pages/Settings";
import TemplateForm from "./pages/TemplateForm";
import { useSession } from "./state/session";

// Telas "de bolso" (Home/Relatórios/Ajustes) compartilham a tabbar fixa — quem
// entra no pathless layout abaixo tem a barra; /entrar, detalhe e formulários não.
function TabbedLayout() {
  return (
    <>
      <Outlet />
      <TabBar />
    </>
  );
}

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
      <Route element={<TabbedLayout />}>
        <Route
          path="/"
          element={
            <RequireAuth>
              <Home />
            </RequireAuth>
          }
        />
        <Route
          path="/relatorios"
          element={
            <RequireAuth>
              <Reports />
            </RequireAuth>
          }
        />
        <Route
          path="/ajustes"
          element={
            <RequireAuth>
              <Settings />
            </RequireAuth>
          }
        />
      </Route>
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
