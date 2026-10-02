// Mapeamento do ProblemDetails (ApiError.errors) para os formulários: chaves
// chegam PascalCase do FluentValidation ("Nome", "IntervaloKm"), às vezes
// lowercase do endpoint ("odometroAtual") e regras no root com chave vazia
// ("Informe intervalo_km e/ou intervalo_meses"). Caso-insensível; o que não
// pertence a campo conhecido sobe como erro do formulário.

/** Primeira mensagem do campo (case-insensitive) — ex.: fieldMessage(err.errors, "nome"). */
export function fieldMessage(
  errors: Record<string, string[]> | undefined,
  field: string,
): string | undefined {
  const target = field.toLowerCase();
  for (const [key, messages] of Object.entries(errors ?? {})) {
    if (key.toLowerCase() === target && messages.length > 0) return messages[0];
  }
  return undefined;
}

/**
 * Primeira mensagem que não pertence a nenhum campo conhecido (chave vazia das
 * regras no root, ou campo inesperado). Fallback é geralmente o title do Problem.
 */
export function leftoverMessage(
  errors: Record<string, string[]> | undefined,
  knownFields: readonly string[],
  fallback: string,
): string {
  const known = new Set(knownFields.map((f) => f.toLowerCase()));
  for (const [key, messages] of Object.entries(errors ?? {})) {
    if (!known.has(key.toLowerCase()) && messages.length > 0) return messages[0];
  }
  return fallback;
}
