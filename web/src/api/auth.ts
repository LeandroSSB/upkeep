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
