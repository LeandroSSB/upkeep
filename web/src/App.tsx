import type { ReactNode } from "react";
import { Link, Navigate, Outlet, Route, Routes } from "react-router-dom";
import { AppShell } from "./components/AppShell";
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
// A barra só pinta com user resolvido: sem ela o bootstrap pisca a tabbar antes
// do redirect para /entrar (ou da Home, quando há refresh salvo).
function TabbedLayout() {
  const { user } = useSession();
  return (
    <>
      <Outlet />
      {user && <TabBar />}
    </>
  );
}

// Carregando a sessão: shell vazio (header só) — não pisca "/entrar" em quem
// já tem refresh salvo nem Home em quem não tem.
function LoadingShell() {
  return <AppShell />;
}

// URL fora do mapa (ex.: /foo) — mesmo tratamento do ativo não encontrado.
function NotFound() {
  return (
    <AppShell>
      <div className="empty-state">
        <p>Página não encontrada.</p>
        <Link className="btn" to="/">
          Voltar para o início
        </Link>
      </div>
    </AppShell>
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
      <Route
        path="*"
        element={
          <RequireAuth>
            <NotFound />
          </RequireAuth>
        }
      />
    </Routes>
  );
}
