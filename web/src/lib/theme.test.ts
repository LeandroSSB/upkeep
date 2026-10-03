// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { applyTheme, isThemePref, readThemePref, resolveTheme, watchSystemTheme } from "./theme";

// jsdom não implementa matchMedia — stub com o .matches controlável por teste
// (resolveTheme("auto") consulta; watchSystemTheme registra "change" listeners,
// que os testes disparam manualmente).
let prefersDark = false;
let mqlListeners: Array<() => void> = [];
beforeEach(() => {
  prefersDark = false;
  mqlListeners = [];
  vi.stubGlobal(
    "matchMedia",
    vi.fn((query: string) => ({
      matches: query.includes("dark") && prefersDark,
      media: query,
      addEventListener: (_type: string, fn: () => void) => mqlListeners.push(fn),
      removeEventListener: (_type: string, fn: () => void) => {
        mqlListeners = mqlListeners.filter((l) => l !== fn);
      },
      addListener: () => {},
      removeListener: () => {},
    })),
  );
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  // meta theme-color como no index.html (applyTheme só a atualiza se existir)
  const meta = document.createElement("meta");
  meta.name = "theme-color";
  meta.content = "#F4F5F1";
  document.head.appendChild(meta);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe("isThemePref", () => {
  it("aceita os 3 estados e rejeita o resto", () => {
    for (const ok of ["auto", "light", "dark"]) expect(isThemePref(ok)).toBe(true);
    for (const bad of ["blue", "", null, undefined, "AUTO"]) {
      expect(isThemePref(bad)).toBe(false);
    }
  });
});

describe("readThemePref", () => {
  it("sem nada salvo (ou lixo) → auto", () => {
    expect(readThemePref()).toBe("auto");
    localStorage.setItem("upkeep-theme", "sepia");
    expect(readThemePref()).toBe("auto");
  });

  it("devolve a preferência salva", () => {
    localStorage.setItem("upkeep-theme", "dark");
    expect(readThemePref()).toBe("dark");
  });
});

describe("resolveTheme", () => {
  it("explícito não consulta o SO", () => {
    expect(resolveTheme("light")).toBe("light");
    expect(resolveTheme("dark")).toBe("dark");
  });

  it("auto segue o SO", () => {
    prefersDark = false;
    expect(resolveTheme("auto")).toBe("light");
    prefersDark = true;
    expect(resolveTheme("auto")).toBe("dark");
  });
});

describe("applyTheme", () => {
  it("dark: seta data-theme, meta escura e persiste", () => {
    applyTheme("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute("content")).toBe("#14171A");
    expect(localStorage.getItem("upkeep-theme")).toBe("dark");
  });

  it("light: seta data-theme, meta clara e persiste", () => {
    applyTheme("light");
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute("content")).toBe("#F4F5F1");
    expect(localStorage.getItem("upkeep-theme")).toBe("light");
  });

  it("auto: REMOVE o atributo e o meta segue o SO (aqui: claro)", () => {
    applyTheme("dark"); // deixa um atributo para provar a remoção
    prefersDark = false;
    applyTheme("auto");
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute("content")).toBe("#F4F5F1");
    expect(localStorage.getItem("upkeep-theme")).toBe("auto");
  });

  it("auto com SO escuro: sem atributo, meta escura", () => {
    prefersDark = true;
    applyTheme("auto");
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute("content")).toBe("#14171A");
  });
});

describe("watchSystemTheme", () => {
  const metaContent = () =>
    document.querySelector('meta[name="theme-color"]')?.getAttribute("content");
  const fireSystemChange = () => mqlListeners.forEach((l) => l());

  it("pref auto: troca do SO re-resolve o meta (claro→escuro→claro) e chama onChange", () => {
    const onChange = vi.fn();
    const stop = watchSystemTheme(onChange);

    prefersDark = true; // anoiteceu
    fireSystemChange();
    expect(metaContent()).toBe("#14171A");
    expect(onChange).toHaveBeenCalledTimes(1);

    prefersDark = false; // amanheceu
    fireSystemChange();
    expect(metaContent()).toBe("#F4F5F1");
    expect(onChange).toHaveBeenCalledTimes(2);

    stop();
  });

  it("pref explícita: troca do SO é no-op (meta intocado, onChange não roda)", () => {
    applyTheme("dark"); // meta travado em #14171A pela escolha do usuário
    const before = metaContent();
    const onChange = vi.fn();
    const stop = watchSystemTheme(onChange);

    prefersDark = false;
    fireSystemChange();
    expect(metaContent()).toBe(before);
    expect(onChange).not.toHaveBeenCalled();

    stop();
  });

  it("disposer remove o listener", () => {
    const stop = watchSystemTheme();
    expect(mqlListeners).toHaveLength(1);
    stop();
    expect(mqlListeners).toHaveLength(0);
  });

  it("sem matchMedia (SSR/jsdom cru): não explode e devolve no-op", () => {
    const realMatchMedia = window.matchMedia;
    vi.stubGlobal("matchMedia", undefined);
    expect(() => watchSystemTheme()).not.toThrow();
    vi.stubGlobal("matchMedia", realMatchMedia);
  });
});
