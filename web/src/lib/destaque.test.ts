// "today" entra fixo ("2026-10-02"): o resultado não pode depender do relógio.
import { describe, expect, it } from "vitest";
import { templateDestaque, todayIso, worstTemplate, type TemplateDue } from "./destaque";

const HOJE = "2026-10-02";

describe("templateDestaque", () => {
  it("vencido por tempo: 'venceu {data}' com a data passada", () => {
    const t: TemplateDue = { status: "vencido", kmRemaining: null, dateDue: "2026-09-14" };
    expect(templateDestaque(t, HOJE)).toBe("venceu 14 set 2026");
  });

  it("vencido só por km: 'estourou {X} km'", () => {
    const t: TemplateDue = { status: "vencido", kmRemaining: -1500, dateDue: null };
    expect(templateDestaque(t, HOJE)).toBe("estourou 1.500 km");
  });

  it("vencido por km com data futura: fala do km, não mente com data que ainda não venceu", () => {
    const t: TemplateDue = { status: "vencido", kmRemaining: -320, dateDue: "2027-03-21" };
    expect(templateDestaque(t, HOJE)).toBe("estourou 320 km");
  });

  it("vencido vencendo pelos dois critérios: a data (passada) vem primeiro", () => {
    const t: TemplateDue = { status: "vencido", kmRemaining: -800, dateDue: "2026-08-01" };
    expect(templateDestaque(t, HOJE)).toBe("venceu 1 ago 2026");
  });

  it("vencido sem data nem km: só 'venceu'", () => {
    const t: TemplateDue = { status: "vencido", kmRemaining: null, dateDue: null };
    expect(templateDestaque(t, HOJE)).toBe("venceu");
  });

  it("vence em breve por tempo e por km", () => {
    expect(
      templateDestaque({ status: "vence_em_breve", kmRemaining: null, dateDue: "2027-03-21" }, HOJE),
    ).toBe("vence em 21 mar 2027");
    expect(
      templateDestaque({ status: "vence_em_breve", kmRemaining: 1500, dateDue: null }, HOJE),
    ).toBe("vence em 1.500 km");
  });

  it("ok carrega o próximo vencimento que existir", () => {
    expect(templateDestaque({ status: "ok", kmRemaining: 5000, dateDue: null }, HOJE)).toBe(
      "ok · vence em 5.000 km",
    );
    expect(templateDestaque({ status: "ok", kmRemaining: null, dateDue: "2027-03-21" }, HOJE)).toBe(
      "ok · vence em 21 mar 2027",
    );
    expect(templateDestaque({ status: "ok", kmRemaining: null, dateDue: null }, HOJE)).toBe("ok");
  });

  it("status null não gera destaque", () => {
    expect(templateDestaque({ status: null, kmRemaining: 10, dateDue: "2027-01-01" }, HOJE)).toBeNull();
  });
});

describe("worstTemplate", () => {
  it("vencido vence vence_em_breve; ok é ignorado", () => {
    const breves: TemplateDue = { status: "vence_em_breve", kmRemaining: null, dateDue: "2026-10-10" };
    const vencido: TemplateDue = { status: "vencido", kmRemaining: null, dateDue: "2026-12-01" };
    const ok: TemplateDue = { status: "ok", kmRemaining: 9000, dateDue: "2026-10-01" };
    expect(worstTemplate([ok, breves, vencido])).toBe(vencido);
  });

  it("entre vencidos, a data mais antiga é a pior", () => {
    const maisAntigo: TemplateDue = { status: "vencido", kmRemaining: null, dateDue: "2026-07-01" };
    const menosAntigo: TemplateDue = { status: "vencido", kmRemaining: null, dateDue: "2026-09-01" };
    expect(worstTemplate([menosAntigo, maisAntigo])).toBe(maisAntigo);
  });

  it("sem data, o menor kmRemaining (mais negativo) é o pior", () => {
    const pior: TemplateDue = { status: "vencido", kmRemaining: -2000, dateDue: null };
    const menosPior: TemplateDue = { status: "vencido", kmRemaining: -200, dateDue: null };
    expect(worstTemplate([menosPior, pior])).toBe(pior);
  });

  it("entre breves, a data que chega primeiro é a pior", () => {
    const proximo: TemplateDue = { status: "vence_em_breve", kmRemaining: null, dateDue: "2026-10-05" };
    const longe: TemplateDue = { status: "vence_em_breve", kmRemaining: null, dateDue: "2027-01-05" };
    expect(worstTemplate([longe, proximo])).toBe(proximo);
  });

  it("só ok (ou vazio) → null", () => {
    expect(worstTemplate([{ status: "ok", kmRemaining: 1, dateDue: null }])).toBeNull();
    expect(worstTemplate([])).toBeNull();
  });
});

describe("todayIso", () => {
  it("data local com zero à esquerda no mês/dia", () => {
    expect(todayIso(new Date(2026, 9, 2))).toBe("2026-10-02");
    expect(todayIso(new Date(2027, 0, 5))).toBe("2027-01-05");
  });
});
