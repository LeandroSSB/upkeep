// O ProblemDetails da API traz chaves PascalCase do FluentValidation ("Nome",
// "IntervaloKm"), às vezes lowercase do endpoint ("odometroAtual") e regras no
// root com chave vazia ("Informe intervalo_km e/ou intervalo_meses").
import { describe, expect, it } from "vitest";
import { fieldMessage, leftoverMessage } from "./formErrors";

describe("fieldMessage", () => {
  it("casa chave PascalCase da API com campo camelCase do form", () => {
    expect(fieldMessage({ Nome: ["Nome é obrigatório"] }, "nome")).toBe("Nome é obrigatório");
    expect(fieldMessage({ IntervaloKm: ["deve ser > 0"] }, "intervaloKm")).toBe("deve ser > 0");
    expect(fieldMessage({ odometroAtual: ["só veículo"] }, "odometroAtual")).toBe("só veículo");
  });

  it("campo ausente ou lista vazia → undefined", () => {
    expect(fieldMessage({ Nome: ["x"] }, "titulo")).toBeUndefined();
    expect(fieldMessage({ Nome: [] }, "nome")).toBeUndefined();
    expect(fieldMessage(undefined, "nome")).toBeUndefined();
  });
});

describe("leftoverMessage", () => {
  it("mensagem de chave vazia (regra no root) vira erro do form", () => {
    const errors = { "": ["Informe intervalo_km e/ou intervalo_meses"], Titulo: ["x"] };
    expect(leftoverMessage(errors, ["titulo"], "fallback")).toBe(
      "Informe intervalo_km e/ou intervalo_meses",
    );
  });

  it("chave inesperada também sobe para o form", () => {
    expect(leftoverMessage({ Surpresa: ["?"] }, ["titulo"], "fallback")).toBe("?");
  });

  it("tudo casou / sem errors → fallback (normalmente o title do Problem)", () => {
    expect(leftoverMessage({ Titulo: ["x"] }, ["titulo"], "fallback")).toBe("fallback");
    expect(leftoverMessage(undefined, [], "fallback")).toBe("fallback");
  });
});
