// POST /import — restaura um arquivo de export (M7): ADITIVO (ids novos, dono
// = usuário atual). O body é o texto cru do arquivo; o content-type JSON é
// posto pelo client (qualquer body não-nulo recebe application/json).
import { apiFetch } from "./client";

export interface Importados {
  assets: number;
  templates: number;
  services: number;
}

export interface ImportResult {
  importados: Importados;
}

export function importBackup(fileText: string): Promise<ImportResult> {
  return apiFetch("/import", { method: "POST", body: fileText });
}
