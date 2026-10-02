// Formatação pt-BR (moeda, data, km) e labels de exibição — funções puras.
import type { AssetTipo, Status } from "./types";

// O Intl usa espaço inseparável (U+00A0) entre "R$" e o valor; normalizamos para
// espaço comum: string previsível em teste e em snapshots de conteúdo.
const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

export function formatBRL(n: number): string {
  return brl.format(n).replace(/\u00A0/g, " ");
}

export function formatKm(n: number): string {
  return `${new Intl.NumberFormat("pt-BR").format(n)} km`;
}

// "YYYY-MM-DD" da API: parse e formato em UTC — o dia nunca desloca em quem está
// num fuso negativo (São Paulo é UTC-3: new Date("2027-03-21") local viraria 20/03).
const dueDate = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  year: "numeric",
  timeZone: "UTC",
});

export function formatDate(iso: string): string {
  const [year, month, day] = iso.split("-").map(Number);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (Number.isNaN(date.getTime())) return iso; // entrada fora do contrato volta como veio
  const parts = dueDate.formatToParts(date);
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? "";
  return `${get("day")} ${get("month").replace(".", "")} ${get("year")}`;
}

// "yyyy-MM" da API (mês, sem dia): "mar 2026". O pt-BR abrevia mês standalone
// com ponto ("mar.") e o format() composto insere um "de" ("mar. de 2026") —
// por isso recompomos as partes na mão, com o ponto limpo.
const monthYear = new Intl.DateTimeFormat("pt-BR", {
  month: "short",
  year: "numeric",
  timeZone: "UTC",
});

export function formatMonth(iso: string): string {
  const [year, month] = iso.split("-").map(Number);
  if (!year || !month) return iso; // entrada fora do contrato volta como veio
  const parts = monthYear.formatToParts(new Date(Date.UTC(year, month - 1, 1)));
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? "";
  return `${get("month").replace(".", "")} ${get("year")}`;
}

export function statusLabel(status: Status): string {
  switch (status) {
    case "vencido":
      return "vencido";
    case "vence_em_breve":
      return "vence em breve";
    default:
      return "ok";
  }
}

export function typeLabel(tipo: AssetTipo): string {
  switch (tipo) {
    case "veiculo":
      return "veículo";
    case "casa":
      return "casa";
    default:
      return "aparelho";
  }
}
