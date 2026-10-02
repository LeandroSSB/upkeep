// Campo inline de "atualizar km": texto + parseKm (à prova do hábito pt-BR —
// "61.500" é milhar, nunca decimal) + Salvar/Cancelar. Erro do servidor
// (odômetro não-regressivo → 400) aparece sob o campo; sucesso sobe para o pai.
import { useState, type FormEvent } from "react";
import { updateOdometer } from "../api/assets";
import { ApiError } from "../api/client";
import { formatKmInput, parseKm } from "../lib/kmInput";

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
  const [value, setValue] = useState(current != null ? formatKmInput(current) : "");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (value.trim() === "") {
      setError("Informe o km atual.");
      return;
    }
    const km = parseKm(value); // "61.500" → 61500; vírgula/letras → null
    if (km == null) {
      setError("Use apenas números — ex.: 61.500.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await updateOdometer(assetId, km);
      onSaved(km);
    } catch (err) {
      setError(err instanceof ApiError ? firstServerMessage(err) : "Algo deu errado. Tente de novo.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form className="km-form" onSubmit={onSubmit} noValidate>
      <input
        type="text"
        inputMode="numeric"
        aria-label="Odômetro atual"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onBlur={() => {
          const km = parseKm(value);
          if (km != null) setValue(formatKmInput(km)); // reagrupa "61500" → "61.500"
        }}
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
