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
