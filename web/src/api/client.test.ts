// @vitest-environment jsdom
// client.ts vive de fetch + localStorage: jsdom (já devDep) dá os dois sem stub manual.
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiFetch, setSession } from "./client";

const fetchMock = vi.fn<typeof fetch>();
vi.stubGlobal("fetch", fetchMock);

/** Response mínimo que o client usa (ok/status/json) — corpo undefined = body vazio (401 da API). */
function res(status: number, body?: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => {
      if (body === undefined) throw new SyntaxError("body vazio");
      return body;
    },
  } as Response;
}

function authHeader(init?: RequestInit): string {
  return init?.headers ? new Headers(init.headers).get("Authorization") ?? "" : "";
}

type FetchCall = Parameters<typeof fetch>;

function callsTo(path: string): FetchCall[] {
  return fetchMock.mock.calls.filter((c) => String(c[0]) === `/api${path}`) as FetchCall[];
}

afterEach(() => {
  setSession(null, null); // zera tokens do módulo + limpa localStorage
  vi.resetAllMocks();
});

describe("apiFetch — rotação de refresh", () => {
  it("401 → refresh 200 → retenta a original com o novo token e devolve os dados", async () => {
    setSession("access-antigo", "refresh-1");
    fetchMock.mockImplementation(async (input, init) => {
      if (String(input) === "/api/auth/refresh") {
        return res(200, { accessToken: "access-novo", refreshToken: "refresh-2" });
      }
      return authHeader(init).includes("access-novo")
        ? res(200, { itens: [1, 2] })
        : res(401);
    });

    const data = await apiFetch<{ itens: number[] }>("/assets");

    expect(data).toEqual({ itens: [1, 2] });
    // um único refresh, com o refresh token da sessão no corpo
    expect(callsTo("/auth/refresh")).toHaveLength(1);
    const refreshCall = callsTo("/auth/refresh")[0];
    expect(JSON.parse(String(refreshCall[1]!.body))).toEqual({ refreshToken: "refresh-1" });
    // a retry saiu com o Authorization NOVO (header reconstruído, não o do 401)
    expect(authHeader(fetchMock.mock.calls.at(-1)![1] as RequestInit)).toBe("Bearer access-novo");
    // rotação persistida
    expect(localStorage.getItem("upkeep-refresh")).toBe("refresh-2");
  });

  it("401 → refresh 401 → lança ApiError 401 e limpa a sessão", async () => {
    setSession("access-antigo", "refresh-1");
    fetchMock.mockImplementation(async (input) =>
      String(input) === "/api/auth/refresh" ? res(401) : res(401));

    const err = await apiFetch("/assets").catch((e) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(401);
    expect(localStorage.getItem("upkeep-refresh")).toBeNull();
  });

  it("duas 401 concorrentes disparam UM único /auth/refresh", async () => {
    setSession("access-antigo", "refresh-1");
    fetchMock.mockImplementation(async (input, init) => {
      if (String(input) === "/api/auth/refresh") {
        await new Promise((r) => setTimeout(r, 10)); // janela de concorrência
        return res(200, { accessToken: "access-novo", refreshToken: "refresh-2" });
      }
      return authHeader(init).includes("access-novo") ? res(200, { ok: true }) : res(401);
    });

    const [a, b] = await Promise.all([apiFetch("/assets"), apiFetch("/me")]);

    expect(a).toEqual({ ok: true });
    expect(b).toEqual({ ok: true });
    expect(callsTo("/auth/refresh")).toHaveLength(1);
  });

  it("401 em aba B envia o refresh MAIS NOVO do localStorage (aba A rotacionou antes)", async () => {
    // cenário duas abas: esta aba (B) tem refresh-1 em memória, mas a aba A já
    // rotacionou e persistiu refresh-2 — enviar refresh-1 revogaria a sessão toda.
    setSession("access-1", "refresh-1");
    localStorage.setItem("upkeep-refresh", "refresh-2");
    fetchMock.mockImplementation(async (input, init) => {
      if (String(input) === "/api/auth/refresh") {
        return res(200, { accessToken: "access-2", refreshToken: "refresh-3" });
      }
      return authHeader(init).includes("access-2") ? res(200, { ok: true }) : res(401);
    });

    const data = await apiFetch("/assets");

    expect(data).toEqual({ ok: true });
    const refreshCall = callsTo("/auth/refresh")[0];
    expect(JSON.parse(String(refreshCall[1]!.body))).toEqual({ refreshToken: "refresh-2" });
  });

  it("com auth:false não tenta refresh em 401 (login com senha errada)", async () => {
    setSession("access-antigo", "refresh-1");
    fetchMock.mockResolvedValue(res(401));

    const err = await apiFetch("/auth/login", {
      method: "POST",
      auth: false,
      body: JSON.stringify({ email: "a@b.c", password: "senhaerrada" }),
    }).catch((e) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(401);
    expect(callsTo("/auth/refresh")).toHaveLength(0);
    expect(localStorage.getItem("upkeep-refresh")).toBe("refresh-1"); // sessão intacta
  });
});

describe("apiFetch — erros e formatos", () => {
  it("preserva title e errors do ProblemDetails (400 de validação)", async () => {
    fetchMock.mockResolvedValue(
      res(400, {
        title: "Um ou mais erros de validação ocorreram.",
        errors: { Email: ["E-mail inválido"], Password: ["A senha precisa ter pelo menos 8 caracteres."] },
      }),
    );

    const err = await apiFetch("/auth/register", {
      method: "POST",
      body: JSON.stringify({ email: "x", password: "1" }),
    }).catch((e) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(400);
    expect((err as ApiError).title).toBe("Um ou mais erros de validação ocorreram.");
    expect((err as ApiError).errors?.["Email"]).toEqual(["E-mail inválido"]);
    expect((err as ApiError).errors?.["Password"]).toEqual([
      "A senha precisa ter pelo menos 8 caracteres.",
    ]);
  });

  it("204 → undefined", async () => {
    fetchMock.mockResolvedValue(res(204));
    await expect(apiFetch("/assets/1")).resolves.toBeUndefined();
  });

  it("falha de rede → ApiError(0, 'Sem conexão com o servidor')", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    const err = await apiFetch("/assets").catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(0);
    expect((err as ApiError).title).toBe("Sem conexão com o servidor");
  });
});
