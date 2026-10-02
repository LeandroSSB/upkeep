// Manutenção programada (template): nova/editar. Pelo menos um intervalo é
// obrigatório (km e/ou meses — checkbox habilita o campo); baseline opcional
// (km base só aparece com intervalo por km; data base padrão hoje).
import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  createTemplate,
  deleteTemplate,
  listAssets,
  listTemplates,
  updateTemplate,
  type Asset,
  type AssetTemplate,
  type TemplateInput,
} from "../api/assets";
import { ApiError } from "../api/client";
import { AppShell } from "../components/AppShell";
import { Field } from "../components/Field";
import { todayIso } from "../lib/destaque";
import { fieldMessage, leftoverMessage } from "../lib/formErrors";
import { formatKmInput, parseKm } from "../lib/kmInput";
import { formatBRLInput, parseBRL } from "../lib/money";

// Campos com slot de erro no formulário. baselineData fica fora de propósito:
// sem slot, o leftoverMessage sobe a mensagem do servidor como erro do form
// (em vez de engolir pelo title genérico do ProblemDetails).
const CAMPOS = [
  "titulo",
  "categoria",
  "intervaloKm",
  "intervaloMeses",
  "custoEstimado",
  "baselineOdometro",
];

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "notfound" }
  | { kind: "ready"; asset: Asset; template: AssetTemplate | null };

export default function TemplateForm() {
  const { id: assetId, templateId } = useParams<{ id: string; templateId: string }>();
  const editing = templateId != null;
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!assetId) return;
    let cancelled = false;
    setPhase({ kind: "loading" });
    Promise.all([
      listAssets(),
      editing && templateId != null ? listTemplates(assetId) : Promise.resolve([] as AssetTemplate[]),
    ])
      .then(([assets, templates]) => {
        if (cancelled) return;
        const asset = assets.find((a) => a.id === assetId);
        if (!asset) {
          setPhase({ kind: "notfound" });
          return;
        }
        if (!editing) {
          setPhase({ kind: "ready", asset, template: null });
          return;
        }
        const template = templates.find((t) => t.id === templateId);
        setPhase(template ? { kind: "ready", asset, template } : { kind: "notfound" });
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof ApiError && (err.status === 404 || err.status === 400)) {
          setPhase({ kind: "notfound" }); // asset alheio (404) ou guid malformado (400)
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
  }, [assetId, templateId, editing, attempt]);

  return (
    <AppShell>
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

        {phase.kind === "notfound" && (
          <div className="empty-state">
            <p>Ativo ou manutenção não encontrados.</p>
            <Link className="btn" to={assetId != null ? `/ativos/${assetId}` : "/"}>
              Voltar para o ativo
            </Link>
          </div>
        )}

        {phase.kind === "ready" && (
          <TemplateFields
            key={phase.template?.id ?? `${phase.asset.id}-novo`}
            asset={phase.asset}
            template={phase.template}
          />
        )}
      </>
    </AppShell>
  );
}

type FieldErrs = {
  titulo?: string;
  categoria?: string;
  custo?: string;
  intervaloKm?: string;
  intervaloMeses?: string;
  intervalo?: string;
  baselineKm?: string;
};

