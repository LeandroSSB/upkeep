// Datas como strings fixas: o resultado não pode depender do fuso do ambiente
// que roda o vitest (parse/format em UTC dentro do formatDate).
import { describe, expect, it } from "vitest";
import { formatBRL, formatDate, formatKm, statusLabel, typeLabel } from "./format";

describe("formatKm", () => {
  it("separa milhar pt-BR e acrescenta a unidade", () => {
    expect(formatKm(58500)).toBe("58.500 km");
    expect(formatKm(1500)).toBe("1.500 km");
    expect(formatKm(320)).toBe("320 km");
    expect(formatKm(0)).toBe("0 km");
  });
});

describe("formatBRL", () => {
  it("moeda pt-BR do Intl, com espaço normalizado", () => {
    expect(formatBRL(1234.56)).toBe("R$ 1.234,56");
    expect(formatBRL(123456.78)).toBe("R$ 123.456,78");
    expect(formatBRL(250)).toBe("R$ 250,00");
    expect(formatBRL(0)).toBe("R$ 0,00");
  });
});

describe("formatDate", () => {
  it("dia numérico, mês abreviado sem ponto, ano — '21 mar 2027'", () => {
    expect(formatDate("2027-03-21")).toBe("21 mar 2027");
    expect(formatDate("2027-01-01")).toBe("1 jan 2027");
    expect(formatDate("2027-12-31")).toBe("31 dez 2027");
    expect(formatDate("2026-09-14")).toBe("14 set 2026");
  });

  it("entrada inválida volta como veio", () => {
    expect(formatDate("não-data")).toBe("não-data");
  });
});

describe("statusLabel", () => {
  it("labels de exibição dos status", () => {
    expect(statusLabel("vencido")).toBe("vencido");
    expect(statusLabel("vence_em_breve")).toBe("vence em breve");
    expect(statusLabel("ok")).toBe("ok");
  });
});

describe("typeLabel", () => {
  it("tipo da API (sem acento) vira label de exibição", () => {
    expect(typeLabel("veiculo")).toBe("veículo");
    expect(typeLabel("casa")).toBe("casa");
    expect(typeLabel("aparelho")).toBe("aparelho");
  });
});
