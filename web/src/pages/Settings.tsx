// Ajustes da conta — e-mail (leitura), tópico ntfy para lembretes e sair.
// Toast reutilizado de components/Toast (mesmo do AssetDetail).
import { useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { getMe, testNotification, updateNtfyTopic, type Me } from "../api/auth";
import { ApiError } from "../api/client";
import { AppShell } from "../components/AppShell";
import { Field } from "../components/Field";
import { Toast } from "../components/Toast";
import { fieldMessage, leftoverMessage } from "../lib/formErrors";
import { useSession } from "../state/session";

// espelho do validator do servidor (MeEndpoints) — inválido nem sai do browser
const TOPIC_PATTERN = /^[a-zA-Z0-9_-]{1,64}$/;
const TOPIC_INVALIDO = "Tópico inválido: use apenas letras, números, - e _ (máx 64)";

type Phase =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "ready"; me: Me };

export default function Settings() {
  const [phase, setPhase] = useState<Phase>({ kind: "loading" });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setPhase({ kind: "loading" });
    getMe()
      .then((me) => {
        if (!cancelled) setPhase({ kind: "ready", me });
      })
      .catch((err) => {
        if (!cancelled) {
          setPhase({
            kind: "error",
            message: err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.",
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [attempt]);

  return (
    <AppShell>
      <h1>Ajustes</h1>

        {phase.kind === "loading" && <p className="loading">Carregando…</p>}

        {phase.kind === "error" && (
          <div className="load-error">
            <p className="form-error" role="alert">
              {phase.message}
            </p>
            <button type="button" className="btn" onClick={() => setAttempt((n) => n + 1)}>
              Tentar de novo
            </button>
          </div>
        )}

        {phase.kind === "ready" && <SettingsForm me={phase.me} />}
    </AppShell>
  );
}

function SettingsForm({ me: initialMe }: { me: Me }) {
  const { logout } = useSession();
  const navigate = useNavigate();
  // me muda só depois de um salvar bem-sucedido (resposta do PUT)
  const [me, setMe] = useState(initialMe);
  const [topic, setTopic] = useState(initialMe.ntfyTopic ?? "");
  const [topicError, setTopicError] = useState<string | undefined>();
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testError, setTestError] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  function validate(value: string): string | undefined {
    const t = value.trim();
    if (t === "") return undefined; // vazio limpa — operação válida
    return TOPIC_PATTERN.test(t) ? undefined : TOPIC_INVALIDO;
  }

  async function onSubmit(ev: FormEvent) {
    ev.preventDefault();
    const error = validate(topic);
    setTopicError(error);
    setFormError(null);
    if (error) return;

    setSaving(true);
    try {
      const updated = await updateNtfyTopic(topic.trim());
      setMe(updated);
      setTopic(updated.ntfyTopic ?? "");
      setToast("Ajustes salvos");
    } catch (err) {
      if (err instanceof ApiError) {
        setTopicError(fieldMessage(err.errors, "ntfyTopic"));
        setFormError(leftoverMessage(err.errors, ["ntfyTopic"], err.title));
      } else {
        setFormError("Algo deu errado. Tente de novo.");
      }
    } finally {
      setSaving(false);
    }
  }

  // O teste dispara para o tópico SALVO no servidor — só faz sentido com o campo
  // preenchido (e válido), senão o usuário clicaria sem ter nada configurado.
  const topicTrimmed = topic.trim();
  const topicPreenchidoValido = topicTrimmed !== "" && TOPIC_PATTERN.test(topicTrimmed);

  async function onTestNotification() {
    setTesting(true);
    setTestError(null);
    try {
      await testNotification();
      setToast("Notificação enviada — confira o celular");
    } catch (err) {
      setTestError(err instanceof ApiError ? err.title : "Algo deu errado. Tente de novo.");
    } finally {
      setTesting(false);
    }
  }

  function onLogout() {
    logout();
    navigate("/entrar", { replace: true });
  }

  return (
    <>
      <form onSubmit={onSubmit} noValidate>
        <Field htmlFor="email" label="E-mail">
          <input id="email" type="email" value={me.email} readOnly />
        </Field>

        <Field htmlFor="ntfyTopic" label="Tópico ntfy (opcional)" error={topicError}>
          <input
            id="ntfyTopic"
            name="ntfyTopic"
            type="text"
            value={topic}
            maxLength={64}
            placeholder="ex.: meus-lembretes"
            onChange={(e) => {
              setTopic(e.target.value);
              setTopicError(undefined); // erro do servidor/validação não sobrevive à edição
              setTestError(null);
            }}
            aria-invalid={topicError !== undefined}
          />
        </Field>

        {me.ntfyTopic && (
          <div className="settings-topic">
            <a href={`https://ntfy.sh/${me.ntfyTopic}`} target="_blank" rel="noreferrer">
              abrir ntfy.sh/{me.ntfyTopic}
            </a>
            <p className="settings-topic__hint">
              Instale o app ntfy e assine o tópico para receber lembretes.
            </p>
          </div>
        )}

        {formError && (
          <p className="form-error" role="alert">
            {formError}
          </p>
        )}

        <div className="form-actions">
          <button type="submit" className="btn btn--primary" disabled={saving}>
            {saving ? "Salvando…" : "Salvar"}
          </button>
          <button
            type="button"
            className="btn"
            onClick={onTestNotification}
            disabled={!topicPreenchidoValido || testing}
          >
            {testing ? "Enviando…" : "Testar notificação"}
          </button>
        </div>

        {testError && (
          <p className="form-error" role="alert">
            {testError}
          </p>
        )}
      </form>

      <div className="settings-footer">
        <button type="button" className="link link--danger" onClick={onLogout}>
          Sair
        </button>
      </div>

      {toast && <Toast message={toast} onHide={() => setToast(null)} />}
    </>
  );
}
