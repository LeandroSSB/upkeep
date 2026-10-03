import { describe, expect, it } from "vitest";
import { gaugeFraction } from "./gauge";

const TODAY = "2026-10-03";

describe("gaugeFraction — régua por km", () => {
  it("consome 3/4 do intervalo", () => {
    expect(gaugeFraction({ intervaloKm: 10000, kmRemaining: 2500, baselineData: null, dateDue: null }, TODAY)).toBe(0.75);
  });
  it("km estourado satura em 1 (o ponteiro fica no fim; a cor conta o venceu)", () => {
    expect(gaugeFraction({ intervaloKm: 10000, kmRemaining: -500, baselineData: null, dateDue: null }, TODAY)).toBe(1);
  });
  it("km restante maior que o intervalo (baseline à frente) satura em 0", () => {
    expect(gaugeFraction({ intervaloKm: 10000, kmRemaining: 11000, baselineData: null, dateDue: null }, TODAY)).toBe(0);
  });
});

describe("gaugeFraction — régua por tempo", () => {
  it("hoje no meio do período (sem intervalo por km)", () => {
    const t = { intervaloKm: null, kmRemaining: null, baselineData: "2026-01-01", dateDue: "2027-01-01" };
    // 2026 não é bissexto: 03/out = dia 276 → 275 dias decorridos de 365
    expect(gaugeFraction(t, TODAY)).toBeCloseTo(275 / 365, 5);
  });
  it("hoje antes do baseline satura em 0", () => {
    const t = { intervaloKm: null, kmRemaining: null, baselineData: "2026-12-01", dateDue: "2027-12-01" };
    expect(gaugeFraction(t, TODAY)).toBe(0);
  });
  it("baseline == vencimento não tem régua", () => {
    const t = { intervaloKm: null, kmRemaining: null, baselineData: "2026-01-01", dateDue: "2026-01-01" };
    expect(gaugeFraction(t, TODAY)).toBeNull();
  });
});

describe("gaugeFraction — sem régua possível", () => {
  it("tudo null", () => {
    expect(gaugeFraction({ intervaloKm: null, kmRemaining: null, baselineData: null, dateDue: null }, TODAY)).toBeNull();
  });
  it("intervaloKm sem kmRemaining cai na régua de tempo", () => {
    const t = { intervaloKm: 10000, kmRemaining: null, baselineData: "2026-01-01", dateDue: "2027-01-01" };
    expect(gaugeFraction(t, TODAY)).toBeCloseTo(275 / 365, 5);
  });
});
