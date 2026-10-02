// Input de km à prova do hábito pt-BR: `type="number"` + Number("61.500")
// resulta em 61.5 (ponto como decimal) e um trunc() silencioso corrói o dado.
// Aqui ponto/espaço são SEMPRE separadores de milhar: "61.500" e "61500" dão
// 61500; "12.5" lê como 125 (intenção pt-BR de milhar); qualquer não-dígito
// rejeita. Inputs km usam type="text" + inputMode="numeric" + estas funções.

/** "61500"/"61.500"/"61 500" → 61500; vazio/não-dígito/fora de safe integer → null. */
export function parseKm(raw: string): number | null {
  const digits = raw.replace(/[.\s]/g, "");
  if (!/^\d+$/.test(digits)) return null;
  const n = Number.parseInt(digits, 10);
  return Number.isSafeInteger(n) ? n : null;
}

/** 61500 → "61.500" (agrupamento pt-BR, sem " km") — para exibir dentro do input. */
export function formatKmInput(n: number | null): string {
  if (n == null) return "";
  return new Intl.NumberFormat("pt-BR").format(n);
}
