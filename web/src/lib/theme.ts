// Tema do app (M8): preferência de 3 estados (auto/claro/escuro) → atributo
// data-theme no <html>, meta theme-color e localStorage. O bootstrap anti-flash
// (script inline do index.html) lê o MESMO storage antes do bundle; este módulo
// é a fonte p/ o toggle dos Ajustes e para testes.
export type ThemePref = "auto" | "light" | "dark";

const STORAGE_KEY = "upkeep-theme";

// meta theme-color acompanha o tema RESOLVIDO (cor da barra do navegador/PWA)
export const THEME_COLORS = { light: "#F4F5F1", dark: "#14171A" } as const;

export function isThemePref(value: unknown): value is ThemePref {
  return value === "auto" || value === "light" || value === "dark";
}

// Preferência persistida; ausente/lixo/storage bloqueado → "auto" (default).
export function readThemePref(): ThemePref {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return isThemePref(stored) ? stored : "auto";
  } catch {
    return "auto";
  }
}

// "auto" pergunta ao SO; explícito é o próprio valor.
export function resolveTheme(pref: ThemePref): "light" | "dark" {
  if (pref !== "auto") return pref;
  return typeof window.matchMedia === "function" &&
    window.matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

// Aplica a preferência: o atributo só existe quando EXPLÍCITO — em "auto" ele é
// REMOVIDO e o @media (prefers-color-scheme) do tokens.css resolve (trocar o
// tema do SO passa a valer na hora, sem reload). Persiste a escolha.
export function applyTheme(pref: ThemePref): void {
  if (pref === "auto") delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = pref;

  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute("content", THEME_COLORS[resolveTheme(pref)]);

  try {
    localStorage.setItem(STORAGE_KEY, pref);
  } catch {
    // storage cheio/bloqueado (modo privado): o tema aplica, só não persiste
  }
}
