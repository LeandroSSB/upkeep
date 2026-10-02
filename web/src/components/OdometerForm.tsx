// Campo inline de "atualizar km": número + Salvar/Cancelar. Erro do servidor
// (odômetro não-regressivo → 400) aparece sob o campo; sucesso sobe para o pai.
import { useState, type FormEvent } from "react";
import { updateOdometer } from "../api/assets";
import { ApiError } from "../api/client";

/** Primeira mensagem do ProblemDetails (qualquer chave), senão o title. */
function firstServerMessage(err: ApiError): string {
  const messages = Object.values(err.errors ?? {}).flat();
  return messages[0] ?? err.title;
}

export function OdometerForm({
  assetId,
  current,
  onSaved,
  onCancel,
}: {
  assetId: string;
  current: number | null;
  onSaved: (km: number) => void;
  onCancel: () => void;
}) {
  const [value, setValue] = useState(current != null ? String(current) : "");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = value.trim();
    const km = Number(trimmed);
    if (trimmed === "" || !Number.isFinite(km)) {
      setError("Informe o km atual.");
      return;
    }
    if (km < 0) {
      setError("O km não pode ser negativo.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await updateOdometer(assetId, Math.trunc(km));
      onSaved(Math.trunc(km));
    } catch (err) {
      setError(err instanceof ApiError ? firstServerMessage(err) : "Algo deu errado. Tente de novo.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="km-form" onSubmit={onSubmit} noValidate>
      <input
        type="number"
        inputMode="numeric"
        min={0}
        step={1}
        aria-label="Odômetro atual"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        autoFocus
      />
      <button type="submit" className="btn btn--primary" disabled={saving}>
        {saving ? "Salvando…" : "Salvar"}
      </button>
      <button type="button" className="link" onClick={onCancel} disabled={saving}>
        Cancelar
      </button>
      {error && (
        <p className="km-form__error" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}
