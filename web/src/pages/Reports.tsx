// Relatórios — custo total e por ativo no período escolhido. Filtros (período +
// ativo) rebuscam na hora; barras horizontais proporcionais ao maior total.
import { useEffect, useState } from "react";
import { listAssets, type Asset } from "../api/assets";
import { ApiError } from "../api/client";
import { getCostReport, type CostByAsset, type CostReport } from "../api/reports";
import { formatBRL } from "../lib/format";

function plural(n: number, um: string, varios: string): string {
  return `${n} ${n === 1 ? um : varios}`;
}

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "ready"; report: CostReport };

export default function Reports() {
  // string vazia = sem filtro (o parâmetro nem vai na query)
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [assetId, setAssetId] = useState("");

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

  // mudança de filtro refaz o fetch na hora (sem debounce: 3 controles leves);
  // com dados já na tela, mantemos a tabela e sinalizamos "Atualizando…"
  useEffect(() => {
    let cancelled = false;
    setPhase((p) => (p.kind === "ready" ? p : { kind: "loading" }));
    setRefreshing(true);
    getCostReport({ assetId, from, to })
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
  }, [assetId, from, to, attempt]);

  const report = phase.kind === "ready" ? phase.report : null;

  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>

      <main>
        <h1>Relatórios</h1>

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

        {report && report.porAsset.length === 0 && (
          <p className="section-empty">Nenhum custo no período.</p>
        )}

        {report && report.porAsset.length > 0 && (
          <>
            <p className="eyebrow">Total no período</p>
            <p className="display-xl report-total">{formatBRL(report.total)}</p>

            <p className="eyebrow">Custo por ativo</p>
            <ul className="bar-list">
              {report.porAsset.map((row) => (
                <BarRow key={row.assetId} row={row} max={maxTotal(report)} />
              ))}
            </ul>
          </>
        )}

        {refreshing && report && (
          <p className="loading" role="status">
            Atualizando…
          </p>
        )}
      </main>
    </div>
  );
}

/** A API já devolve total desc; Math.max por robustez (largura 100% = maior). */
function maxTotal(report: CostReport): number {
  return Math.max(...report.porAsset.map((r) => r.total));
}

/** Linha do por-asset: nome + contagem + valor mono, barra proporcional ao máximo. */
function BarRow({ row, max }: { row: CostByAsset; max: number }) {
  const pct = max > 0 ? (row.total / max) * 100 : 0;
  return (
    <li className="bar-row">
      <p className="bar-row__top">
        <span className="bar-row__nome">{row.nome}</span>
        <span className="bar-row__qtd">{plural(row.quantidade, "serviço", "serviços")}</span>
        <span className="bar-row__valor">{formatBRL(row.total)}</span>
      </p>
      <div className="bar-row__track">
        <div className="bar-row__fill" style={{ width: `${pct}%` }} />
      </div>
    </li>
  );
}
