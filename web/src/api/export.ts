// GET /export — JSON completo dos dados do usuário, pego como blob para download.
// (M6: posse de dados/backup. O backend serve com content-disposition
// attachment; o nome do arquivo sai de lá.)
import { apiFetchRaw } from "./client";

export interface ExportDownload {
  blob: Blob;
  /** filename do content-disposition; fallback se o header não vier. */
  filename: string;
}

export async function exportUserData(): Promise<ExportDownload> {
  const res = await apiFetchRaw("/export");
  const match = res.headers.get("content-disposition")?.match(/filename="?([^";]+)"?/);
  return { blob: await res.blob(), filename: match?.[1] ?? "upkeep-export.json" };
}
