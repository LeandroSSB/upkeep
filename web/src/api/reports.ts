// GET /reports/costs — total gasto e quebra por ativo no período (bounds
// inclusivos). Decimal chega serializado como number (como o custo dos serviços).
import { apiFetch } from "./client";

export interface CostByAsset {
  assetId: string;
  nome: string;
  total: number;
  quantidade: number;
}

export interface CostByMonth {
  /** "yyyy-MM" — régua cronológica de até 12 meses, zeros incluídos. */
  mes: string;
  total: number;
  quantidade: number;
}

export interface CostReport {
  total: number;
  /** Ordenado pela API por total desc. */
  porAsset: CostByAsset[];
  /** DateOnly "YYYY-MM-DD"; null = o filtro não foi enviado neste lado. */
  de: string | null;
  ate: string | null;
  /** Presente apenas com groupBy=month; sem o parâmetro a API devolve null. */
  porMes: CostByMonth[] | null;
}

export interface CostFilters {
  assetId?: string;
  from?: string;
  to?: string;
  /** Único valor suportado hoje: "month" (soma a régua mensal à resposta). */
  groupBy?: "month";
}

/** Parâmetro vazio não entra na query — a API trata ausente como "sem filtro". */
export async function getCostReport(filters: CostFilters = {}): Promise<CostReport> {
  const params = new URLSearchParams();
  if (filters.assetId) params.set("assetId", filters.assetId);
  if (filters.from) params.set("from", filters.from);
  if (filters.to) params.set("to", filters.to);
  if (filters.groupBy) params.set("groupBy", filters.groupBy);
  const qs = params.toString();
  return apiFetch<CostReport>(`/reports/costs${qs ? `?${qs}` : ""}`);
}
