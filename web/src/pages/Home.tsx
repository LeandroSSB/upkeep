// Home — o banco de etiquetas: sticker-herói do mais urgente, seções VENCIDOS /
// VENCE EM BREVE e a lista ATIVOS com os dots de contagem.
import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { listAssets, listTemplates, type Asset } from "../api/assets";
import { ApiError } from "../api/client";
import { AppShell } from "../components/AppShell";
import { Sticker } from "../components/Sticker";
import { templateDestaque, todayIso, worstTemplate } from "../lib/destaque";
import { formatKm, statusLabel, typeLabel } from "../lib/format";
import type { Status } from "../lib/types";

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "ready"; assets: Asset[] };

function plural(n: number, um: string, varios: string): string {
  return `${n} ${n === 1 ? um : varios}`;
}

/** Herói = pior status da tela ("vencido" antes de "vence_em_breve"); nada urgente, sem herói. */
function pickHero(assets: Asset[]): { asset: Asset; status: Status } | null {
  const vencido = assets.find((a) => a.statusAgregado === "vencido");
  if (vencido) return { asset: vencido, status: "vencido" };
  const breve = assets.find((a) => a.statusAgregado === "vence_em_breve");
  if (breve) return { asset: breve, status: "vence_em_breve" };
  return null;
}

/** Pior status global — alimenta o dot do header. */
function worstStatus(assets: Asset[]): Status | null {
  if (assets.some((a) => a.statusAgregado === "vencido")) return "vencido";
  if (assets.some((a) => a.statusAgregado === "vence_em_breve")) return "vence_em_breve";
  return assets.length > 0 ? "ok" : null;
}

/** Dot de contagem: preenchido com o número quando > 0, oco quando 0. */
function CountDot({ status, count }: { status: Status; count: number }) {
  return (
    <span
      className={`count-dot count-dot--${status}${count > 0 ? " count-dot--on" : ""}`}
      title={count > 0 ? `${count} ${statusLabel(status)}` : `nenhum ${statusLabel(status)}`}
    >
      {count > 0 ? count : ""}
    </span>
  );
}

export default function Home() {
  const navigate = useNavigate();
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setPhase({ kind: "loading" });
    listAssets()
      .then((assets) => {
        if (!cancelled) setPhase({ kind: "ready", assets });
      })
      .catch((err) => {
        if (!cancelled) {
          setPhase({
            kind: "error",
            message: err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.",
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  const assets = phase.kind === "ready" ? phase.assets : [];
  const hero = pickHero(assets);
  const heroId = hero?.asset.id;
  const vencidos = assets.filter((a) => a.statusAgregado === "vencido" && a.id !== heroId);
  const breves = assets.filter((a) => a.statusAgregado === "vence_em_breve" && a.id !== heroId);
  const worst = worstStatus(assets);

  // Destaque do herói sai do pior template do asset ("estourou 1.500 km" conta a
  // história melhor que "1 vencida"). Falha/nada urgente nos templates → fallback
  // para o texto de contagem.
  const [heroDetail, setHeroDetail] = useState<{ status: Status; destaque: string } | null>(null);
  useEffect(() => {
    setHeroDetail(null);
    if (!heroId) return;
    let cancelled = false;
    listTemplates(heroId)
      .then((templates) => {
        if (cancelled) return;
        const worstTemplateItem = worstTemplate(templates);
        if (!worstTemplateItem) return;
        const destaque = templateDestaque(worstTemplateItem, todayIso());
        if (destaque) setHeroDetail({ status: worstTemplateItem.status ?? "vencido", destaque });
      })
      .catch(() => {} // sem templates do herói: segue o texto de contagem
      );
    return () => {
      cancelled = true;
    };
  }, [heroId]);

  return (
    <AppShell
      dot={
        worst ? (
          <span
            className={`dot dot--${worst}`}
            aria-label={`pior status: ${statusLabel(worst)}`}
          />
        ) : undefined
      }
      after={
        phase.kind === "ready" && (
          <button
            type="button"
            className="fab"
            aria-label="Cadastrar ativo"
            onClick={() => navigate("/ativos/novo")}
          >
            +
          </button>
        )
      }
    >
      <>
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

        {phase.kind === "ready" && assets.length === 0 && (
          <div className="empty-state">
            <p>Nenhum ativo ainda — cadastre seu carro, casa ou aparelho.</p>
            <button type="button" className="btn btn--primary" onClick={() => navigate("/ativos/novo")}>
              Cadastrar ativo
            </button>
          </div>
        )}

        {phase.kind === "ready" && assets.length > 0 && (
          <>
            {hero && (
              <>
                <p className="eyebrow eyebrow--hero">Mais urgente</p>
                <Link to={`/ativos/${hero.asset.id}`} className="asset-link">
                  <Sticker
                    hero
                    item={{
                      titulo: hero.asset.nome,
                      subtitulo: typeLabel(hero.asset.tipo),
                      status: heroDetail?.status ?? hero.status,
                      destaque:
                        heroDetail?.destaque ??
                        (hero.asset.overdue > 0
                          ? plural(hero.asset.overdue, "vencida", "vencidas")
                          : plural(hero.asset.dueSoon, "vence em breve", "vencem em breve")),
                    }}
                  />
                </Link>
              </>
            )}

            {vencidos.length > 0 && (
              <>
                <p className="eyebrow">Vencidos</p>
                {vencidos.map((a) => (
                  <Link key={a.id} to={`/ativos/${a.id}`} className="asset-link">
                    <Sticker
                      item={{
                        titulo: a.nome,
                        subtitulo: typeLabel(a.tipo),
                        status: "vencido",
                        destaque: plural(a.overdue, "item vencido", "itens vencidos"),
                      }}
                    />
                  </Link>
                ))}
              </>
            )}

            {breves.length > 0 && (
              <>
                <p className="eyebrow">Vence em breve</p>
                {breves.map((a) => (
                  <Link key={a.id} to={`/ativos/${a.id}`} className="asset-link">
                    <Sticker
                      item={{
                        titulo: a.nome,
                        subtitulo: typeLabel(a.tipo),
                        status: "vence_em_breve",
                        destaque: plural(a.dueSoon, "vence em breve", "vencem em breve"),
                      }}
                    />
                  </Link>
                ))}
              </>
            )}

            <p className="eyebrow">Ativos</p>
            <ul className="asset-list">
              {assets.map((a) => (
                <li key={a.id}>
                  <Link to={`/ativos/${a.id}`} className="asset-row">
                    <span className="asset-row__main">
                      <span className="asset-row__nome">{a.nome}</span>
                      <span className="asset-row__tipo">{typeLabel(a.tipo)}</span>
                    </span>
                    {a.tipo === "veiculo" && a.odometroAtual != null && (
                      <span className="asset-row__odo">{formatKm(a.odometroAtual)}</span>
                    )}
                    <span className="count-dots">
                      <CountDot status="vencido" count={a.overdue} />
                      <CountDot status="vence_em_breve" count={a.dueSoon} />
                      <CountDot status="ok" count={a.ok} />
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </>
        )}
      </>
    </AppShell>
  );
}
