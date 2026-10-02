import { apiFetch, setSession } from "./client";

export interface User {
  id: string;
  email: string;
}

/** GET /me devolve o user logado (+ ntfyTopic, usado pela tela de Ajustes). */
export interface Me extends User {
  ntfyTopic: string | null;
}

interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  user: User;
}

async function authPost(path: string, email: string, password: string): Promise<AuthResponse> {
  return apiFetch<AuthResponse>(path, {
    method: "POST",
    auth: false, // não têm (nem precisam de) Bearer; 401 aqui é credencial errada
    body: JSON.stringify({ email, password }),
  });
}

export async function login(email: string, password: string): Promise<User> {
  const res = await authPost("/auth/login", email, password);
  setSession(res.accessToken, res.refreshToken);
  return res.user;
}

export async function register(email: string, password: string): Promise<User> {
  const res = await authPost("/auth/register", email, password);
  setSession(res.accessToken, res.refreshToken);
  return res.user;
}

/** GET /me — dados da conta (tela de Ajustes). */
export async function getMe(): Promise<Me> {
  return apiFetch<Me>("/me");
}

/**
 * PUT /me/ntfy-topic. String vazia/whitespace LIMPA o tópico (operação válida);
 * o servidor trim e valida [a-zA-Z0-9_-]{1,64}. Devolve o /me atualizado.
 */
export async function updateNtfyTopic(ntfyTopic: string): Promise<Me> {
  return apiFetch<Me>("/me/ntfy-topic", {
    method: "PUT",
    body: JSON.stringify({ ntfyTopic }),
  });
}
