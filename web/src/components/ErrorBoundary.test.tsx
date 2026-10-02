// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { ErrorBoundary } from "./ErrorBoundary";

function Thrower(): never {
  throw new Error("boom");
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("ErrorBoundary", () => {
  it("throw no render → fallback com mensagem e botão, e loga o erro no console", () => {
    // mock: além de limpar o output, verifica o console.error de diagnóstico
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <Thrower />
      </ErrorBoundary>,
    );

    expect(screen.getByRole("alert")).toBeTruthy();
    expect(screen.getByText("Algo quebrou aqui.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Recarregar" })).toBeTruthy();
    expect(
      errorSpy.mock.calls.some((c) => String(c[0]).includes("Erro não tratado na UI")),
    ).toBe(true);
  });

  it("sem erro → renderiza os children normalmente", () => {
    render(<ErrorBoundary>tudo certo</ErrorBoundary>);
    expect(screen.getByText("tudo certo")).toBeTruthy();
  });
});
