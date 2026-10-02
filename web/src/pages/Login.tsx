import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { ApiError } from "../api/client";
import { useSession } from "../state/session";

type Mode = "entrar" | "criar";
type FieldErrors = { email?: string; password?: string };

const EMAIL_VAZIO = "Informe seu e-mail.";
const EMAIL_INVALIDO = "E-mail inválido.";
const SENHA_CURTA = "A senha precisa ter pelo menos 8 caracteres.";

export default function Login() {
  const { login, register } = useSession();
  const navigate = useNavigate();
  const [mode, setMode] = useState<Mode>("entrar");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  const isRegister = mode === "criar";

  function switchMode(next: Mode) {
    setMode(next);
    setFieldErrors({});
    setFormError(null);
  }

  function validate(): FieldErrors {
    const errs: FieldErrors = {};
    if (!email.trim()) errs.email = EMAIL_VAZIO;
    else if (!email.includes("@")) errs.email = EMAIL_INVALIDO;
    if (password.length < 8) errs.password = SENHA_CURTA;
    return errs;
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const errs = validate();
    setFieldErrors(errs);
    setFormError(null);
    if (errs.email || errs.password) return;

    setSending(true);
    try {
      if (isRegister) await register(email, password);
      else await login(email, password);
      navigate("/");
    } catch (err) {
      if (err instanceof ApiError) {
        // errors do ProblemDetails (chaves "Email"/"Password" na API) sob os campos
        const server: FieldErrors = {};
        for (const [key, messages] of Object.entries(err.errors ?? {})) {
          const field = key.toLowerCase() === "email" ? "email" : key.toLowerCase() === "password" ? "password" : null;
          if (field && messages.length > 0) server[field] = messages[0];
        }
        setFieldErrors(server);
        setFormError(
          err.status === 401 && !isRegister
            ? "E-mail ou senha incorretos." // 401 do login vem sem corpo
            : err.title,
        );
      } else {
        setFormError("Algo deu errado. Tente de novo.");
      }
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="shell">
      <header className="app-header">
        <span className="wordmark">upkeep</span>
        <span className="dot" aria-hidden="true" />
      </header>

      <main>
        <h1>{isRegister ? "Criar conta" : "Entrar"}</h1>
        <p className="login-switch">
          {isRegister ? (
            <>Já tem conta? <button type="button" className="link" onClick={() => switchMode("entrar")}>Entrar</button></>
          ) : (
            <>Primeira vez? <button type="button" className="link" onClick={() => switchMode("criar")}>Criar conta</button></>
          )}
        </p>

        <form onSubmit={onSubmit} noValidate>
          <div className="field">
            <label htmlFor="email">E-mail</label>
            <input
              id="email"
              name="email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              aria-invalid={fieldErrors.email !== undefined}
            />
            {fieldErrors.email && <p className="field__error">{fieldErrors.email}</p>}
          </div>

          <div className="field">
            <label htmlFor="password">Senha</label>
            <input
              id="password"
              name="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete={isRegister ? "new-password" : "current-password"}
              aria-invalid={fieldErrors.password !== undefined}
            />
            {fieldErrors.password && <p className="field__error">{fieldErrors.password}</p>}
          </div>

          {formError && <p className="form-error" role="alert">{formError}</p>}

          <button type="submit" className="btn btn--primary" disabled={sending}>
            {isRegister ? "Criar conta" : "Entrar"}
          </button>
        </form>
      </main>
    </div>
  );
}