function TemplateFields({ asset, template }: { asset: Asset; template: AssetTemplate | null }) {
  const editing = template != null;
  const navigate = useNavigate();
  const [titulo, setTitulo] = useState(template?.titulo ?? "");
  const [categoria, setCategoria] = useState(template?.categoria ?? "");
  const [custo, setCusto] = useState(
    template?.custoEstimado != null ? formatBRLInput(template.custoEstimado) : "",
  );
  const [porKm, setPorKm] = useState(template?.intervaloKm != null);
  const [km, setKm] = useState(template?.intervaloKm != null ? formatKmInput(template.intervaloKm) : "");
  const [porMeses, setPorMeses] = useState(template?.intervaloMeses != null);
  const [meses, setMeses] = useState(
    template?.intervaloMeses != null ? String(template.intervaloMeses) : "",
  );
  const [baseKm, setBaseKm] = useState(
    template?.baselineOdometro != null ? formatKmInput(template.baselineOdometro) : "",
  );
  const [baseData, setBaseData] = useState(template?.baselineData ?? todayIso());
  const [errs, setErrs] = useState<FieldErrs>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  function onBlurKm(setter: (v: string) => void) {
    return (value: string) => {
      const parsed = parseKm(value);
      if (parsed != null) setter(formatKmInput(parsed));
    };
  }

  function validate(): FieldErrs {
    const e: FieldErrs = {};
    const t = titulo.trim();
    if (!t) e.titulo = "Informe o título.";
    else if (t.length > 200) e.titulo = "O título pode ter até 200 caracteres.";
    if (categoria.trim().length > 64) e.categoria = "A categoria pode ter até 64 caracteres.";
    if (custo.trim() !== "" && parseBRL(custo) == null)
      e.custo = "Use números — ex.: 350 ou 350,50.";
    if (porKm) {
      const intervalo = parseKm(km);
      if (intervalo == null) e.intervaloKm = "Use apenas números — ex.: 10.000.";
      else if (intervalo <= 0) e.intervaloKm = "O intervalo precisa ser maior que zero.";
    }
    if (porMeses) {
      const intervalo = parseKm(meses);
      if (intervalo == null) e.intervaloMeses = "Use apenas números — ex.: 6.";
      else if (intervalo < 1) e.intervaloMeses = "O intervalo precisa ser de pelo menos 1 mês.";
    }
    if (!porKm && !porMeses) e.intervalo = "Marque repetição por km, por meses ou ambos.";
    if (porKm && baseKm.trim() !== "" && parseKm(baseKm) == null)
      e.baselineKm = "Use apenas números — ex.: 61.500.";
    return e;
  }

  async function onSubmit(ev: FormEvent) {
    ev.preventDefault();
    const e = validate();
    setErrs(e);
    setFormError(null);
    if (Object.values(e).some(Boolean)) return;

    const input: TemplateInput = {
      titulo: titulo.trim(),
      categoria: categoria.trim() || null,
      intervaloKm: porKm ? parseKm(km) : null,
      intervaloMeses: porMeses ? parseKm(meses) : null,
      custoEstimado: custo.trim() !== "" ? parseBRL(custo) : null,
      baselineOdometro: porKm && baseKm.trim() !== "" ? parseKm(baseKm) : null,
      baselineData: baseData || null, // vazio → o servidor usa hoje
    };

    setSending(true);
    try {
      if (editing && template != null) await updateTemplate(template.id, input);
      else await createTemplate(asset.id, input);
      navigate(`/ativos/${asset.id}`);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrs({
          titulo: fieldMessage(err.errors, "titulo"),
          categoria: fieldMessage(err.errors, "categoria"),
          custo: fieldMessage(err.errors, "custoEstimado"),
          intervaloKm: fieldMessage(err.errors, "intervaloKm"),
          intervaloMeses: fieldMessage(err.errors, "intervaloMeses"),
          baselineKm: fieldMessage(err.errors, "baselineOdometro"),
        });
        setFormError(leftoverMessage(err.errors, CAMPOS, err.title));
      } else {
        setFormError("Algo deu errado. Tente de novo.");
      }
    } finally {
      setSending(false);
    }
  }

  async function onDelete() {
    if (!template) return;
    setDeleting(true);
    setDeleteError(null);
    try {
      await deleteTemplate(template.id);
      navigate(`/ativos/${asset.id}`);
    } catch (err) {
      setDeleting(false);
      // 409 (possui serviços vinculados) chega como title do Problem
      setDeleteError(err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.");
    }
  }

  return (
    <>
      <h1>{editing ? "Editar manutenção" : "Nova manutenção"}</h1>
      <p className="form-sub">{asset.nome}</p>

      <form onSubmit={onSubmit} noValidate>
        <Field htmlFor="titulo" label="Título" error={errs.titulo}>
          <input
            id="titulo"
            name="titulo"
            type="text"
            value={titulo}
            onChange={(e) => setTitulo(e.target.value)}
            aria-invalid={errs.titulo !== undefined}
          />
        </Field>

        <Field htmlFor="categoria" label="Categoria (opcional)" error={errs.categoria}>
          <input
            id="categoria"
            name="categoria"
            type="text"
            value={categoria}
            onChange={(e) => setCategoria(e.target.value)}
            aria-invalid={errs.categoria !== undefined}
          />
        </Field>

        <Field htmlFor="custo" label="Custo estimado (opcional)" error={errs.custo}>
          <input
            id="custo"
            name="custo"
            className="input-mono"
            type="text"
            inputMode="decimal"
            placeholder="350,50"
            value={custo}
            onChange={(e) => setCusto(e.target.value)}
            onBlur={() => {
              const v = parseBRL(custo);
              if (v !== null) setCusto(formatBRLInput(v));
            }}
            aria-invalid={errs.custo !== undefined}
          />
        </Field>

        <div className="field">
          <label className="check" htmlFor="porKm">
            <input
              id="porKm"
              type="checkbox"
              checked={porKm}
              onChange={(e) => setPorKm(e.target.checked)}
            />
            Repetir por km
          </label>
          {porKm && (
            <input
              id="intervaloKm"
              name="intervaloKm"
              className="input-mono"
              type="text"
              inputMode="numeric"
              placeholder="10.000"
              value={km}
              onChange={(e) => setKm(e.target.value)}
              onBlur={(e) => onBlurKm(setKm)(e.target.value)}
              aria-label="Intervalo em km"
              aria-invalid={errs.intervaloKm !== undefined}
            />
          )}
          {porKm && errs.intervaloKm && <p className="field__error">{errs.intervaloKm}</p>}
        </div>

        <div className="field">
          <label className="check" htmlFor="porMeses">
            <input
              id="porMeses"
              type="checkbox"
              checked={porMeses}
              onChange={(e) => setPorMeses(e.target.checked)}
            />
            Repetir por meses
          </label>
          {porMeses && (
            <input
              id="intervaloMeses"
              name="intervaloMeses"
              className="input-mono"
              type="text"
              inputMode="numeric"
              placeholder="6"
              value={meses}
              onChange={(e) => setMeses(e.target.value)}
              aria-label="Intervalo em meses"
              aria-invalid={errs.intervaloMeses !== undefined}
            />
          )}
          {porMeses && errs.intervaloMeses && <p className="field__error">{errs.intervaloMeses}</p>}
        </div>

        {errs.intervalo && (
          <p className="form-error" role="alert">
            {errs.intervalo}
          </p>
        )}

        {porKm && (
          <Field htmlFor="baseKm" label="Km base (opcional)" error={errs.baselineKm}>
            <input
              id="baseKm"
              name="baseKm"
              className="input-mono"
              type="text"
              inputMode="numeric"
              placeholder="61.500"
              value={baseKm}
              onChange={(e) => setBaseKm(e.target.value)}
              onBlur={(e) => onBlurKm(setBaseKm)(e.target.value)}
              aria-invalid={errs.baselineKm !== undefined}
            />
          </Field>
        )}

        <Field htmlFor="baseData" label="Data base (vazio usa hoje)">
          <input
            id="baseData"
            name="baseData"
            type="date"
            value={baseData}
            onChange={(e) => setBaseData(e.target.value)}
          />
        </Field>

        {formError && (
          <p className="form-error" role="alert">
            {formError}
          </p>
        )}

        <div className="form-actions">
          <button type="submit" className="btn btn--primary" disabled={sending}>
            {sending ? "Salvando…" : "Salvar manutenção"}
          </button>
          <Link className="link" to={`/ativos/${asset.id}`}>
            Cancelar
          </Link>
        </div>
      </form>

      {editing && template != null && (
        <div className="wo-footer">
          {confirming ? (
            <div className="wo-confirm" role="alert">
              <p>Excluir {template.titulo}?</p>
              <div className="wo-confirm__actions">
                <button
                  type="button"
                  className="btn btn--danger"
                  onClick={onDelete}
                  disabled={deleting}
                >
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
            <button
              type="button"
              className="link link--danger"
              onClick={() => setConfirming(true)}
            >
              Excluir manutenção
            </button>
          )}
        </div>
      )}
    </>
  );
}
