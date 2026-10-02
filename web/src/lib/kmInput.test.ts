// O ponto central do carry-fix da Task 4: `type="number"` + Number("61.500")
// = 61.5 → Math.trunc → 61 (dado errado silencioso). Aqui ponto/espaço são
// SEMPRE milhar pt-BR e nada além de dígitos passa.
import { describe, expect, it } from "vitest";
import { formatKmInput, parseKm } from "./kmInput";

describe("parseKm", () => {
  it("dígitos puros viram inteiro", () => {
    expect(parseKm("61500")).toBe(61500);
    expect(parseKm("1500")).toBe(1500);
    expect(parseKm("320")).toBe(320);
    expect(parseKm("0")).toBe(0);
    expect(parseKm("007")).toBe(7);
  });

  it("ponto e espaço são separadores de milhar (hábito pt-BR)", () => {
    expect(parseKm("61.500")).toBe(61500);
    expect(parseKm("1.500")).toBe(1500);
    expect(parseKm("61 500")).toBe(61500);
    expect(parseKm(" 61.500 ")).toBe(61500);
  });

  it("ponto 'decimal' é milhar: '12.5' lê 125 — nunca 12,5 nem 12", () => {
    expect(parseKm("12.5")).toBe(125);
    expect(parseKm("61.500")).not.toBe(61.5);
  });

  it("vírgula, sinal e não-dígitos rejeitam (null)", () => {
    expect(parseKm("")).toBeNull();
    expect(parseKm("   ")).toBeNull();
    expect(parseKm("12,5")).toBeNull();
    expect(parseKm("-10")).toBeNull();
    expect(parseKm("61.5a0")).toBeNull();
    expect(parseKm("km")).toBeNull();
  });

  it("além do safe integer rejeita (odômetro absurdo)", () => {
    expect(parseKm("9".repeat(20))).toBeNull();
  });
});

describe("formatKmInput", () => {
  it("agrupa milhar pt-BR sem unidade", () => {
    expect(formatKmInput(61500)).toBe("61.500");
    expect(formatKmInput(1500)).toBe("1.500");
    expect(formatKmInput(320)).toBe("320");
    expect(formatKmInput(0)).toBe("0");
  });

  it("null vira string vazia (campo opcional)", () => {
    expect(formatKmInput(null)).toBe("");
  });

  it("round-trip: o que formatou reparsa igual", () => {
    expect(parseKm(formatKmInput(61500))).toBe(61500);
  });
});
