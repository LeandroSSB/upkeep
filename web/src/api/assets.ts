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

export interface AssetInput {
  nome: string;
  tipo: AssetTipo;
  /** Só é aceito para veículo; null nos demais tipos. */
  odometroAtual: number | null;
  notas: string | null;
}

export async function createAsset(input: AssetInput): Promise<Asset> {
  return apiFetch<Asset>("/assets", { method: "POST", body: JSON.stringify(input) });
}

/** PUT /assets/{id} substitui nome e notas (tipo não muda; odômetro é na tela do ativo). */
export async function updateAsset(id: string, input: { nome: string; notas: string | null }): Promise<Asset> {
  return apiFetch<Asset>(`/assets/${id}`, { method: "PUT", body: JSON.stringify(input) });
}

export interface TemplateInput {
  titulo: string;
  categoria: string | null;
  intervaloKm: number | null;
  intervaloMeses: number | null;
  custoEstimado: number | null;
  baselineOdometro: number | null;
  /** DateOnly "YYYY-MM-DD"; null → o servidor usa hoje. */
  baselineData: string | null;
}

export async function createTemplate(assetId: string, input: TemplateInput): Promise<AssetTemplate> {
  return apiFetch<AssetTemplate>(`/assets/${assetId}/templates`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

/** PUT /templates/{id} substitui todos os campos (mesma shape do create). */
export async function updateTemplate(id: string, input: TemplateInput): Promise<AssetTemplate> {
  return apiFetch<AssetTemplate>(`/templates/${id}`, { method: "PUT", body: JSON.stringify(input) });
}

/** DELETE /templates/{id} → 204. 409 quando há serviços vinculados (FK restrict). */
export async function deleteTemplate(id: string): Promise<void> {
  return apiFetch<void>(`/templates/${id}`, { method: "DELETE" });
}

/**
 * POST /assets/{id}/odometer. Regra não-regressiva é do servidor: valor menor que
 * o atual → 400 com errors.odometer (o form mostra inline). Resposta é AssetResponse
 * sem os agregados (statusAgregado etc. null) — use só os campos básicos.
 */
export async function updateOdometer(assetId: string, odometer: number): Promise<Asset> {
  return apiFetch<Asset>(`/assets/${assetId}/odometer`, {
    method: "POST",
    body: JSON.stringify({ odometer }),
  });
}

/** DELETE /assets/{id} → 204. Apaga o asset com templates e histórico (cascade). */
export async function deleteAsset(assetId: string): Promise<void> {
  return apiFetch<void>(`/assets/${assetId}`, { method: "DELETE" });
}
