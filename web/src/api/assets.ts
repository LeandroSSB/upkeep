// Espelha os DTOs da API (AssetDtos.cs / TemplateEndpoints.cs, serializados em camelCase).
import type { AssetTipo, Status } from "../lib/types";
import { apiFetch } from "./client";

export interface Asset {
  id: string;
  nome: string;
  tipo: AssetTipo;
  odometroAtual: number | null;
  notas: string | null;
  /** Agregado dos templates; null nos endpoints que não calculam (GET /assets sempre preenche). */
  statusAgregado: Status | null;
  overdue: number;
  dueSoon: number;
  ok: number;
}

export async function listAssets(): Promise<Asset[]> {
  return apiFetch<Asset[]>("/assets");
}

export interface AssetTemplate {
  id: string;
  assetId: string;
  titulo: string;
  categoria: string | null;
  intervaloKm: number | null;
  intervaloMeses: number | null;
  custoEstimado: number | null;
  baselineOdometro: number | null;
  /** DateOnly → "YYYY-MM-DD". */
  baselineData: string;
  status: Status | null;
  kmRemaining: number | null;
  /** DateOnly → "YYYY-MM-DD". */
  dateDue: string | null;
}

export async function listTemplates(assetId: string): Promise<AssetTemplate[]> {
  return apiFetch<AssetTemplate[]>(`/assets/${assetId}/templates`);
}
