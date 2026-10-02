import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { apiFetch, onSessionCleared, refresh, setSession } from "../api/client";
import { login as loginRequest, register as registerRequest, type Me, type User } from "../api/auth";

const REFRESH_KEY = "upkeep-refresh";

interface Session {
  user: User | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  // Bootstrap: refresh salvo → rota → pega o user no /me (o refresh não devolve user).
  useEffect(() => {
    let cancelled = false;
    const stored = localStorage.getItem(REFRESH_KEY);
    if (!stored) {
      setLoading(false);
      return;
    }
    setSession(null, stored);
    void (async () => {
      if (await refresh()) {
        try {
          const me = await apiFetch<Me>("/me");
          if (!cancelled) setUser(me);
        } catch {
          setSession(null, null); // /me não deu conta: sessão inútil, limpa
        }
      }
      if (!cancelled) setLoading(false);
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // 401 terminal em qualquer chamada (refresh falhou) → logout limpo para /entrar.
  useEffect(() => onSessionCleared(() => setUser(null)), []);

  const login = async (email: string, password: string) => {
    setUser(await loginRequest(email, password));
  };

  const register = async (email: string, password: string) => {
    setUser(await registerRequest(email, password));
  };

  const logout = () => {
    // Revoga o refresh no servidor ANTES de limpar (fire-and-forget: falha é
    // ignorada — offline/já revogado, o token local morre de qualquer jeito).
    // Lê do localStorage: mesma fonte de verdade do rotate (outra aba pode ter
    // rotacionado depois que este módulo carregou).
    const current = localStorage.getItem(REFRESH_KEY);
    if (current)
      void apiFetch("/auth/logout", {
        method: "POST",
        auth: false,
        body: JSON.stringify({ refreshToken: current }),
      }).catch(() => {});
    setSession(null, null);
    setUser(null);
  };

  return (
    <SessionContext.Provider value={{ user, loading, login, register, logout }}>
      {children}
    </SessionContext.Provider>
  );
}

export function useSession(): Session {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error("useSession precisa estar dentro de <SessionProvider>");
  return ctx;
}
