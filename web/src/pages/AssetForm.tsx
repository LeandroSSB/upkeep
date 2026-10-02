// Novo/editar ativo. Criar: nome, tipo e odômetro (só veículo). Editar: nome e
// notas — o tipo não muda na API e o odômetro atualiza pela tela do ativo.
import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  createAsset,
  listAssets,
  updateAsset,
  type Asset,
  type AssetInput,
} from "../api/assets";
import { ApiError } from "../api/client";
import { AppShell } from "../components/AppShell";
import { Field } from "../components/Field";
import { typeLabel } from "../lib/format";
import { fieldMessage, leftoverMessage } from "../lib/formErrors";
import { formatKmInput, parseKm } from "../lib/kmInput";
import type { AssetTipo } from "../lib/types";

const TIPOS: readonly AssetTipo[] = ["veiculo", "casa", "aparelho"];
const CAMPOS = ["nome", "tipo", "odometroAtual", "odometer"];

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "notfound" }
  | { kind: "ready"; asset: Asset | null };

export default function AssetForm() {
  const { id } = useParams<{ id: string }>();
  const editing = id != null;
  const [phase, setPhase] = useState<Phase>(editing ? { kind: "loading" } : { kind: "ready", asset: null });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!editing || !id) return;
    let cancelled = false;
    setPhase({ kind: "loading" });
    // sem GET por id na API: o asset vem da lista do usuário (mesmo padrão do detail)
    listAssets()
      .then((assets) => {
        if (cancelled) return;
        const asset = assets.find((a) => a.id === id);
        setPhase(asset ? { kind: "ready", asset } : { kind: "notfound" });
      })
      .catch((err) => {
        if (cancelled) return;
        setPhase({
          kind: "error",
          message: err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.",
        });
      });
    return () => {
      cancelled = true;
    };
  }, [id, editing, attempt]);

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
            <p>Ativo não encontrado.</p>
            <Link className="btn" to="/">
              Voltar para a lista
            </Link>
          </div>
        )}

        {phase.kind === "ready" && (
          <AssetFields key={phase.asset?.id ?? "novo"} asset={phase.asset} assetId={id} />
        )}
      </>
    </AppShell>
  );
}

type FieldErrs = { nome?: string; tipo?: string; odometro?: string };

function AssetFields({ asset, assetId }: { asset: Asset | null; assetId?: string }) {
  const editing = asset != null;
  const navigate = useNavigate();
  const [nome, setNome] = useState(asset?.nome ?? "");
  const [tipo, setTipo] = useState<AssetTipo>(asset?.tipo ?? "veiculo");
  const [odometro, setOdometro] = useState(
    asset?.odometroAtual != null ? formatKmInput(asset.odometroAtual) : "",
  );
  const [notas, setNotas] = useState(asset?.notas ?? "");
  const [errs, setErrs] = useState<FieldErrs>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  function onBlurKm() {
    const km = parseKm(odometro);
    if (km != null) setOdometro(formatKmInput(km));
  }

  function validate(): FieldErrs {
    const e: FieldErrs = {};
    const n = nome.trim();
    if (!n) e.nome = "Informe o nome do ativo.";
    else if (n.length > 200) e.nome = "O nome pode ter até 200 caracteres.";
    if (!editing && tipo === "veiculo" && odometro.trim() !== "" && parseKm(odometro) == null)
      e.odometro = "Use apenas números — ex.: 61.500.";
    return e;
  }

  async function onSubmit(ev: FormEvent) {
    ev.preventDefault();
    const e = validate();
    setErrs(e);
    setFormError(null);
    if (e.nome || e.tipo || e.odometro) return;

    setSending(true);
    try {
      if (editing && assetId != null) {
        await updateAsset(assetId, { nome: nome.trim(), notas: notas.trim() || null });
        navigate(`/ativos/${assetId}`);
      } else {
        const input: AssetInput = {
          nome: nome.trim(),
          tipo,
          odometroAtual: tipo === "veiculo" ? parseKm(odometro) : null,
          notas: notas.trim() || null,
        };
        const created = await createAsset(input);
        navigate(`/ativos/${created.id}`);
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setErrs({
          nome: fieldMessage(err.errors, "nome"),
          tipo: fieldMessage(err.errors, "tipo"),
          odometro: fieldMessage(err.errors, "odometroAtual") ?? fieldMessage(err.errors, "odometer"),
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
      <h1>{editing ? "Editar ativo" : "Novo ativo"}</h1>
      {editing && asset && (
        <p className="form-sub">
          Tipo: {typeLabel(asset.tipo)} — o odômetro atualiza na tela do ativo.
        </p>
      )}

      <form onSubmit={onSubmit} noValidate>
        <Field htmlFor="nome" label="Nome" error={errs.nome}>
          <input
            id="nome"
            name="nome"
            type="text"
            value={nome}
            onChange={(e) => setNome(e.target.value)}
            aria-invalid={errs.nome !== undefined}
          />
        </Field>

        {!editing && (
          <Field htmlFor="tipo" label="Tipo" error={errs.tipo}>
            <select
              id="tipo"
              name="tipo"
              value={tipo}
              onChange={(e) => setTipo(e.target.value as AssetTipo)}
            >
              {TIPOS.map((t) => (
                <option key={t} value={t}>
                  {typeLabel(t)}
                </option>
              ))}
            </select>
          </Field>
        )}

        {!editing && tipo === "veiculo" && (
          <Field htmlFor="odometro" label="Odômetro atual (opcional)" error={errs.odometro}>
            <input
              id="odometro"
              name="odometro"
              className="input-mono"
              type="text"
              inputMode="numeric"
              placeholder="61.500"
              value={odometro}
              onChange={(e) => setOdometro(e.target.value)}
              onBlur={onBlurKm}
              aria-invalid={errs.odometro !== undefined}
            />
          </Field>
        )}

        <Field htmlFor="notas" label="Notas (opcional)">
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
            {sending ? "Salvando…" : "Salvar ativo"}
          </button>
          <Link className="link" to={editing && assetId != null ? `/ativos/${assetId}` : "/"}>
            Cancelar
          </Link>
        </div>
      </form>
    </>
  );
}
