// GET /reports/costs — total gasto e quebra por ativo no período (bounds
// inclusivos). Decimal chega serializado como number (como o custo dos serviços).
import { apiFetch } from "./client";

export interface CostByAsset {
  assetId: string;
  nome: string;
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
}

export interface CostFilters {
  assetId?: string;
  from?: string;
  to?: string;
}

/** Parâmetro vazio não entra na query — a API trata ausente como "sem filtro". */
export async function getCostReport(filters: CostFilters = {}): Promise<CostReport> {
  const params = new URLSearchParams();
  if (filters.assetId) params.set("assetId", filters.assetId);
  if (filters.from) params.set("from", filters.from);
  if (filters.to) params.set("to", filters.to);
  const qs = params.toString();
  return apiFetch<CostReport>(`/reports/costs${qs ? `?${qs}` : ""}`);
}
