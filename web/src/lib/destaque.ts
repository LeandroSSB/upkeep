// A linha "próximo vencimento" das plaquetas de manutenção — e qual template é o
// pior de um asset (alimenta o herói da Home). Puro: "today" entra como
// parâmetro para o teste não depender do relógio.
import { formatDate, formatKm } from "./format";
import type { Status } from "./types";

/** O que a plaqueta de um template precisa para derivar o destaque (AssetTemplate satisfaz). */
export interface TemplateDue {
  status: Status | null;
  kmRemaining: number | null;
  /** DateOnly → "YYYY-MM-DD"; vencido quando <= today. */
  dateDue: string | null;
}

/** Data local de hoje como "YYYY-MM-DD" (mesmo formato da API). */
export function todayIso(now: Date = new Date()): string {
  const m = String(now.getMonth() + 1).padStart(2, "0");
  const d = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${m}-${d}`;
}

/**
 * Destaque da plaqueta conforme o status:
 * - vencido: "venceu {data}" quando o critério tempo venceu (dateDue já passou);
 *   "estourou {X} km" quando o km passou do limite (kmRemaining <= 0 — cobre o
 *   caso km estourado com dateDue ainda futura, onde "venceu {data futura}" mentiria).
 * - vence_em_breve: "vence em {data}" ou "vence em {X} km".
 * - ok: "ok · vence em {data}/{X} km" (o que existir).
 */
export function templateDestaque(t: TemplateDue, today: string): string | null {
  switch (t.status) {
    case "vencido": {
      if (t.dateDue && t.dateDue <= today) return `venceu ${formatDate(t.dateDue)}`;
      if (t.kmRemaining != null && t.kmRemaining <= 0)
        return `estourou ${formatKm(Math.abs(t.kmRemaining))}`;
      if (t.dateDue) return `venceu ${formatDate(t.dateDue)}`;
      return "venceu";
    }
    case "vence_em_breve": {
      if (t.dateDue) return `vence em ${formatDate(t.dateDue)}`;
      if (t.kmRemaining != null) return `vence em ${formatKm(t.kmRemaining)}`;
      return "vence em breve";
    }
    case "ok": {
      if (t.dateDue) return `ok · vence em ${formatDate(t.dateDue)}`;
      if (t.kmRemaining != null) return `ok · vence em ${formatKm(t.kmRemaining)}`;
      return "ok";
    }
    default:
      return null;
  }
}

function statusRank(status: Status | null): number {
  if (status === "vencido") return 0;
  if (status === "vence_em_breve") return 1;
  return 2;
}

/** menor = mais urgente: status, depois dateDue (ISO compara em ordem cronológica;
 * datas vencidas são naturalmente menores que as futuras), depois km restante. */
function compareUrgency(a: TemplateDue, b: TemplateDue): number {
  const rank = statusRank(a.status) - statusRank(b.status);
  if (rank !== 0) return rank;
  if (a.dateDue !== b.dateDue) {
    if (a.dateDue === null) return 1;
    if (b.dateDue === null) return -1;
    return a.dateDue < b.dateDue ? -1 : 1;
  }
  if (a.kmRemaining !== b.kmRemaining) {
    if (a.kmRemaining === null) return 1;
    if (b.kmRemaining === null) return -1;
    return a.kmRemaining - b.kmRemaining;
  }
  return 0;
}

/** O pior template não-ok do asset (vencido antes de vence_em_breve; entre iguais,
 *  o mais atrasado). null quando só há "ok" (ou nada) — quem chamou decide o fallback. */
export function worstTemplate<T extends TemplateDue>(templates: readonly T[]): T | null {
  const pending = templates.filter((t) => t.status === "vencido" || t.status === "vence_em_breve");
  if (pending.length === 0) return null;
  return pending.reduce((worst, t) => (compareUrgency(t, worst) < 0 ? t : worst));
}
