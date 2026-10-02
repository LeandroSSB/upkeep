// Cliente HTTP do SPA. Todo caminho é relativo: "/api" é prefixado AQUI — em dev
// o proxy do vite faz o strip, em produção o nginx repassa com o prefixo já certo.

export class ApiError extends Error {
  constructor(
    public status: number,
    public title: string,
    public errors?: Record<string, string[]>,
  ) {
    super(title);
    this.name = "ApiError";
  }
}

const REFRESH_KEY = "upkeep-refresh";

// access vive só em memória; refresh persiste (localStorage) para sobreviver ao reload.
let accessToken: string | null = null;
let refreshToken: string | null = null;

export function setSession(access: string | null, refresh: string | null): void {
  accessToken = access;
  refreshToken = refresh;
  if (refresh !== null) localStorage.setItem(REFRESH_KEY, refresh);
  else localStorage.removeItem(REFRESH_KEY);
}

// Quem clear a sessão por 401 terminal avisa os inscritos (SessionProvider zera o
// user e o RequireAuth devolve para /entrar) — "logout limpo" sem acoplamento a React.
type SessionListener = () => void;
const clearedListeners = new Set<SessionListener>();

export function onSessionCleared(listener: SessionListener): () => void {
  clearedListeners.add(listener);
  return () => clearedListeners.delete(listener);
}

function notifyCleared(): void {
  for (const listener of clearedListeners) listener();
}

/** Um único refresh em voo: duas 401 simultâneas não podem rodar duas rotações. */
let refreshInFlight: Promise<boolean> | null = null;

/** POST /auth/refresh com o token atual; true = sessão renovada (e rotacionada). */
export function refresh(): Promise<boolean> {
  if (!refreshInFlight) {
    refreshInFlight = rotate().finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

async function rotate(): Promise<boolean> {
  if (!refreshToken) return false;
  try {
    const res = await fetch("/api/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    if (!res.ok) {
      setSession(null, null); // refresh inválido/expirado: não há o que recuperar
      return false;
    }
    const tokens = (await res.json()) as { accessToken: string; refreshToken: string };
    setSession(tokens.accessToken, tokens.refreshToken);
    return true;
  } catch {
    return false; // rede fora: quem chamou decide (apiFetch limpa e lança 401)
  }
}

function buildInit(init: RequestInit & { auth?: boolean }): RequestInit {
  const { auth, headers, ...rest } = init;
  const merged = new Headers(headers);
  if (rest.body != null) merged.set("Content-Type", "application/json");
  if (auth !== false && accessToken) merged.set("Authorization", `Bearer ${accessToken}`);
  return { ...rest, headers: merged };
}

async function toApiError(res: Response): Promise<ApiError> {
  let title = `Erro ${res.status}`;
  let errors: Record<string, string[]> | undefined;
  try {
    const body = (await res.json()) as { title?: string; errors?: Record<string, string[]> };
    if (typeof body?.title === "string") title = body.title;
    if (body?.errors && typeof body.errors === "object") errors = body.errors;
  } catch {
    // corpo vazio (401 da API) ou não-JSON: fica o fallback
  }
  return new ApiError(res.status, title, errors);
}

async function request<T>(path: string, init: RequestInit & { auth?: boolean }, retried: boolean): Promise<T> {
  const { auth } = init;

  let res: Response;
  try {
    res = await fetch(`/api${path}`, buildInit(init));
  } catch {
    throw new ApiError(0, "Sem conexão com o servidor");
  }

  if (res.status === 401 && auth !== false) {
    // uma única tentativa de refresh, depois UMA retry da original
    if (!retried && refreshToken && (await refresh())) {
      return request<T>(path, init, true); // buildInit refeita: Authorization novo
    }
    setSession(null, null);
    notifyCleared();
    throw new ApiError(401, "Sessão expirada. Entre de novo.");
  }

  if (res.ok) {
    return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
  }

  throw await toApiError(res);
}

export function apiFetch<T>(path: string, init?: RequestInit & { auth?: boolean }): Promise<T> {
  return request<T>(path, init ?? {}, false);
}
