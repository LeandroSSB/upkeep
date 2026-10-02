// Digitação pt-BR de valor: ponto = milhar, vírgula = decimal. O espelho do
// parseKm para centavos — "350,50" e "1.234,56" são o caso real.
import { describe, expect, it } from "vitest";
import { formatBRLInput, parseBRL } from "./money";

describe("parseBRL", () => {
  it("inteiro e centavos com vírgula", () => {
    expect(parseBRL("350")).toBe(350);
    expect(parseBRL("350,50")).toBe(350.5);
    expect(parseBRL("0")).toBe(0);
    expect(parseBRL("0,99")).toBe(0.99);
    expect(parseBRL("350,5")).toBe(350.5);
  });

  it("milhar com ponto, prefixo R$ e espaços", () => {
    expect(parseBRL("1.234,56")).toBe(1234.56);
    expect(parseBRL("1.234")).toBe(1234);
    expect(parseBRL("10.000")).toBe(10000);
    // caso ambíguo de 2 dígitos: hábito US "350.50" também é milhar (reagrupa no blur)
    expect(parseBRL("350.50")).toBe(35050);
    expect(parseBRL("R$ 350,50")).toBe(350.5);
    expect(parseBRL("R$350")).toBe(350);
    expect(parseBRL(" r$ 1.000,00 ")).toBe(1000);
  });

  it("mais de 2 decimais ou vírgula dupla rejeita", () => {
    expect(parseBRL("12,345")).toBeNull();
    expect(parseBRL("350,5,5")).toBeNull();
    expect(parseBRL(",50")).toBeNull();
  });

  it("não-número rejeita", () => {
    expect(parseBRL("")).toBeNull();
    expect(parseBRL("abc")).toBeNull();
    expect(parseBRL("-5")).toBeNull();
    expect(parseBRL("350;drop")).toBeNull();
    expect(parseBRL("grátis")).toBeNull();
  });
});

describe("formatBRLInput", () => {
  it("pt-BR com 2 casas, sem R$", () => {
    expect(formatBRLInput(1234.56)).toBe("1.234,56");
    expect(formatBRLInput(350)).toBe("350,00");
    expect(formatBRLInput(350.5)).toBe("350,50");
  });

  it("null vira string vazia (campo opcional)", () => {
    expect(formatBRLInput(null)).toBe("");
  });

  it("round-trip: o que formatou reparsa igual", () => {
    expect(parseBRL(formatBRLInput(1234.56))).toBe(1234.56);
  });
});
