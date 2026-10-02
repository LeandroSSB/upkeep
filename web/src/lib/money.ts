// Digitação de valor em pt-BR (mesma filosofia do kmInput): ponto = milhar,
// vírgula = decimal. "R$" opcional. Máximo 2 casas — "12,345" é ambíguo e
// rejeita. Inputs de custo usam type="text" + inputMode="decimal" + isto.

/** "350"/"350,50"/"1.234,56"/"R$ 350" → número; resto → null. */
export function parseBRL(raw: string): number | null {
  const s = raw.replace(/\s/g, "").replace(/^r\$/i, "");
  const normalized = s.replace(/\./g, "").replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  const n = Number(normalized);
  return Number.isFinite(n) ? n : null;
}

/** 1234.5 → "1.234,50" (2 casas, sem "R$") — para exibir dentro do input. */
export function formatBRLInput(n: number | null): string {
  if (n == null) return "";
  return n.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
