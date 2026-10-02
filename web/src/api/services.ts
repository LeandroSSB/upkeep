// Espelha ServiceResponse da API (ServiceEndpoints.cs, camelCase). data é DateOnly
// ("YYYY-MM-DD"); custo é decimal serializado como número.
import { apiFetch } from "./client";

export interface Service {
  id: string;
  assetId: string;
  templateId: string | null;
  data: string;
  odometro: number | null;
  custo: number;
  notas: string | null;
  createdAt: string;
}

export async function listServices(assetId: string): Promise<Service[]> {
  return apiFetch<Service[]>(`/assets/${assetId}/services`);
}

export interface ServiceInput {
  /** null = serviço avulso. */
  templateId: string | null;
  /** DateOnly "YYYY-MM-DD". */
  data: string;
  odometro: number | null;
  custo: number;
  notas: string | null;
}

/** POST /assets/{assetId}/services. Odômetro maior que o atual avança o km do ativo. */
export async function createService(assetId: string, input: ServiceInput): Promise<Service> {
  return apiFetch<Service>(`/assets/${assetId}/services`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}
