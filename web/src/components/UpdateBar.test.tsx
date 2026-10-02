// @vitest-environment jsdom
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { getSwRegistration, UPDATE_EVENT } from "../sw-register";
import { UpdateBar } from "./UpdateBar";

// O registration (com o worker em waiting) vem do módulo sw-register — mock
// parcial preserva UPDATE_EVENT real e troca só o getter.
vi.mock("../sw-register", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../sw-register")>();
  return { ...actual, getSwRegistration: vi.fn() };
});

// jsdom não tem navigator.serviceWorker — stub mínimo do que o reload() toca.
const swAddEventListener = vi.fn();
beforeEach(() => {
  Object.defineProperty(window.navigator, "serviceWorker", {
    value: { addEventListener: swAddEventListener },
    configurable: true,
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  // serviceWorker é read-only no lib.dom — limpa redefinindo (delete não compila).
  Object.defineProperty(window.navigator, "serviceWorker", {
    value: undefined,
    configurable: true,
  });
});

describe("UpdateBar", () => {
  it("invisível até o evento upkeep-update", () => {
    render(<UpdateBar />);
    expect(screen.queryByText("Nova versão disponível")).toBeNull();
  });

  it("evento → barra visível; clique manda SKIP_WAITING pro worker em waiting", () => {
    const postMessage = vi.fn();
    vi.mocked(getSwRegistration).mockReturnValue({
      waiting: { postMessage },
    } as unknown as ServiceWorkerRegistration);

    render(<UpdateBar />);
    // act: o listener é um evento nativo — sem ele o setState fica em batch
    // pendente e o DOM ainda não atualizou na hora da asserção.
    act(() => {
      window.dispatchEvent(new Event(UPDATE_EVENT));
    });

    expect(screen.getByText("Nova versão disponível")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Recarregar" }));
    expect(postMessage).toHaveBeenCalledWith({ type: "SKIP_WAITING" });
  });
});
