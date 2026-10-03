// Relatórios — custo total com quebra por ativo ou por mês no período escolhido.
// Filtros (período + ativo) e o toggle de agrupamento refazem o fetch na hora;
// barras horizontais proporcionais ao maior total da visão ativa.
import { useEffect, useState } from "react";
import { listAssets, type Asset } from "../api/assets";
import { ApiError } from "../api/client";
import { getCostReport, type CostReport } from "../api/reports";
import { AppShell } from "../components/AppShell";
import { formatBRL, formatKm, formatMonth } from "../lib/format";

function plural(n: number, um: string, varios: string): string {
  return `${n} ${n === 1 ? um : varios}`;
}

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "ready"; report: CostReport };

type View = "asset" | "month";

export default function Reports() {
  // string vazia = sem filtro (o parâmetro nem vai na query)
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [assetId, setAssetId] = useState("");
  const [view, setView] = useState<View>("asset");

  // opções do select — independentes do relatório: falha aqui não bloqueia a tabela
  const [assets, setAssets] = useState<Asset[] | null>(null);
  const [assetsFailed, setAssetsFailed] = useState(false);

  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [refreshing, setRefreshing] = useState(false); // re-fetch com dados na tela
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    listAssets()
      .then((list) => {
        if (!cancelled) {
          setAssets(list);
          setAssetsFailed(false);
        }
      })
      .catch(() => {
        if (!cancelled) setAssetsFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  // mudança de filtro ou de visão refaz o fetch na hora (sem debounce: controles
  // leves); com dados já na tela, mantemos a tabela e sinalizamos "Atualizando…"
  useEffect(() => {
    let cancelled = false;
    setPhase((p) => (p.kind === "ready" ? p : { kind: "loading" }));
    setRefreshing(true);
    getCostReport({ assetId, from, to, groupBy: view === "month" ? "month" : undefined })
      .then((report) => {
        if (!cancelled) setPhase({ kind: "ready", report });
      })
      .catch((err) => {
        if (!cancelled) {
          setPhase({
            kind: "error",
            message: err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.",
          });
        }
      })
      .finally(() => {
        if (!cancelled) setRefreshing(false);
      });
    return () => {
      cancelled = true;
    };
  }, [assetId, from, to, view, attempt]);

  const report = phase.kind === "ready" ? phase.report : null;
  const months = report?.porMes ?? [];
  // visão mês: régua toda zerada (ou clampada vazia) = mesmo empty state;
  // !refreshing evita flash de "Nenhum custo" durante a troca de visão
  const temCusto =
    report !== null &&
    !refreshing &&
    (view === "asset" ? report.porAsset.length > 0 : months.some((m) => m.total > 0));
  // visão mês: o total exibido vem da régua (12 meses), não do período todo —
  // evita contradição "Total R$1.000" com barras somando R$100 (dados antigos fora da janela)
  const totalExibido =
    view === "month" ? months.reduce((acc, m) => acc + m.total, 0) : (report?.total ?? 0);

  return (
    <AppShell>
      <h1>Relatórios</h1>

        <div className="report-toggle" role="group" aria-label="Agrupar custos">
          <button
            type="button"
            className={`btn${view === "asset" ? " btn--primary" : ""}`}
            aria-pressed={view === "asset"}
            onClick={() => setView("asset")}
          >
            Por ativo
          </button>
          <button
            type="button"
            className={`btn${view === "month" ? " btn--primary" : ""}`}
            aria-pressed={view === "month"}
            onClick={() => setView("month")}
          >
            Por mês
          </button>
        </div>

        <div className="report-filters">
          <div className="field">
            <label htmlFor="rep-from">De</label>
            <input id="rep-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="field">
            <label htmlFor="rep-to">Até</label>
            <input id="rep-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
          <div className="field report-filters__asset">
            <label htmlFor="rep-asset">Ativo</label>
            <select
              id="rep-asset"
              value={assetId}
              onChange={(e) => setAssetId(e.target.value)}
              disabled={assets === null}
            >
              <option value="">Todos os ativos</option>
              {assets?.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.nome}
                </option>
              ))}
            </select>
          </div>
        </div>
        {assetsFailed && (
          <p className="section-empty">Não foi possível carregar a lista de ativos.</p>
        )}

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

        {report && !temCusto && <p className="section-empty">Nenhum custo no período.</p>}

        {report && temCusto && (
          <>
            <p className="eyebrow">{view === "month" ? "Total nos últimos 12 meses" : "Total no período"}</p>
            <p className="display-xl report-total">{formatBRL(totalExibido)}</p>

            {/* veículo selecionado: insight de custo por km acima das barras.
             * custoPorKm só vem não-null p/ veículo do usuário; porKm null =
             * ainda sem primeiro odômetro conhecido p/ dividir. */}
            {report.custoPorKm?.porKm != null && (
              <div className="report-km">
                <p className="eyebrow">Custo por km</p>
                <p className="display-xl report-km__valor">
                  {formatBRL(report.custoPorKm.porKm)}/km
                </p>
                <p className="report-km__sub">
                  {formatBRL(report.custoPorKm.total)} em {formatKm(report.custoPorKm.kmRodados)}
                </p>
              </div>
            )}
            {report.custoPorKm != null && report.custoPorKm.porKm == null && (
              <p className="section-empty report-km-empty">Custo por km: ainda sem odômetro inicial</p>
            )}

            {view === "asset" ? (
              <>
                <p className="eyebrow">Custo por ativo</p>
                <ul className="bar-list">
                  {report.porAsset.map((row) => (
                    <BarRow
                      key={row.assetId}
                      nome={row.nome}
                      quantidade={row.quantidade}
                      total={row.total}
                      max={maxTotal(report.porAsset)}
                    />
                  ))}
                </ul>
              </>
            ) : (
              <>
                <p className="eyebrow">Custo por mês</p>
                <ul className="bar-list">
                  {months.map((row) => (
                    <BarRow
                      key={row.mes}
                      nome={formatMonth(row.mes)}
                      quantidade={row.quantidade}
                      total={row.total}
                      max={maxTotal(months)}
                    />
                  ))}
                </ul>
              </>
            )}
          </>
        )}

        {refreshing && report && (
          <p className="loading" role="status">
            Atualizando…
          </p>
        )}
    </AppShell>
  );
}

/** A API já devolve as visões ordenadas (asset: total desc; mês: régua
 * cronológica); Math.max por robustez (largura 100% = maior da visão ativa). */
function maxTotal(rows: { total: number }[]): number {
  return Math.max(...rows.map((r) => r.total));
}

/** Linha de barra (por ativo ou por mês): nome + contagem + valor mono,
 * barra proporcional ao máximo da visão ativa. */
function BarRow({
  nome,
  quantidade,
  total,
  max,
}: {
  nome: string;
  quantidade: number;
  total: number;
  max: number;
}) {
  const pct = max > 0 ? (total / max) * 100 : 0;
  return (
    <li className="bar-row">
      <p className="bar-row__top">
        <span className="bar-row__nome">{nome}</span>
        <span className="bar-row__qtd">{plural(quantidade, "serviço", "serviços")}</span>
        <span className="bar-row__valor">{formatBRL(total)}</span>
      </p>
      <div className="bar-row__track">
        <div className="bar-row__fill" style={{ width: `${pct}%` }} />
      </div>
    </li>
  );
}
