// Lançar serviço no histórico do ativo: template opcional ("Serviço avulso"),
// data padrão hoje (máx. amanhã, espelho da API), odômetro se veículo, custo
// obrigatório em pt-BR. Sucesso → volta ao ativo com toast "Serviço lançado".
import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { listAssets, listTemplates, type Asset, type AssetTemplate } from "../api/assets";
import { ApiError } from "../api/client";
import { createService, type ServiceInput } from "../api/services";
import { Field } from "../components/Field";
import { todayIso } from "../lib/destaque";
import { fieldMessage, leftoverMessage } from "../lib/formErrors";
import { formatKmInput, parseKm } from "../lib/kmInput";
import { parseBRL } from "../lib/money";

const CAMPOS = ["templateId", "data", "odometro", "custo", "notas"];

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "notfound" }
  | { kind: "ready"; asset: Asset; templates: AssetTemplate[] };

/** Amanhã local em "YYYY-MM-DD" — o limite da API é hoje(UTC)+1. */
function tomorrowIso(now: Date = new Date()): string {
  const amanha = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  return todayIso(amanha);
}

export default function ServiceForm() {
  const { id: assetId } = useParams<{ id: string }>();
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!assetId) return;
    let cancelled = false;
    setPhase({ kind: "loading" });
    Promise.all([listAssets(), listTemplates(assetId)])
      .then(([assets, templates]) => {
        if (cancelled) return;
        const asset = assets.find((a) => a.id === assetId);
        if (!asset) {
          setPhase({ kind: "notfound" });
          return;
        }
        setPhase({ kind: "ready", asset, templates });
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof ApiError && (err.status === 404 || err.status === 400)) {
          setPhase({ kind: "notfound" });
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
  }, [assetId, attempt]);

  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>

      <main>
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
          <ServiceFields key={phase.asset.id} asset={phase.asset} templates={phase.templates} />
        )}
      </main>
    </div>
  );
}

type FieldErrs = { data?: string; odometro?: string; custo?: string; notas?: string };

function ServiceFields({ asset, templates }: { asset: Asset; templates: AssetTemplate[] }) {
  const veiculo = asset.tipo === "veiculo";
  const navigate = useNavigate();
  const [templateId, setTemplateId] = useState("");
  const [data, setData] = useState(todayIso());
  const [odometro, setOdometro] = useState("");
  const [custo, setCusto] = useState("");
  const [notas, setNotas] = useState("");
  const [errs, setErrs] = useState<FieldErrs>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  function validate(): FieldErrs {
    const e: FieldErrs = {};
    if (!data) e.data = "Informe a data.";
    if (veiculo && odometro.trim() !== "" && parseKm(odometro) == null)
      e.odometro = "Use apenas números — ex.: 61.500.";
    if (custo.trim() === "") e.custo = "Informe o custo.";
    else if (parseBRL(custo) == null) e.custo = "Use números — ex.: 350 ou 350,50.";
    if (notas.trim().length > 2000) e.notas = "As notas podem ter até 2000 caracteres.";
    return e;
  }

  async function onSubmit(ev: FormEvent) {
    ev.preventDefault();
    const e = validate();
    setErrs(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;

    const input: ServiceInput = {
      templateId: templateId || null,
      data,
      odometro: veiculo && odometro.trim() !== "" ? parseKm(odometro) : null,
      custo: parseBRL(custo) ?? 0, // validate() garantiu que parseia
      notas: notas.trim() || null,
    };

    setSending(true);
    try {
      await createService(asset.id, input);
      // state carrega o toast: o AssetDetail remonta (histórico rebuscado) e exibe
      navigate(`/ativos/${asset.id}`, { state: { toast: "Serviço lançado" } });
    } catch (err) {
      if (err instanceof ApiError) {
        setErrs({
          data: fieldMessage(err.errors, "data"),
          odometro: fieldMessage(err.errors, "odometro") ?? fieldMessage(err.errors, "odometroAtual"),
          custo: fieldMessage(err.errors, "custo"),
          notas: fieldMessage(err.errors, "notas"),
        });
        setFormError(leftoverMessage(err.errors, CAMPOS, err.title));
      } else {
        setFormError("Algo deu errado. Tente de novo.");
      }
    } finally {
      setSending(false);
    }
  }

  return (
    <>
      <h1>Lançar serviço</h1>
      <p className="form-sub">{asset.nome}</p>

      <form onSubmit={onSubmit} noValidate>
        <Field htmlFor="templateId" label="Manutenção (opcional)">
          <select
            id="templateId"
            name="templateId"
            value={templateId}
            onChange={(e) => setTemplateId(e.target.value)}
          >
            <option value="">Serviço avulso</option>
            {templates.map((t) => (
              <option key={t.id} value={t.id}>
                {t.titulo}
              </option>
            ))}
          </select>
        </Field>

        <Field htmlFor="data" label="Data" error={errs.data}>
          <input
            id="data"
            name="data"
            type="date"
            value={data}
            max={tomorrowIso()}
            onChange={(e) => setData(e.target.value)}
            aria-invalid={errs.data !== undefined}
          />
        </Field>

        {veiculo && (
          <Field htmlFor="odometro" label="Odômetro (opcional)" error={errs.odometro}>
            <input
              id="odometro"
              name="odometro"
              className="input-mono"
              type="text"
              inputMode="numeric"
              placeholder={asset.odometroAtual != null ? formatKmInput(asset.odometroAtual) : "61.500"}
              value={odometro}
              onChange={(e) => setOdometro(e.target.value)}
              onBlur={() => {
                const km = parseKm(odometro);
                if (km != null) setOdometro(formatKmInput(km));
              }}
              aria-invalid={errs.odometro !== undefined}
            />
          </Field>
        )}

        <Field htmlFor="custo" label="Custo" error={errs.custo}>
          <input
            id="custo"
            name="custo"
            className="input-mono"
            type="text"
            inputMode="decimal"
            placeholder="350,50"
            value={custo}
            onChange={(e) => setCusto(e.target.value)}
            aria-invalid={errs.custo !== undefined}
          />
        </Field>

        <Field htmlFor="notas" label="Notas (opcional)" error={errs.notas}>
          <textarea
            id="notas"
            name="notas"
            rows={3}
            value={notas}
            onChange={(e) => setNotas(e.target.value)}
          />
        </Field>

        {formError && (
          <p className="form-error" role="alert">
            {formError}
          </p>
        )}

        <div className="form-actions">
          <button type="submit" className="btn btn--primary" disabled={sending}>
            {sending ? "Lançando…" : "Lançar serviço"}
          </button>
          <Link className="link" to={`/ativos/${asset.id}`}>
            Cancelar
          </Link>
        </div>
      </form>
    </>
  );
}
