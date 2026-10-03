// A fração da régua: quanto do intervalo já foi consumido (0..1). É o dado por
// trás do "mostrador" (gauge) dos stickers — posição do ponteiro, não só a cor.
const DAY_MS = 86_400_000;

/** O que o gauge precisa de um template (AssetTemplate satisfaz). */
export interface GaugeInput {
  intervaloKm: number | null;
  kmRemaining: number | null;
  /** DateOnly "YYYY-MM-DD". */
  baselineData: string | null;
  /** DateOnly "YYYY-MM-DD". */
  dateDue: string | null;
}

/** Diferença em dias entre duas DateOnly ISO (parse UTC: sem drift de fuso). */
function dayDiff(later: string, earlier: string): number {
  return (Date.parse(later) - Date.parse(earlier)) / DAY_MS;
}

function clamp01(n: number): number {
  return Math.min(1, Math.max(0, n));
}

/**
 * Fração consumida do intervalo: km primeiro (régua do odômetro), senão tempo
 * (hoje entre baseline e vencimento). Estouro (>1) satura em 1 — o ponteiro no
 * fim + a cor do status é que contam o "venceu". null quando não há régua
 * possível (sem intervalo conhecido).
 */
export function gaugeFraction(t: GaugeInput, today: string): number | null {
  if (t.intervaloKm != null && t.intervaloKm > 0 && t.kmRemaining != null) {
    return clamp01((t.intervaloKm - t.kmRemaining) / t.intervaloKm);
  }
  if (t.baselineData && t.dateDue && t.dateDue > t.baselineData) {
    return clamp01(dayDiff(today, t.baselineData) / dayDiff(t.dateDue, t.baselineData));
  }
  return null;
}
