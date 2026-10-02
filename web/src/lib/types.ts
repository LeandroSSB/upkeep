/** Status canônico da API (statusAgregado/status) — cor do sticker e label derivam daqui. */
export type Status = "ok" | "vence_em_breve" | "vencido";

/** Tipo do ativo. "veiculo" sem acento é o valor da API; "veículo" é só exibição (typeLabel). */
export type AssetTipo = "veiculo" | "casa" | "aparelho";
