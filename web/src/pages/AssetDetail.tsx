// Ativo — a ordem de serviço: header com odômetro, manutenções como stickers e o
// logbook de serviços. Formulários (template/serviço/editar) chegam na Task 5 —
// as rotas já existem como placeholder.
import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router-dom";
import { deleteAsset, listAssets, listTemplates, type Asset, type AssetTemplate } from "../api/assets";
import { ApiError } from "../api/client";
import { listServices, type Service } from "../api/services";
import { AppShell } from "../components/AppShell";
import { Sticker } from "../components/Sticker";
import { Toast } from "../components/Toast";
import { WorkOrderHeader } from "../components/WorkOrderHeader";
import { templateDestaque, todayIso } from "../lib/destaque";
import { formatBRL, formatDate, formatKm } from "../lib/format";
import { gaugeFraction } from "../lib/gauge";

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "notfound" }
  | { kind: "ready"; asset: Asset; templates: AssetTemplate[]; services: Service[] };

export default function AssetDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  // Forms de sucesso (ex.: lançar serviço) chegam com { toast } no state da rota
  const toast = (location.state as { toast?: string } | null)?.toast ?? null;
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);
  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    setPhase({ kind: "loading" });
    setConfirming(false);
    setDeleteError(null);
    // asset vem da lista do usuário (sem endpoint GET por id); templates e serviços
    // aninhados 404 quando o asset é de outro usuário — tratado junto abaixo.
    Promise.all([listAssets(), listTemplates(id), listServices(id)])
      .then(([assets, templates, services]) => {
        if (cancelled) return;
        const asset = assets.find((a) => a.id === id);
        if (!asset) {
          setPhase({ kind: "notfound" });
          return;
        }
        setPhase({ kind: "ready", asset, templates, services });
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof ApiError && (err.status === 404 || err.status === 400)) {
          setPhase({ kind: "notfound" }); // id alheio (404) ou malformado (400 no bind do Guid)
        } else {
          setPhase({
            kind: "error",
            message: err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.",
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [id, attempt]);

  // km andou → status/kmRemaining dos stickers mudam no servidor; só rebusca os templates.
  function onOdometerSaved(km: number) {
    if (!id) return;
    setPhase((p) => (p.kind === "ready" ? { ...p, asset: { ...p.asset, odometroAtual: km } } : p));
    listTemplates(id)
      .then((templates) => {
        setPhase((p) => (p.kind === "ready" ? { ...p, templates } : p));
      })
      .catch(() => {} // falha aqui não derruba a tela: os stickers ficam como estão
      );
  }

  async function onDelete() {
    if (!id) return;
    setDeleting(true);
    setDeleteError(null);
    try {
      await deleteAsset(id);
      navigate("/");
    } catch (err) {
      setDeleting(false);
      setDeleteError(err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.");
    }
  }

  return (
    <AppShell
      after={
        toast && (
          <Toast
            message={toast}
            onHide={() => navigate(location.pathname, { replace: true })} // limpa o state
          />
        )
      }
    >
        {phase.kind === "loading" && <p className="loading">Carregando…</p>}

        {phase.kind === "error" && (
          <div className="load-error">
            <p className="form-error" role="alert">
              {phase.message}
            </p>
            <button type="button" className="btn" onClick={() => setAttempt((n) => n + 1)}>
              Tentar de novo
            </button>
          </div>
        )}

        {phase.kind === "notfound" && (
          <div className="empty-state">
            <p>Ativo não encontrado.</p>
            <Link className="btn" to="/">
              Voltar para a lista
            </Link>
          </div>
        )}

        {phase.kind === "ready" && (
          <>
            <WorkOrderHeader key={phase.asset.id} asset={phase.asset} onOdometerSaved={onOdometerSaved} />

            <p className="eyebrow">Manutenções</p>
            {phase.templates.length === 0 && (
              <p className="section-empty">Nenhuma manutenção programada ainda.</p>
            )}
            {phase.templates.map((t) => (
              <Link key={t.id} to={`/ativos/${phase.asset.id}/templates/${t.id}`} className="asset-link">
                <Sticker
                  item={{
                    titulo: t.titulo,
                    subtitulo: t.categoria ?? undefined,
                    status: t.status ?? "ok",
                    destaque: templateDestaque(t, todayIso()) ?? undefined,
                    gauge: gaugeFraction(t, todayIso()),
                  }}
                />
              </Link>
            ))}
            <Link className="btn section-action" to={`/ativos/${phase.asset.id}/templates/novo`}>
              Nova manutenção
            </Link>

            <p className="eyebrow">Histórico</p>
            {phase.services.length === 0 ? (
              <p className="section-empty">Nada lançado ainda — registre a primeira manutenção.</p>
            ) : (
              <ul className="logbook">
                {phase.services.map((s) => {
                  const template =
                    s.templateId != null ? phase.templates.find((t) => t.id === s.templateId) : undefined;
                  return (
                    <li key={s.id} className="logbook__row">
                      <span className="logbook__data">{formatDate(s.data)}</span>
                      <span className="logbook__desc">
                        <span className="logbook__titulo">{template?.titulo ?? "Serviço avulso"}</span>
                        {s.odometro != null && (
                          <span className="logbook__km">{formatKm(s.odometro)}</span>
                        )}
                        {s.notas && <span className="logbook__notas">{s.notas}</span>}
                      </span>
                      <span className="logbook__custo">{formatBRL(s.custo)}</span>
                    </li>
                  );
                })}
              </ul>
            )}

            <div className="wo-footer">
              {confirming ? (
                <div className="wo-confirm" role="alert">
                  <p>
                    Excluir {phase.asset.nome}? Isso apaga as manutenções e o histórico.
                  </p>
                  <div className="wo-confirm__actions">
                    <button type="button" className="btn btn--danger" onClick={onDelete} disabled={deleting}>
                      {deleting ? "Excluindo…" : "Excluir"}
                    </button>
                    <button
                      type="button"
                      className="link"
                      onClick={() => setConfirming(false)}
                      disabled={deleting}
                    >
                      Cancelar
                    </button>
                  </div>
                  {deleteError && <p className="field__error">{deleteError}</p>}
                </div>
              ) : (
                <>
                  <Link className="link" to={`/ativos/${phase.asset.id}/editar`}>
                    Editar
                  </Link>
                  <button type="button" className="link link--danger" onClick={() => setConfirming(true)}>
                    Excluir
                  </button>
                </>
              )}
            </div>

            <div className="wo-actions">
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => navigate(`/ativos/${phase.asset.id}/servicos/novo`)}
              >
                Lançar serviço
              </button>
            </div>
          </>
        )}
    </AppShell>
  );
}
